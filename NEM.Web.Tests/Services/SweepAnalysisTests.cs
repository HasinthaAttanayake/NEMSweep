using AwesomeAssertions;
using NEM.Contracts;
using NEM.Web.Services.Insights;

namespace NEM.Web.Tests.Services;

public sealed class SweepAnalysisTests
{
    [Fact]
    public void Build_ReadsTheAnnualCostFromTheLevelisedCostAndTheEnergyServed()
    {
        SweepAnalysis analysis = Analyse(Run("p0", "Baseline", 0, slcoe: 166.90m, energyServed: 66_275_989));

        analysis.Runs.Single().TotalAnnualCostAud.Should().BeApproximately(11_061_462_564, 1);
    }

    [Fact]
    public void Build_SeparatesRunsThatProducedResultsFromRunsThatReachedALimit()
    {
        SweepAnalysis analysis = Analyse(
            Run("p0", "Baseline", 0),
            Failed("p1", "+500 MW", 500),
            Run("p2", "+1,000 MW", 1000));

        analysis.Runs.Select(run => run.Label).Should().Equal("Baseline", "+1,000 MW");
        analysis.ConstrainedPoints.Should().ContainSingle().Which.Label.Should().Be("+500 MW");
    }

    [Fact]
    public void Build_FindsAnInteriorCostMinimum()
    {
        SweepAnalysis analysis = Analyse(
            Run("p0", "Baseline", 0, slcoe: 166.90m),
            Run("p1", "+2,000 MW", 2000, slcoe: 143.47m),
            Run("p2", "+5,500 MW", 5500, slcoe: 130.69m),
            Run("p3", "+7,000 MW", 7000, slcoe: 141.91m));

        analysis.UnitCostTurningPoint.Should().NotBeNull();
        analysis.UnitCostTurningPoint!.IsMinimum.Should().BeTrue();
        analysis.UnitCostTurningPoint.Run.Label.Should().Be("+5,500 MW");
        analysis.UnitCostTurningPoint.ReboundPercentage.Should().BeApproximately(8.59, 0.05);
        analysis.CheapestUnitCost!.Label.Should().Be("+5,500 MW");
    }

    [Fact]
    public void Build_ClaimsNoTurningPointForAMonotoneCostCurve()
    {
        SweepAnalysis analysis = Analyse(
            Run("p0", "Baseline", 0, slcoe: 166.90m),
            Run("p1", "+10%", 0.1, slcoe: 168.40m),
            Run("p2", "+20%", 0.2, slcoe: 170.02m),
            Run("p3", "+30%", 0.3, slcoe: 173.53m));

        analysis.UnitCostTurningPoint.Should().BeNull();
    }

    [Fact]
    public void Build_ClaimsNoTurningPointForRoundingSizedWobble()
    {
        SweepAnalysis analysis = Analyse(
            Run("p0", "A", 0, slcoe: 100.00m),
            Run("p1", "B", 1, slcoe: 99.90m),
            Run("p2", "C", 2, slcoe: 100.05m));

        analysis.UnitCostTurningPoint.Should().BeNull();
    }

    [Fact]
    public void Build_StatesWhenTheUnitCostAndTheAnnualBillMoveInOppositeDirections()
    {
        SweepAnalysis analysis = Analyse(
            Run("p0", "Baseline", 0, slcoe: 166.90m, energyServed: 66_275_989),
            Run("p1", "+3,500 MW", 3500, slcoe: 150m, energyServed: 96_936_000),
            Run("p2", "+7,000 MW", 7000, slcoe: 141.91m, energyServed: 127_593_437));

        Finding finding = analysis.Findings.Should()
            .ContainSingle(finding => finding.Headline.Contains("annual bill")).Subject;
        finding.Headline.Should().Contain("falls");
        finding.Headline.Should().Contain("rises");
        finding.Tone.Should().Be(FindingTone.Caution);
        finding.Metric.Should().StartWith("+");
    }

    [Fact]
    public void Build_UsesAPluralVerbWhenBothCostReadingsMoveTogether()
    {
        SweepAnalysis analysis = Analyse(
            Run("p0", "Baseline", 0, slcoe: 166.90m, energyServed: 66_275_989),
            Run("p1", "+40%", 0.4, slcoe: 173.53m, energyServed: 66_275_989));

        analysis.Findings.Should()
            .ContainSingle(finding => finding.Headline.Contains("both rise"));
    }

