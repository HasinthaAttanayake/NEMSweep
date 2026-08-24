using AwesomeAssertions;
using NEMSweep.Model.Generation.Wind;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Scenarios;
using NEMSweep.Model.Series;
using NEMSweep.Model.Simulation;
using NEMSweep.Model.Units;
using NEMSweep.Model.Weather;

namespace NEMSweep.Model.Tests.Simulation;

public sealed class InterRegionalDispatchTests
{
    private const double Tolerance = 1e-6;

    private static readonly DateTimeOffset NemStart =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    public void Dispatch_WithoutInterconnectors_MatchesIndependentSingleRegionRuns()
    {
        Region nsw = CoalRegion("NSW1", capacityMw: 80, demandMw: [100, 60, 100]);
        Region vic = CoalRegion("VIC1", capacityMw: 120, demandMw: [90, 130, 70]);

        IReadOnlyList<DispatchOutcome> together = Dispatcher.Dispatch(System([nsw, vic]));
        DispatchOutcome nswAlone = Dispatcher.Dispatch(System([nsw])).Single();
        DispatchOutcome vicAlone = Dispatcher.Dispatch(System([vic])).Single();

        AssertIdentical(together[0], nswAlone);
        AssertIdentical(together[1], vicAlone);
    }

    [Fact]
    public void Dispatch_ZeroTransferLimits_ReproducesIndependentRuns()
    {
        Region nsw = CoalRegion("NSW1", capacityMw: 200, demandMw: [50]);
        Region vic = CoalRegion("VIC1", capacityMw: 5, demandMw: [100]);

        IReadOnlyList<DispatchOutcome> linked = Dispatcher.Dispatch(
            System([nsw, vic], [Link("NSW1", "VIC1", capacityMw: 0)]));
        IReadOnlyList<DispatchOutcome> unlinked = Dispatcher.Dispatch(System([nsw, vic]));

        AssertIdentical(linked[0], unlinked[0]);
        AssertIdentical(linked[1], unlinked[1]);
        linked[1].Reliability.UnservedEnergy.Should().Be(
            Energy.FromMegawattHours(95),
            "a zero-limit link must leave the deficit region exactly as it was");
    }

    [Fact]
    public void Dispatch_TransferRequiresAnInterconnectorInTheSendingDirection()
    {
        Region nsw = CoalRegion("NSW1", capacityMw: 5, demandMw: [100]);
        Region vic = CoalRegion("VIC1", capacityMw: 200, demandMw: [50]);

        SystemDispatchRunResult blocked = Dispatcher.DispatchSystem(
            System([nsw, vic], [Link("NSW1", "VIC1", capacityMw: 1_000)]));
        SystemDispatchRunResult enabled = Dispatcher.DispatchSystem(
            System([nsw, vic], [Link("VIC1", "NSW1", capacityMw: 1_000)]));

        blocked.RegionalOutcomes[0].Imports[0].Megawatts.Should().Be(0);
        blocked.RegionalOutcomes[0].Unserved[0].Megawatts.Should().Be(95);
        enabled.RegionalOutcomes[0].Imports[0].Megawatts.Should().BeApproximately(95, Tolerance);
        enabled.RegionalOutcomes[0].Unserved[0].Megawatts.Should().BeApproximately(0, Tolerance);
    }

    [Fact]
    public void Dispatch_SingleHopTransfer_DeliversNinetyFivePercentAndBooksTheLoss()
    {
        Region nsw = CoalRegion("NSW1", capacityMw: 200, demandMw: [50]);
        Region vic = CoalRegion("VIC1", capacityMw: 5, demandMw: [100]);

        SystemDispatchRunResult result = Dispatcher.DispatchSystem(
            System([nsw, vic], [Link("NSW1", "VIC1", capacityMw: 1_000)]));

        DispatchOutcome exporter = result.RegionalOutcomes[0];
        DispatchOutcome importer = result.RegionalOutcomes[1];
        exporter.Exports[0].Megawatts.Should().BeApproximately(
            100,
            Tolerance,
            "delivering the 95 MW deficit over one hop requires 95/0.95 = 100 MW to be sent");
        exporter.PerFleetGeneration[GenerationTechnology.Coal][0].Megawatts.Should()
            .BeApproximately(150, Tolerance, "50 MW of local load plus 100 MW started to export");
        importer.Imports[0].Megawatts.Should().BeApproximately(95, Tolerance);
        importer.Unserved[0].Megawatts.Should().BeApproximately(0, Tolerance);
    }

