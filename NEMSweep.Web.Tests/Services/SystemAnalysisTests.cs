using AwesomeAssertions;
using NEMSweep.Contracts;
using NEMSweep.Web.Services.Insights;

namespace NEMSweep.Web.Tests.Services;

public sealed class SystemAnalysisTests
{
    [Fact]
    public void Build_ReadsOneProfilePerRegionInTheArtifactsOwnOrder()
    {
        SystemAnalysis analysis = Analyse(TwoRegionSystem());

        analysis.Regions.Select(region => region.RegionId).Should().Equal("NSW1", "VIC1");
        analysis.Cheapest!.RegionId.Should().Be("VIC1");
        analysis.Dearest!.RegionId.Should().Be("NSW1");
    }

    [Fact]
    public void Build_StatesTheSpreadBetweenTheCheapestAndDearestRegion()
    {
        SystemAnalysis analysis = Analyse(TwoRegionSystem());

        Finding finding = analysis.Findings.Should()
            .ContainSingle(finding => finding.Headline.Contains("more than")).Subject;
        finding.Metric.Should().Be("$21.80");
        finding.Detail.Should().Contain("$167.27").And.Contain("$145.47");
    }

    [Fact]
    public void Build_DistinguishesTheSystemFigureFromThePlainAverageOfTheRegions()
    {
        SystemAnalysis analysis = Analyse(TwoRegionSystem());

        analysis.MeanRegionalSlcoe.Should().Be(156.37m);
        analysis.Findings.Should().ContainSingle(finding =>
            finding.Headline.Contains("demand-weighted, not an average"));
    }

    [Fact]
    public void Build_NamesTheRegionCarryingTheUnservedEnergy()
    {
        SystemAnalysis analysis = Analyse(TwoRegionSystem());

        Finding finding = analysis.Findings.Should()
            .ContainSingle(finding => finding.Headline.Contains("Unserved energy is concentrated")).Subject;
        finding.Headline.Should().Contain("Victoria");
        finding.Detail.Should().Contain("All of it fell in Victoria (VIC1)");
    }

    [Fact]
    public void Build_ReportsAServedYearRatherThanInventingAShortfallFinding()
    {
        SystemDispatchResultsDTO system = TwoRegionSystem(systemUnservedMwh: 0, vicUnservedMwh: 0);

        SystemAnalysis analysis = Analyse(system);

        analysis.Findings.Should().ContainSingle(finding =>
            finding.Headline == "Every hour of demand was served"
            && finding.Tone == FindingTone.Favourable);
    }

    [Fact]
    public void Build_SeparatesSpillingFromRunningShortWhenARegionDoesBoth()
    {
        SystemAnalysis analysis = Analyse(TwoRegionSystem());

        analysis.Findings.Should().ContainSingle(finding =>
            finding.Headline == "Victoria spills energy and still runs short");
    }

    [Fact]
    public void Build_IntegratesInterconnectorFlowIntoEnergyRatherThanSummingPowers()
    {
        SystemDispatchResultsDTO system = TwoRegionSystem(resolutionHours: 2);

        LinkFlowEvidence flow = Analyse(system).Links[0].Flow!;

        // 1,000 MW held for one of three two-hour intervals is 2,000 MWh, not 1,000.
        flow.EnergyMwh.Should().Be(2000);
        flow.LossesMwh.Should().Be(100);
        flow.FlowingIntervals.Should().Be(1);
        flow.PeakFlowMw.Should().Be(1000);
    }

    [Fact]
    public void Build_DrawsEveryLinkTheRunDeclaresBeforeAnyFlowEvidenceIsRead()
    {
        SystemAnalysis analysis = SystemAnalysis.Build(SystemFacts.From(TwoRegionSystem()));

        // The topology declares both directions, so both are links whether or not either ran. A
        // null flow says the evidence has not been read; it is not a link that carried nothing.
        analysis.Links.Select(link => link.Id).Should().Equal("NSW1->VIC1", "VIC1->NSW1");
        analysis.Links.Should().OnlyContain(link => link.Flow == null);
        analysis.HasLinkEvidence.Should().BeFalse();
        analysis.FlowingLinks.Should().BeEmpty();
    }

    [Fact]
    public void WithLinkEvidence_MeasuresUtilisationAgainstTheCapacityTheTopologyDeclares()
    {
        SystemDispatchResultsDTO system = TwoRegionSystem();
        // An evidence block disagreeing with the topology about capacity: the row displays the
        // declared 1,000 MW, so the share it reports has to divide by that one.
        DispatchInterconnectorDTO mismatched = system.Interconnectors[0] with { CapacityMw = 500 };

        LinkFlowEvidence flow = SystemAnalysis.Build(SystemFacts.From(system))
            .WithLinkEvidence([mismatched], system.Resolution)
            .Links[0].Flow!;

        flow.CapacityMw.Should().Be(1000);
        // 1,000 MWh carried against 1,000 MW over three one-hour intervals.
        flow.CapacityFactor.Should().BeApproximately(1.0 / 3, 0.0001);
    }