    [Fact]
    public void Build_SeparatesTheCostComponentThatFallsFromTheOneThatTurnsTheCurve()
    {
        SweepAnalysis analysis = Analyse(
            Run("p0", "Baseline", 0, generationSlcoe: 163.71m, storageSlcoe: 3.19m),
            Run("p1", "+3,500 MW", 3500, generationSlcoe: 132.13m, storageSlcoe: 2.24m),
            Run("p2", "+7,000 MW", 7000, generationSlcoe: 122.41m, storageSlcoe: 19.50m));

        analysis.Findings.Should().ContainSingle(finding =>
            finding.Headline.StartsWith("Generation keeps getting cheaper"));
    }

    [Fact]
    public void Build_ReportsHowMuchNewLoadWasMetFromRecoveredSpill()
    {
        SweepAnalysis analysis = Analyse(
            Run("p0", "Baseline", 0, demand: 66_276_000, curtailed: 1_857_426),
            Run("p1", "+7,000 MW", 7000, demand: 127_596_000, curtailed: 0));

        Finding finding = analysis.Findings.Should()
            .ContainSingle(finding => finding.Headline.Contains("soaks up")).Subject;
        finding.Metric.Should().Be("3%");
        finding.Tone.Should().Be(FindingTone.Favourable);
    }

    [Fact]
    public void Build_ReportsTheShareOfNewRenewableEnergyThatIsSpilledWhenDemandIsUnchanged()
    {
        SweepAnalysis analysis = Analyse(
            Run("p0", "Baseline", 0, demand: 66_275_989, delivered: 66_275_989,
                renewableShare: 0.3798, curtailed: 1_857_426),
            Run("p1", "+40%", 0.4, demand: 66_275_989, delivered: 66_275_989,
                renewableShare: 0.4586, curtailed: 6_523_789));

        Finding finding = analysis.Findings.Should()
            .ContainSingle(finding => finding.Headline.Contains("spilled")).Subject;
        finding.Metric.Should().Be("47%");
    }

    /// <summary>
    /// The run where the installed fleet stops being enough is a capital decision, and it is not
    /// visible in any cost series because the storage cost is spread across every run after it.
    /// </summary>
    [Fact]
    public void Build_NamesTheRunWhereNewStorageBecomesNecessary()
    {
        SweepAnalysis analysis = Analyse(
            Sized("p0", "Baseline", 0, StorageSizingOutcome.NotRequired),
            Sized("p1", "+3,000 MW", 3000, StorageSizingOutcome.NotRequired),
            Sized("p2", "+3,500 MW", 3500, StorageSizingOutcome.Resized),
            Sized("p3", "+4,000 MW", 4000, StorageSizingOutcome.Resized));

        Finding finding = analysis.Findings.Should()
            .ContainSingle(finding => finding.Headline.StartsWith("New storage becomes necessary")).Subject;
        finding.Headline.Should().Contain("+3,500 MW");
        finding.Detail.Should().Contain("+3,000 MW");
        finding.Metric.Should().Be("3,500");
        finding.Tone.Should().Be(FindingTone.Constraint);
    }

    [Fact]
    public void Build_ClaimsNoStorageThresholdWhenTheFirstRunAlreadyNeedsBuilding()
    {
        SweepAnalysis analysis = Analyse(
            Sized("p0", "Baseline", 0, StorageSizingOutcome.Resized),
            Sized("p1", "+500 MW", 500, StorageSizingOutcome.Resized));

        analysis.Findings.Should().NotContain(finding =>
            finding.Headline.StartsWith("New storage becomes necessary"));
    }

    [Fact]
    public void Build_ClaimsNoStorageThresholdWhenNothingIsEverResized()
    {
        SweepAnalysis analysis = Analyse(
            Sized("p0", "Baseline", 0, StorageSizingOutcome.NotRequired),
            Sized("p1", "+500 MW", 500, StorageSizingOutcome.NotRequired));

        analysis.Findings.Should().NotContain(finding =>
            finding.Headline.StartsWith("New storage becomes necessary"));
    }