    [Fact]
    public void Dispatch_WheelingThroughAMiddleRegion_CompoundsLossAndLeavesTheTransitRegionUnbooked()
    {
        Region source = CoalRegion("AAA1", capacityMw: 200, demandMw: [50]);
        Region transit = CoalRegion("BBB1", capacityMw: 40, demandMw: [40]);
        Region sink = CoalRegion("CCC1", capacityMw: 0.875, demandMw: [46]);

        SystemDispatchRunResult result = Dispatcher.DispatchSystem(System(
            [source, transit, sink],
            [
                Link("AAA1", "BBB1", capacityMw: 1_000),
                Link("BBB1", "CCC1", capacityMw: 1_000),
            ]));

        DispatchOutcome transitOutcome = result.RegionalOutcomes[1];
        DispatchOutcome sinkOutcome = result.RegionalOutcomes[2];
        result.RegionalOutcomes[0].Exports[0].Megawatts.Should().BeApproximately(
            50,
            Tolerance,
            "45.125 MW delivered over two hops requires 45.125 / 0.95^2 = 50 MW sent");
        sinkOutcome.Imports[0].Megawatts.Should().BeApproximately(45.125, Tolerance);
        transitOutcome.Imports[0].Megawatts.Should().BeApproximately(
            0,
            Tolerance,
            "a transit region books nothing; the energy passes through it");
        transitOutcome.Exports[0].Megawatts.Should().BeApproximately(0, Tolerance);
    }

    [Fact]
    public void Dispatch_LossLedger_AgreesAcrossSolverLinksAndOutcomes()
    {
        Region source = CoalRegion("AAA1", capacityMw: 200, demandMw: [50]);
        Region transit = CoalRegion("BBB1", capacityMw: 40, demandMw: [40]);
        Region sink = CoalRegion("CCC1", capacityMw: 0.875, demandMw: [46]);
        PowerSystem system = System(
            [source, transit, sink],
            [
                Link("AAA1", "BBB1", capacityMw: 1_000),
                Link("BBB1", "CCC1", capacityMw: 1_000),
            ]);

        SystemDispatchRunResult result = Dispatcher.DispatchSystem(system);
        SystemDispatchOutcome aggregate = SystemDispatchOutcome.Create(
            system,
            result);

        double fromLinks = result.InterconnectorFlows.Sum(flow => flow.Losses[0].Megawatts);
        aggregate.TransmissionLosses[0].Megawatts.Should().BeApproximately(
            4.875,
            Tolerance,
            "50 sent less 45.125 delivered");
        fromLinks.Should().BeApproximately(
            aggregate.TransmissionLosses[0].Megawatts,
            Tolerance,
            "per-link losses must sum to the system total derived from exports less imports");
    }

    [Fact]
    public void Dispatch_ExportDrawsOnCurtailedRenewablesBeforeStartingThermalPlant()
    {
        FlowSeries windDemand = HourlyFlow(10);
        var windy = new Region(
            "AAA1",
            [Fleet(GenerationTechnology.Wind, 100), Fleet(GenerationTechnology.Coal, 100, 50m)],
            windDemand,
            resourceProfile: RegionalResources(windDemand));
        Region deficit = CoalRegion("BBB1", capacityMw: 0, demandMw: [50]);

        SystemDispatchRunResult result = Dispatcher.DispatchSystem(System(
            [windy, deficit],
            [Link("AAA1", "BBB1", capacityMw: 1_000)]));

        DispatchOutcome exporter = result.RegionalOutcomes[0];
        exporter.PerFleetCurtailment[GenerationTechnology.Wind][0].Megawatts.Should()
            .BeApproximately(
                90 - (50 / 0.95),
                Tolerance,
                "the export is sourced from spilled wind, which reduces curtailment");
        exporter.PerFleetGeneration[GenerationTechnology.Coal][0].Megawatts.Should()
            .BeApproximately(
                0,
                Tolerance,
                "thermal plant must not start while free spilled wind is available");
    }

