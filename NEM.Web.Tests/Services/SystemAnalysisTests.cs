using AwesomeAssertions;
using NEM.Contracts;
using NEM.Web.Services.Insights;

namespace NEM.Web.Tests.Services;

public sealed class SystemAnalysisTests
{
    [Fact]
    public void Build_ReadsOneProfilePerRegionInTheArtifactsOwnOrder()
    {
        SystemAnalysis analysis = SystemAnalysis.Build(TwoRegionSystem());

        analysis.Regions.Select(region => region.RegionId).Should().Equal("NSW1", "VIC1");
        analysis.Cheapest!.RegionId.Should().Be("VIC1");
        analysis.Dearest!.RegionId.Should().Be("NSW1");
    }

    [Fact]
    public void Build_StatesTheSpreadBetweenTheCheapestAndDearestRegion()
    {
        SystemAnalysis analysis = SystemAnalysis.Build(TwoRegionSystem());

        Finding finding = analysis.Findings.Should()
            .ContainSingle(finding => finding.Headline.Contains("more than")).Subject;
        finding.Metric.Should().Be("$21.80");
        finding.Detail.Should().Contain("$167.27").And.Contain("$145.47");
    }

    [Fact]
    public void Build_DistinguishesTheSystemFigureFromThePlainAverageOfTheRegions()
    {
        SystemAnalysis analysis = SystemAnalysis.Build(TwoRegionSystem());

        analysis.MeanRegionalSlcoe.Should().Be(156.37m);
        analysis.Findings.Should().ContainSingle(finding =>
            finding.Headline.Contains("demand-weighted, not an average"));
    }

    [Fact]
    public void Build_NamesTheRegionCarryingTheUnservedEnergy()
    {
        SystemAnalysis analysis = SystemAnalysis.Build(TwoRegionSystem());

        Finding finding = analysis.Findings.Should()
            .ContainSingle(finding => finding.Headline.Contains("Unserved energy is concentrated")).Subject;
        finding.Headline.Should().Contain("Victoria");
        finding.Detail.Should().Contain("All of it fell in Victoria (VIC1)");
    }

    [Fact]
    public void Build_ReportsAServedYearRatherThanInventingAShortfallFinding()
    {
        SystemDispatchResultsDTO system = TwoRegionSystem(systemUnservedMwh: 0, vicUnservedMwh: 0);

        SystemAnalysis analysis = SystemAnalysis.Build(system);

        analysis.Findings.Should().ContainSingle(finding =>
            finding.Headline == "Every hour of demand was served"
            && finding.Tone == FindingTone.Favourable);
    }

    [Fact]
    public void Build_SeparatesSpillingFromRunningShortWhenARegionDoesBoth()
    {
        SystemAnalysis analysis = SystemAnalysis.Build(TwoRegionSystem());

        analysis.Findings.Should().ContainSingle(finding =>
            finding.Headline == "Victoria spills energy and still runs short");
    }

    [Fact]
    public void Build_IntegratesInterconnectorFlowIntoEnergyRatherThanSummingPowers()
    {
        SystemDispatchResultsDTO system = TwoRegionSystem(resolutionHours: 2);

        LinkFlow link = SystemAnalysis.Build(system).Links[0];

        // 1,000 MW held for one of three two-hour intervals is 2,000 MWh, not 1,000.
        link.EnergyMwh.Should().Be(2000);
        link.LossesMwh.Should().Be(100);
        link.FlowingIntervals.Should().Be(1);
        link.PeakFlowMw.Should().Be(1000);
    }

    [Fact]
    public void Build_ReportsTheDirectionOfTheBusiestLink()
    {
        SystemAnalysis analysis = SystemAnalysis.Build(TwoRegionSystem());

        analysis.Findings.Should().ContainSingle(finding =>
            finding.Headline == "New South Wales underwrites Victoria");
    }

    [Fact]
    public void Build_NamesTheRegionTheSizingLoopHadToGrowStorageIn()
    {
        SystemAnalysis analysis = SystemAnalysis.Build(TwoRegionSystem());

        Finding finding = analysis.Findings.Should()
            .ContainSingle(finding => finding.Headline.Contains("needed more storage")).Subject;
        finding.Headline.Should().Contain("Victoria");
        finding.Metric.Should().Be("3,529");
    }

    [Fact]
    public void Build_ClaimsNoStorageDivergenceWhenEveryRegionWasResized()
    {
        SystemDispatchResultsDTO system = TwoRegionSystem(nswResized: true);

        SystemAnalysis.Build(system).Findings.Should().NotContain(finding =>
            finding.Headline.Contains("needed more storage"));
    }

    [Fact]
    public void Build_AddsEachRegionsGenerationMixWhenTheDetailArtifactsAreSupplied()
    {
        SystemDispatchResultsDTO system = TwoRegionSystem();
        RegionDispatchResultsDTO detail = ArtifactFixtures.RegionResults() with
        {
            DataSeries = ArtifactFixtures.RegionResults().DataSeries with
            {
                DeliveredGenerationByTechnologyMw = new Dictionary<string, double[]>
                {
                    ["Wind"] = [100, 100, 100],
                    ["Coal"] = [300, 300, 300],
                },
            },
        };

        SystemAnalysis analysis = SystemAnalysis.Build(
            system,
            new Dictionary<string, RegionDispatchResultsDTO> { ["VIC1"] = detail });

        RegionProfile victoria = analysis.Regions.Single(region => region.RegionId == "VIC1");
        victoria.Mix.TotalMwh.Should().Be(1200);
        victoria.Mix.RenewableShare.Should().Be(0.25);
        analysis.Regions.Single(region => region.RegionId == "NSW1").Mix.TotalMwh.Should().Be(0);
    }

    [Fact]
    public void CurtailedShareOfAvailable_MeasuresSpillAgainstEverythingTheFleetCouldHaveDelivered()
    {
        SystemAnalysis analysis = SystemAnalysis.Build(TwoRegionSystem());

        analysis.Regions.Single(region => region.RegionId == "VIC1")
            .CurtailedShareOfAvailable.Should().BeApproximately(0.0607, 0.0005);
    }

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
            "results-nsw1.json");
        RegionDispatchSummaryDTO vic = new(
            Metrics(demandMwh: 44_977_270, deliveredMwh: 44_665_751, curtailedMwh: 2_891_385, unservedMwh: vicUnservedMwh),
            new ReliabilityBasisDTO(0.002, vicUnservedMwh > 0 ? 0.00164 : 0, true, "NEM reliability standard"),
            Sizing(3243.4, 6772, StorageSizingOutcome.Resized),
            Cost(slcoe: 145.47m, generation: 138.90m, storage: 6.57m, total: 6_542_903_141m, netImported: 252_796),
            "results-vic1.json");

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
            [
                new DispatchInterconnectorDTO("NSW1", "VIC1", 1000, [1000, 0, 0], [50, 0, 0]),
                new DispatchInterconnectorDTO("VIC1", "NSW1", 1000, [0, 0, 0], [0, 0, 0]),
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
        new("calculated", 0, 0, total, generation, storage, slcoe, 0, 0, netImported);
}