    [Fact]
    public void WithLinkEvidence_LeavesADeclaredLinkTheEvidenceOmitsWithoutAFlow()
    {
        SystemDispatchResultsDTO system = TwoRegionSystem();

        SystemAnalysis analysis = SystemAnalysis.Build(SystemFacts.From(system))
            .WithLinkEvidence([system.Interconnectors[0]], system.Resolution);

        analysis.Links.Single(link => link.Id == "NSW1->VIC1").Flow.Should().NotBeNull();
        analysis.Links.Single(link => link.Id == "VIC1->NSW1").Flow.Should().BeNull();
    }

    [Fact]
    public void Build_ReportsTheDirectionOfTheBusiestLink()
    {
        SystemAnalysis analysis = Analyse(TwoRegionSystem());

        analysis.Findings.Should().ContainSingle(finding =>
            finding.Headline == "New South Wales underwrites Victoria");
    }

    [Fact]
    public void Build_NamesTheRegionTheSizingLoopHadToGrowStorageIn()
    {
        SystemAnalysis analysis = Analyse(TwoRegionSystem());

        Finding finding = analysis.Findings.Should()
            .ContainSingle(finding => finding.Headline.Contains("needed more storage")).Subject;
        finding.Headline.Should().Contain("Victoria");
        finding.Metric.Should().Be("3,529");
    }

    [Fact]
    public void Build_ClaimsNoStorageDivergenceWhenEveryRegionWasResized()
    {
        SystemDispatchResultsDTO system = TwoRegionSystem(nswResized: true);

        Analyse(system).Findings.Should().NotContain(finding =>
            finding.Headline.Contains("needed more storage"));
    }

    [Fact]
    public void Build_ReadsEachRegionsGenerationMixFromItsSummaryWithoutTheDetailArtifact()
    {
        SystemAnalysis analysis = Analyse(TwoRegionSystem());

        RegionProfile victoria = analysis.Regions.Single(region => region.RegionId == "VIC1");
        victoria.Mix.TotalMwh.Should().Be(44_665_751);
        victoria.Mix.RenewableShare.Should().BeApproximately(0.5841, 0.0001);
    }

    [Fact]
    public void Build_TakesTheSystemMixAsTheSumOfTheRegionalOnes()
    {
        SystemAnalysis analysis = Analyse(TwoRegionSystem());

        // Every generator sits in exactly one region, so the regional mixes add to the system's.
        analysis.SystemMix.TotalMwh.Should()
            .Be(analysis.Regions.Sum(region => region.Mix.TotalMwh));
        analysis.SystemMix.ByTechnology.Single(entry => entry.Technology == "Coal")
            .EnergyMwh.Should().Be(59_853_259);
    }

    [Fact]
    public void CurtailedShareOfAvailable_MeasuresSpillAgainstEverythingTheFleetCouldHaveDelivered()
    {
        SystemAnalysis analysis = Analyse(TwoRegionSystem());

        analysis.Regions.Single(region => region.RegionId == "VIC1")
            .CurtailedShareOfAvailable.Should().BeApproximately(0.0607, 0.0005);
    }

    /// <summary>
    /// The analysis as a page builds it: the compact overview first, then the interval evidence
    /// folded in when the artifact carrying it arrives.
    /// </summary>
    private static SystemAnalysis Analyse(SystemDispatchResultsDTO system) =>
        SystemAnalysis.Build(SystemFacts.From(system))
            .WithLinkEvidence(system.Interconnectors, system.Resolution);