    [Fact]
    public void Build_NamesTheLastFeasibleRunWhenLaterRunsReachALimit()
    {
        SweepAnalysis analysis = Analyse(
            Run("p0", "Baseline", 0),
            Run("p1", "+7,000 MW", 7000),
            Failed("p2", "+7,500 MW", 7500),
            Failed("p3", "+8,000 MW", 8000));

        Finding finding = analysis.Findings.Should()
            .ContainSingle(finding => finding.Tone == FindingTone.Constraint
                && finding.Headline.Contains("feasible")).Subject;
        finding.Headline.Should().Contain("+7,000 MW");
        finding.Metric.Should().Be("2");
    }

    [Fact]
    public void Build_StatesWhenTheReliabilityTargetIsSettingTheCost()
    {
        SweepAnalysis analysis = Analyse(
            Run("p0", "Baseline", 0, achievedUse: 0),
            Run("p1", "+4,000 MW", 4000, achievedUse: 0.002),
            Run("p2", "+4,500 MW", 4500, achievedUse: 0.002),
            Run("p3", "+5,000 MW", 5000, achievedUse: 0.002));

        analysis.Findings.Should().ContainSingle(finding =>
            finding.Headline.Contains("3 runs sit exactly on the reliability target"));
    }

    [Fact]
    public void Build_DoesNotReadRunsPastTheTargetAsSittingOnIt()
    {
        SweepAnalysis analysis = Analyse(
            Run("p0", "Baseline", 0, achievedUse: 0),
            Run("p1", "+5,000 MW", 5000, achievedUse: 0.1506),
            Run("p2", "+9,000 MW", 9000, achievedUse: 2.0787),
            Run("p3", "+12,000 MW", 12000, achievedUse: 5.9253));

        analysis.Findings.Should().NotContain(finding =>
            finding.Headline.Contains("sit exactly on the reliability target"));
    }

    [Fact]
    public void Build_NamesTheRunFromWhichTheReliabilityStandardIsMissed()
    {
        SweepAnalysis analysis = Analyse(
            Run("p0", "Baseline", 0, achievedUse: 0),
            Run("p1", "+5,000 MW", 5000, achievedUse: 0.1506, unserved: 346_919.4),
            Run("p2", "+12,000 MW", 12000, achievedUse: 5.9253, unserved: 17_287_805.1));

        Finding finding = analysis.Findings.Should().ContainSingle(finding =>
            finding.Headline.Contains("cannot meet the reliability standard")).Subject;

        finding.Headline.Should().Be("2 of 3 runs cannot meet the reliability standard");
        finding.Detail.Should().Contain("From +5,000 MW onwards");
        finding.Detail.Should().Contain("+12,000 MW is the furthest outside it, at 5.93%");
        finding.Detail.Should().Contain("0.002% target");
        finding.Tone.Should().Be(FindingTone.Constraint);
        // The shortfall has to outrank the cost findings: every one of them quotes a cost per
        // megawatt-hour drawn from a run that did not serve its load.
        analysis.Findings[0].Should().Be(finding);
    }

    /// <summary>
    /// A run counts as within target once the sizing loop has built its way there, so a bare count
    /// of misses reads as though every other run were fine. The headline has to separate the runs
    /// storage could not save from the runs it did.
    /// </summary>
    [Fact]
    public void Build_SaysMissedRunsWereAlreadyGivenStorageAndCountsTheRunsThatBuiltTheirWayBack()
    {
        SweepAnalysis analysis = Analyse(
            Sized("p0", "Baseline", 0, StorageSizingOutcome.NotRequired),
            Sized("p1", "+500 MW", 500, StorageSizingOutcome.Resized),
            Sized("p2", "+4,500 MW", 4500, StorageSizingOutcome.Resized),
            Sized(
                "p3",
                "+5,000 MW",
                5000,
                StorageSizingOutcome.StorageNoLongerImprovesReliability,
                achievedUse: 0.1506,
                unserved: 346_919.4));

        Finding finding = analysis.Findings.Should().ContainSingle(finding =>
            finding.Headline.Contains("cannot meet the reliability standard")).Subject;

        finding.Headline.Should().Be(
            "1 of 4 runs cannot meet the reliability standard even with more storage");
        finding.Detail.Should().Contain("grew storage for every one of them and still fell short");
        finding.Detail.Should().Contain("generation or transmission rather than more storage");
        finding.Detail.Should().Contain("A further 2 runs hold the standard only by building storage.");
    }