    [Fact]
    public void Dispatch_HydroExport_CappedToSamePacedAllowanceAsLocalDispatch()
    {
        // Hydro rejoins the export pool (see RegionalDispatchRun.ExportableSurplus /
        // IncrementalHeadroom) rather than being excluded outright, but its incremental
        // headroom here is capped to the SAME per-interval pace as local dispatch. Giving it a
        // higher SRMC than Coal means Coal covers all of AAA1's own local demand first, so
        // Hydro's paced allowance for the interval (computed from residual demand alone -
        // Coal's contribution is invisible to it) is entirely free for the export - but the
        // export still can't reach past that allowance into Hydro's full remaining budget.
        FlowSeries demand = HourlyFlow(20);
        var hydro = new GeneratingFleet(
            GenerationTechnology.Hydro,
            Power.FromMegawatts(100),
            new Dictionary<DateOnly, double> { [new DateOnly(2026, 7, 1)] = 1 },
            shortRunMarginalCost: GenerationEnergyCost.FromAudPerMwhGenerated(50));
        var source = new Region(
            "AAA1",
            [Fleet(GenerationTechnology.Coal, 50, shortRunMarginalCostAudPerMwh: 1), hydro],
            demand);
        Region deficit = CoalRegion("BBB1", capacityMw: 0, demandMw: [50]);

        SystemDispatchRunResult result = Dispatcher.DispatchSystem(System(
            [source, deficit],
            [Link("AAA1", "BBB1", capacityMw: 1_000)]));

        DispatchOutcome exporter = result.RegionalOutcomes[0];
        DispatchOutcome importer = result.RegionalOutcomes[1];
        exporter.PerFleetGeneration[GenerationTechnology.Coal][0].Megawatts.Should().BeApproximately(
            50,
            Tolerance,
            "20 MW local load plus 30 MW headroom started to serve the export");
        exporter.PerFleetGeneration[GenerationTechnology.Hydro][0].Megawatts.Should().BeApproximately(
            20,
            Tolerance,
            "no local demand left for Hydro, but its paced allowance covers the rest of the export");
        exporter.Exports[0].Megawatts.Should().BeApproximately(
            50,
            Tolerance,
            "Coal's 30 MW headroom plus Hydro's 20 MW paced allowance");
        importer.Unserved[0].Megawatts.Should().BeGreaterThan(
            0,
            "the export is capped to Hydro's paced allowance, not its full remaining budget, "
            + "so BBB1's 50 MW deficit isn't fully closed");
    }

    [Fact]
    public void Dispatch_LargestDeficitIsServedFirstWhenSurplusIsScarce()
    {
        Region source = CoalRegion("AAA1", capacityMw: 60, demandMw: [10]);
        Region small = CoalRegion("BBB1", capacityMw: 0, demandMw: [20]);
        Region large = CoalRegion("CCC1", capacityMw: 0, demandMw: [40]);

        SystemDispatchRunResult result = Dispatcher.DispatchSystem(System(
            [source, small, large],
            [
                Link("AAA1", "BBB1", capacityMw: 1_000),
                Link("AAA1", "CCC1", capacityMw: 1_000),
            ]));

        result.RegionalOutcomes[2].Unserved[0].Megawatts.Should().BeApproximately(
            0,
            Tolerance,
            "the 40 MW deficit outranks the 20 MW one and is served in full");
        result.RegionalOutcomes[1].Unserved[0].Megawatts.Should().BeGreaterThan(
            0,
            "only 50 MW of surplus exists, so the smaller deficit is left partly unserved");
    }

    [Theory]
    [InlineData(7)]
    [InlineData(41)]
    [InlineData(2026)]
    public void Dispatch_RandomTwoRegionMonth_PreservesEveryBalanceInvariant(int seed)
    {
        var random = new Random(seed);
        int length = 24 * 30;
        double[] nswDemand = Enumerable.Range(0, length)
            .Select(_ => Math.Round(random.NextDouble() * 120, 6)).ToArray();
        double[] vicDemand = Enumerable.Range(0, length)
            .Select(_ => Math.Round(random.NextDouble() * 120, 6)).ToArray();

        Region nsw = CoalRegion("NSW1", capacityMw: 90, demandMw: nswDemand);
        Region vic = CoalRegion("VIC1", capacityMw: 70, demandMw: vicDemand);
        PowerSystem system = System(
            [nsw, vic],
            [
                Link("NSW1", "VIC1", capacityMw: 30),
                Link("VIC1", "NSW1", capacityMw: 30),
            ]);

        SystemDispatchRunResult result = Dispatcher.DispatchSystem(system);
        SystemDispatchOutcome aggregate = SystemDispatchOutcome.Create(
            system,
            result);

        for (int index = 0; index < length; index++)
        {
            double sent = result.RegionalOutcomes.Sum(outcome => outcome.Exports[index].Megawatts);
            double received = result.RegionalOutcomes.Sum(
                outcome => outcome.Imports[index].Megawatts);
            sent.Should().BeGreaterThanOrEqualTo(
                received - Tolerance,
                "energy cannot be created in transit at index {0}",
                index);
            aggregate.TransmissionLosses[index].Megawatts.Should().BeApproximately(
                result.InterconnectorFlows.Sum(flow => flow.Losses[index].Megawatts),
                Tolerance,
                "loss ledgers must agree at index {0}",
                index);

            foreach (InterconnectorFlow flow in result.InterconnectorFlows)
            {
                flow.Flow[index].Megawatts.Should().BeLessThanOrEqualTo(
                    flow.Interconnector.Capacity.Megawatts + Tolerance,
                    "the directed link capacity binds at index {0}",
                    index);
            }

            foreach (DispatchOutcome outcome in result.RegionalOutcomes)
            {
                (outcome.Curtailment[index].Megawatts * outcome.Unserved[index].Megawatts).Should()
                    .BeApproximately(
                        0,
                        Tolerance,
                        "curtailment and unserved demand must not coexist in {0} at index {1}",
                        outcome.RegionId,
                        index);
            }
        }
    }