    /// <summary>
    /// A two-region run shaped like the published one: a larger, dearer exporter and a smaller,
    /// cheaper importer that spills energy, runs short, and needs its storage grown.
    /// </summary>
    private static SystemDispatchResultsDTO TwoRegionSystem(
        double systemUnservedMwh = 738,
        double vicUnservedMwh = 738,
        bool nswResized = false,
        double resolutionHours = 1)
    {
        DispatchResultsDTO template = ArtifactFixtures.Results();
        RegionDispatchSummaryDTO nsw = new(
            Metrics(demandMwh: 66_275_989, deliveredMwh: 66_542_090, curtailedMwh: 1_855_528, unservedMwh: 0),
            new ReliabilityBasisDTO(0.002, 0, true, "NEM reliability standard"),
            Sizing(5515, nswResized ? 9000 : 5515, StorageOutcome(nswResized)),
            Cost(slcoe: 167.27m, generation: 164.08m, storage: 3.19m, total: 11_085_977_181m, netImported: -266_101),
            Mix(coal: 41_275_989, solar: 15_000_000, wind: 7_500_000, hydro: 2_766_101),
            "results-nsw1.json",
            "results-nsw1-overview.json");
        RegionDispatchSummaryDTO vic = new(
            Metrics(demandMwh: 44_977_270, deliveredMwh: 44_665_751, curtailedMwh: 2_891_385, unservedMwh: vicUnservedMwh),
            new ReliabilityBasisDTO(0.002, vicUnservedMwh > 0 ? 0.00164 : 0, true, "NEM reliability standard"),
            Sizing(3243.4, 6772, StorageSizingOutcome.Resized),
            Cost(slcoe: 145.47m, generation: 138.90m, storage: 6.57m, total: 6_542_903_141m, netImported: 252_796),
            Mix(coal: 18_577_270, solar: 14_000_000, wind: 11_000_000, hydro: 1_088_481),
            "results-vic1.json",
            "results-vic1-overview.json");

        return new SystemDispatchResultsDTO(
            ArtifactSchemaVersions.SystemDispatchResults,
            "run-1",
            template.Scenario.PeriodStart,
            template.Scenario.PeriodEnd,
            TimeSpan.FromHours(resolutionHours),
            ["NSW1", "VIC1"],
            new Dictionary<string, DispatchSourcesDTO>
            {
                ["NSW1"] = template.DataSources,
                ["VIC1"] = template.DataSources,
            },
            new Dictionary<string, RegionDispatchSummaryDTO> { ["NSW1"] = nsw, ["VIC1"] = vic },
            template.DataSeries,
            Metrics(
                demandMwh: 111_253_259,
                deliveredMwh: 111_207_841,
                curtailedMwh: 4_746_913,
                unservedMwh: systemUnservedMwh),
            new ReliabilityBasisDTO(0.002, systemUnservedMwh > 0 ? 0.00066 : 0, true, "NEM reliability standard"),
            Sizing(8758.4, 12287, StorageSizingOutcome.Resized),
            Cost(slcoe: 158.46m, generation: 153.90m, storage: 4.56m, total: 17_629_045_242m, netImported: 0),
            new DispatchTopologyDTO(
                ["NSW1", "VIC1"],
                [
                    new DispatchTopologyLinkDTO("NSW1->VIC1", "NSW1", "VIC1", 1000),
                    new DispatchTopologyLinkDTO("VIC1->NSW1", "VIC1", "NSW1", 1000),
                ]),
            [
                new DispatchInterconnectorDTO("NSW1->VIC1", "NSW1", "VIC1", 1000, [1000, 0, 0], [50, 0, 0]),
                new DispatchInterconnectorDTO("VIC1->NSW1", "VIC1", "NSW1", 1000, [0, 0, 0], [0, 0, 0]),
            ]);
    }

    private static StorageSizingOutcome StorageOutcome(bool resized) =>
        resized ? StorageSizingOutcome.Resized : StorageSizingOutcome.NotRequired;

    private static DispatchMetricsDTO Metrics(
        double demandMwh,
        double deliveredMwh,
        double curtailedMwh,
        double unservedMwh) => new(
        demandMwh,
        deliveredMwh,
        curtailedMwh,
        unservedMwh,
        demandMwh <= 0 ? 0 : 100 * unservedMwh / demandMwh,
        unservedMwh > 0 ? 2 : 0,
        1,
        unservedMwh > 0 ? 416.8 : 0,
        new IntervalPointersDTO(null, null, 0));

    private static StorageSizingOutcomeDTO Sizing(
        double initialEnergyMwh,
        double finalEnergyMwh,
        StorageSizingOutcome outcome) =>
        new(outcome, initialEnergyMwh, 940, finalEnergyMwh, 940, 100_000, 10_000, 3);

    private static DispatchCostDTO Cost(
        decimal slcoe,
        decimal generation,
        decimal storage,
        decimal total,
        double netImported) =>
        new(
            "calculated", 0, 0, total, generation, storage, slcoe, 0, 0,
            TransmissionCostStatus.NotModelled, netImported, []);

    private static Dictionary<string, double> Mix(
        double coal,
        double solar,
        double wind,
        double hydro) => new()
        {
            ["Coal"] = coal,
            ["Solar"] = solar,
            ["Wind"] = wind,
            ["Hydro"] = hydro,
        };
}