    /// <summary>
    /// The qualifier is a claim about storage's ceiling, so it is only earned when every missed run
    /// actually had storage grown for it and still fell short.
    /// </summary>
    [Fact]
    public void Build_OmitsTheStorageQualifierWhenAMissedRunWasNeverResized()
    {
        SweepAnalysis analysis = Analyse(
            Sized("p0", "Baseline", 0, StorageSizingOutcome.NotRequired),
            Sized(
                "p1",
                "+5,000 MW",
                5000,
                StorageSizingOutcome.Resized,
                achievedUse: 0.1506,
                unserved: 346_919.4));

        Finding finding = analysis.Findings.Should().ContainSingle(finding =>
            finding.Headline.Contains("cannot meet the reliability standard")).Subject;

        finding.Headline.Should().Be("1 of 2 runs cannot meet the reliability standard");
        finding.Detail.Should().NotContain("more storage");
    }

    [Fact]
    public void Build_CountsMissedRunsWithoutClaimingABoundaryWhenTheSweepRecovers()
    {
        SweepAnalysis analysis = Analyse(
            Run("p0", "Baseline", 0, achievedUse: 0),
            Run("p1", "+5,000 MW", 5000, achievedUse: 0.1506),
            Run("p2", "+9,000 MW", 9000, achievedUse: 0));

        Finding finding = analysis.Findings.Should().ContainSingle(finding =>
            finding.Headline.Contains("cannot meet the reliability standard")).Subject;

        finding.Detail.Should().StartWith("1 run ends outside the standard.");
        finding.Detail.Should().NotContain("onwards");
    }

    [Fact]
    public void Build_ClaimsNoReliabilityShortfallWhenEveryRunMeetsTheStandard()
    {
        SweepAnalysis analysis = Analyse(
            Run("p0", "Baseline", 0, achievedUse: 0),
            Run("p1", "+5,000 MW", 5000, achievedUse: 0.001));

        analysis.Findings.Should().NotContain(finding =>
            finding.Headline.Contains("reliability standard"));
    }

    /// <summary>
    /// The binding finding reads the same scope as everything else on the page. Taking the achieved
    /// value from the index's basis reported a region as pinned to the standard on the strength of
    /// the system's result.
    /// </summary>
    [Fact]
    public void Build_JudgesTheBindingTargetAtTheSelectedScope()
    {
        SweepIndexPointDTO[] points =
        [
            .. new[] { ("p0", "Baseline", 0d), ("p1", "+4,000 MW", 4000d), ("p2", "+4,500 MW", 4500d) }
                .Select(run => Run(run.Item1, run.Item2, run.Item3, achievedUse: 0.002) with
                {
                    RegionScalars = [new SweepPointRegionScalarsDTO("QLD1", Scalars())],
                }),
        ];

        SweepAnalysis system = SweepAnalysis.Build(Index(points));
        SweepAnalysis region = SweepAnalysis.Build(Index(points), "QLD1");

        system.Findings.Should().ContainSingle(finding =>
            finding.Headline.Contains("sit exactly on the reliability target"));
        region.Findings.Should().NotContain(finding =>
            finding.Headline.Contains("sit exactly on the reliability target"));
    }

    [Fact]
    public void Build_ReadsRegionalScalarsWhenARegionIsSelected()
    {
        SweepIndexPointDTO point = Run("p0", "Baseline", 0, slcoe: 158.46m) with
        {
            RegionScalars =
            [
                new SweepPointRegionScalarsDTO("VIC1", Scalars(slcoe: 145.47m)),
            ],
        };

        SweepAnalysis system = SweepAnalysis.Build(Index(point));
        SweepAnalysis region = SweepAnalysis.Build(Index(point), "VIC1");

        system.Runs.Single().Scalars.SlcoeAudPerMwh.Should().Be(158.46m);
        region.Runs.Single().Scalars.SlcoeAudPerMwh.Should().Be(145.47m);
    }