    private static void AssertIdentical(DispatchOutcome actual, DispatchOutcome expected)
    {
        actual.RegionId.Should().Be(expected.RegionId);
        for (int index = 0; index < expected.Demand.Length; index++)
        {
            actual.Unserved[index].Megawatts.Should().Be(expected.Unserved[index].Megawatts);
            actual.Curtailment[index].Megawatts.Should().Be(expected.Curtailment[index].Megawatts);
            actual.Charge[index].Megawatts.Should().Be(expected.Charge[index].Megawatts);
            actual.Discharge[index].Megawatts.Should().Be(expected.Discharge[index].Megawatts);
            actual.Imports[index].Megawatts.Should().Be(0);
            actual.Exports[index].Megawatts.Should().Be(0);
            foreach (GenerationTechnology technology in expected.PerFleetGeneration.Keys)
            {
                actual.PerFleetGeneration[technology][index].Megawatts.Should()
                    .Be(expected.PerFleetGeneration[technology][index].Megawatts);
            }
        }
    }

    private static PowerSystem System(
        IReadOnlyList<Region> regions,
        IReadOnlyList<Interconnector>? interconnectors = null) =>
        new(
            new PowerSystemId("test-power-system"),
            new ScenarioId("test-scenario"),
            regions,
            interconnectors);

    private static Interconnector Link(
        string fromRegionId,
        string toRegionId,
        double capacityMw) =>
        new(
            fromRegionId,
            toRegionId,
            Power.FromMegawatts(capacityMw));

    private static Region CoalRegion(string regionId, double capacityMw, double[] demandMw) =>
        new(regionId, [Fleet(GenerationTechnology.Coal, capacityMw)], HourlyFlow(demandMw));

    private static GeneratingFleet Fleet(
        GenerationTechnology technology,
        double capacityMw,
        decimal shortRunMarginalCostAudPerMwh = 0m) =>
        new(
            technology,
            Power.FromMegawatts(capacityMw),
            shortRunMarginalCost: GenerationEnergyCost.FromAudPerMwhGenerated(
                shortRunMarginalCostAudPerMwh));

    private static FlowSeries HourlyFlow(params double[] megawatts) =>
        new(NemStart, TimeSpan.FromHours(1), megawatts);

    private static RegionalResourceProfile RegionalResources(FlowSeries timeline)
    {
        var zeros = new double[timeline.Length];
        double[] windSpeed = Enumerable.Repeat(
            WindPowerCurve.RatedWindSpeedMetresPerSecond,
            timeline.Length).ToArray();
        return new RegionalResourceProfile(
            TraceSeries.GlobalHorizontalRadiation(timeline.Start, timeline.Resolution, zeros),
            TraceSeries.DirectNormalRadiation(timeline.Start, timeline.Resolution, zeros),
            TraceSeries.DiffuseHorizontalRadiation(timeline.Start, timeline.Resolution, zeros),
            SolarZenithSeries.Calculate(
                timeline.Start,
                timeline.Resolution,
                timeline.Length,
                latitude: -33.8688,
                longitude: 151.2093),
            TraceSeries.DryBulbTemperature(timeline.Start, timeline.Resolution, zeros),
            TraceSeries.WindSpeed(
                timeline.Start,
                timeline.Resolution,
                windSpeed,
                WindPowerCurve.DefaultHubHeightMetres));
    }
}