    /// <summary>
    /// The index records one reliability verdict, and it is the system's. A region that served all
    /// of its load sits inside a system that did not, and must not be told it missed the standard.
    /// </summary>
    [Fact]
    public void Build_ReadsTheReliabilityShortfallFromTheSelectedRegionRatherThanTheSystem()
    {
        SweepIndexPointDTO point = Run("p0", "Baseline", 0, achievedUse: 5.9253, unserved: 17_287_805.1)
            with
        {
            RegionScalars = [new SweepPointRegionScalarsDTO("QLD1", Scalars())],
        };

        SweepAnalysis system = SweepAnalysis.Build(Index(point));
        SweepAnalysis region = SweepAnalysis.Build(Index(point), "QLD1");

        system.Findings.Should().ContainSingle(finding =>
            finding.Headline.Contains("reliability standard"));
        region.Findings.Should().NotContain(finding =>
            finding.Headline.Contains("reliability standard"));
    }

    [Fact]
    public void Build_ProducesNoFindingsForASweepWithASingleRun()
    {
        SweepAnalysis analysis = Analyse(Run("p0", "Baseline", 0));

        analysis.Findings.Should().BeEmpty();
    }

    private static SweepAnalysis Analyse(params SweepIndexPointDTO[] points) =>
        SweepAnalysis.Build(Index(points));

    private static SweepIndexDTO Index(params SweepIndexPointDTO[] points) =>
        ArtifactFixtures.Index(points);

    private static SweepIndexPointDTO Run(
        string pointId,
        string label,
        double axisValue,
        decimal slcoe = 150m,
        decimal generationSlcoe = 145m,
        decimal storageSlcoe = 5m,
        double demand = 1_000_000,
        double energyServed = 1_000_000,
        double delivered = 1_000_000,
        double? renewableShare = null,
        double curtailed = 0,
        double achievedUse = 0,
        double unserved = 0) => new(
        pointId,
        label,
        axisValue,
        SweepPointStatus.Succeeded,
        $"points/{pointId}.json",
        $"configs/{pointId}.json",
        Scalars(
            slcoe,
            generationSlcoe,
            storageSlcoe,
            demand,
            energyServed,
            delivered,
            renewableShare,
            curtailed,
            unserved,
            achievedUse),
        new ReliabilityBasisDTO(0.002, achievedUse, achievedUse <= 0.002, "NEM reliability standard"),
        ArtifactFixtures.Sizing(),
        new IntervalPointersDTO(null, null, 0),
        null);

    /// <summary>A run whose sizing loop finished with a chosen outcome, growing storage when resized.</summary>
    private static SweepIndexPointDTO Sized(
        string pointId,
        string label,
        double axisValue,
        StorageSizingOutcome outcome,
        double achievedUse = 0,
        double unserved = 0) => Run(pointId, label, axisValue, achievedUse: achievedUse, unserved: unserved) with
        {
            StorageSizing = new StorageSizingOutcomeDTO(
            outcome,
            5515,
            940,
            outcome == StorageSizingOutcome.Resized ? 6036 : 5515,
            outcome == StorageSizingOutcome.Resized ? 1509 : 940,
            100_000,
            10_000,
            3),
        };

    private static SweepIndexPointDTO Failed(string pointId, string label, double axisValue) =>
        ArtifactFixtures.FailedPoint(pointId, label, axisValue, "The Battery capacity bounds are insufficient.");

    private static SweepPointScalarResultsDTO Scalars(
        decimal slcoe = 150m,
        decimal generationSlcoe = 145m,
        decimal storageSlcoe = 5m,
        double demand = 1_000_000,
        double energyServed = 1_000_000,
        double delivered = 1_000_000,
        double? renewableShare = null,
        double curtailed = 0,
        double unserved = 0,
        double unservedShare = 0) => new(
        SlcoeAudPerMwh: slcoe,
        GenerationSlcoeAudPerMwh: generationSlcoe,
        StorageSlcoeAudPerMwh: storageSlcoe,
        DemandMwh: demand,
        EnergyServedMwh: energyServed,
        DeliveredGenerationMwh: delivered,
        AchievedRenewableShareGridScale: renewableShare,
        AchievedRenewableShareNative: null,
        StoragePowerMw: 940,
        StorageEnergyMwh: 5515,
        UnservedEnergyMwh: unserved,
        UnservedEnergyPercentageOfDemand: unservedShare,
        UnservedHours: 0,
        HoursServedFraction: 1,
        PeakUnservedPowerMw: 0,
        CurtailedEnergyMwh: curtailed,
        TransmissionSlcotAudPerMwh: 0m,
        TransmissionCostStatus: TransmissionCostStatus.NotModelled,
        NetImportedEnergyMwh: 0);
}
