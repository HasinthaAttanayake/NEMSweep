using AwesomeAssertions;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.Units;

namespace NEM.Model.Tests.Simulation;

public sealed class SystemDispatchOutcomeTests
{
    private static readonly DateTimeOffset NemStart =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    public void Create_SumsSystemFlowsAndRecomputesDeliveredLoadAndReliability()
    {
        PowerSystem system = System("NSW1", "VIC1");
        SystemDispatchOutcome outcome = SystemDispatchOutcome.Create(system,
        [
            Outcome("NSW1", GenerationTechnology.Coal, [100, 90], [100, 100], [0, 10]),
            Outcome("VIC1", GenerationTechnology.Gas, [45, 50], [50, 50], [5, 0]),
        ]);

        AssertFlow(outcome.Demand, 150, 150);
        AssertFlow(outcome.Unserved, 5, 10);
        AssertFlow(outcome.DeliveredToLoad, 145, 140);
        AssertFlow(outcome.PerFleetGeneration[GenerationTechnology.Coal], 100, 90);
        AssertFlow(outcome.PerFleetGeneration[GenerationTechnology.Gas], 45, 50);
        outcome.Reliability.UnservedEnergy.Should().Be(Energy.FromMegawattHours(15));
        outcome.Reliability.PeakUnservedPower.Should().Be(Power.FromMegawatts(10));
        outcome.Reliability.UnservedHours.Should().Be(2);
        outcome.RegionalOutcomes.Select(regional => regional.RegionId).Should().Equal("NSW1", "VIC1");
    }

    [Fact]
    public void Create_UsesTechnologyUnionAndZeroFillsAbsentRegionalFlows()
    {
        SystemDispatchOutcome outcome = SystemDispatchOutcome.Create(System("NSW1", "VIC1"),
        [
            Outcome("NSW1", GenerationTechnology.Coal, [100, 100]),
            Outcome("VIC1", GenerationTechnology.Wind, [50, 50]),
        ]);

        outcome.PerFleetGeneration.Keys.Should().BeEquivalentTo(
            [GenerationTechnology.Coal, GenerationTechnology.Wind]);
        AssertFlow(outcome.PerFleetCurtailment[GenerationTechnology.Coal], 0, 0);
        AssertFlow(outcome.PerFleetCharge[GenerationTechnology.Wind], 0, 0);
        AssertFlow(outcome.PerFleetDelivered[GenerationTechnology.Coal], 100, 100);
        AssertFlow(outcome.PerFleetDelivered[GenerationTechnology.Wind], 50, 50);
    }

    [Fact]
    public void Create_SumsStateOfChargeByStorageTechnology()
    {
        SystemDispatchOutcome outcome = SystemDispatchOutcome.Create(System("NSW1", "VIC1"),
        [
            Outcome("NSW1", GenerationTechnology.Coal, [100, 100], stateOfCharge: [3, 4]),
            Outcome("VIC1", GenerationTechnology.Gas, [100, 100], stateOfCharge: [7, 9]),
        ]);

        outcome.StateOfChargeByTechnology[StorageTechnology.Battery][0].MegawattHours.Should().Be(10);
        outcome.StateOfChargeByTechnology[StorageTechnology.Battery][1].MegawattHours.Should().Be(13);
    }

    [Fact]
    public void Create_PreservesSystemEnergyIdentityForEveryInterval()
    {
        SystemDispatchOutcome outcome = SystemDispatchOutcome.Create(System("NSW1", "VIC1"),
        [
            Outcome("NSW1", GenerationTechnology.Coal, [120, 90], demand: [100, 100], unserved: [0, 10], charge: [20, 0]),
            Outcome("VIC1", GenerationTechnology.Gas, [80, 110], demand: [100, 100], discharge: [20, 0], curtailment: [0, 10]),
        ]);

        AssertFlow(outcome.Charge, 20, 0);
        AssertFlow(outcome.Discharge, 20, 0);
        AssertFlow(outcome.PerFleetCurtailment[GenerationTechnology.Gas], 0, 10);
        for (int index = 0; index < outcome.Length; index++)
        {
            double inputs = outcome.PerFleetGeneration.Values.Sum(flow => flow[index].Megawatts)
                + outcome.Discharge[index].Megawatts
                + outcome.Unserved[index].Megawatts;
            double outputs = outcome.Demand[index].Megawatts
                + outcome.Charge[index].Megawatts
                + outcome.PerFleetCurtailment.Values.Sum(flow => flow[index].Megawatts)
                + outcome.TransmissionLosses[index].Megawatts;
            inputs.Should().BeApproximately(outputs, 1e-9);
        }
    }

    [Fact]
    public void Create_ReconcilesTransmissionLossesAgainstImportsAndExports()
    {
        PowerSystem system = LinkedSystem();
        DispatchOutcome[] outcomes =
        [
            Outcome("NSW1", GenerationTechnology.Coal, [200, 200], demand: [100, 100], exports: [100, 100]),
            Outcome("VIC1", GenerationTechnology.Gas, [5, 5], demand: [100, 100], imports: [95, 95]),
        ];
        SystemDispatchOutcome outcome = SystemDispatchOutcome.Create(
            system,
            new SystemDispatchRunResult(
                outcomes,
                [new InterconnectorFlow(
                    system.Interconnectors.Single(),
                    Hourly([100, 100]),
                    Hourly([5, 5]))]));

        AssertFlow(outcome.Exports, 100, 100);
        AssertFlow(outcome.Imports, 95, 95);
        AssertFlow(outcome.TransmissionLosses, 5, 5);
    }

    [Fact]
    public void Create_AcceptsCaseInsensitiveSolverEvidenceTopology()
    {
        PowerSystem system = LinkedSystem();
        DispatchOutcome[] outcomes =
        [
            Outcome("NSW1", GenerationTechnology.Coal, [200, 200], demand: [100, 100], exports: [100, 100]),
            Outcome("VIC1", GenerationTechnology.Gas, [5, 5], demand: [100, 100], imports: [95, 95]),
        ];

        SystemDispatchOutcome outcome = SystemDispatchOutcome.Create(
            system,
            new SystemDispatchRunResult(
                outcomes,
                [new InterconnectorFlow(
                    new Interconnector("nsw1", "vic1", Power.FromMegawatts(1_000)),
                    Hourly([100, 100]),
                    Hourly([5, 5]))]));

        outcome.InterconnectorFlows.Single().Interconnector.FromRegionId.Should().Be("nsw1");
    }

    [Fact]
    public void Create_RejectsBoundaryFlowsWithoutInterconnectors()
    {
        var act = () => SystemDispatchOutcome.Create(System("NSW1"),
            [Outcome("NSW1", GenerationTechnology.Coal, [99, 100], demand: [100, 100], imports: [1, 0])]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*NSW1*boundary flow at index 0*no interconnectors*");
    }

    [Fact]
    public void Create_LinkedOutcomesWithoutSolverEvidence_Throws()
    {
        var act = () => SystemDispatchOutcome.Create(LinkedSystem(),
        [
            Outcome("NSW1", GenerationTechnology.Coal, [200, 200], demand: [100, 100], exports: [100, 100]),
            Outcome("VIC1", GenerationTechnology.Gas, [5, 5], demand: [100, 100], imports: [95, 95]),
        ]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*linked power system*solver evidence*");
    }

    [Fact]
    public void Create_RejectsSolverLossesThatDoNotMatchRegionalBoundaryFlows()
    {
        PowerSystem system = LinkedSystem();
        DispatchOutcome[] outcomes =
        [
            Outcome("NSW1", GenerationTechnology.Coal, [200, 200], demand: [100, 100], exports: [100, 100]),
            Outcome("VIC1", GenerationTechnology.Gas, [5, 5], demand: [100, 100], imports: [95, 95]),
        ];

        var act = () => SystemDispatchOutcome.Create(
            system,
            new SystemDispatchRunResult(
                outcomes,
                [new InterconnectorFlow(
                    system.Interconnectors.Single(),
                    Hourly([100, 100]),
                    Hourly([4, 4]))]));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*transmission loss reconciliation failed at index 0*");
    }

    [Fact]
    public void Create_RejectsSolverLossGreaterThanItsDirectedFlow()
    {
        PowerSystem system = LinkedSystem();
        DispatchOutcome[] outcomes =
        [
            Outcome("NSW1", GenerationTechnology.Coal, [102, 102], demand: [100, 100], exports: [2, 2]),
            Outcome("VIC1", GenerationTechnology.Gas, [0, 0], demand: [100, 100], unserved: [100, 100]),
        ];

        var act = () => SystemDispatchOutcome.Create(
            system,
            new SystemDispatchRunResult(
                outcomes,
                [new InterconnectorFlow(
                    system.Interconnectors.Single(),
                    Hourly([1, 1]),
                    Hourly([2, 2]))]));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Solver evidence exceeds non-negative limits*");
    }

    [Fact]
    public void Create_RejectsNegativeBoundaryFlow()
    {
        var act = () => SystemDispatchOutcome.Create(System("NSW1"),
            [Outcome("NSW1", GenerationTechnology.Coal, [99, 100], demand: [100, 100], exports: [-1, 0])]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*NSW1*negative imports or exports at index 0*");
    }

    [Fact]
    public void Create_RejectsMissingUnknownAndDuplicateRegions()
    {
        var missing = () => SystemDispatchOutcome.Create(System("NSW1", "VIC1"),
            [Outcome("NSW1", GenerationTechnology.Coal, [100, 100])]);
        var unknown = () => SystemDispatchOutcome.Create(System("NSW1"),
            [Outcome("QLD1", GenerationTechnology.Coal, [100])]);
        var duplicate = () => SystemDispatchOutcome.Create(System("NSW1"),
        [
            Outcome("NSW1", GenerationTechnology.Coal, [100]),
            Outcome("nsw1", GenerationTechnology.Gas, [100]),
        ]);

        missing.Should().Throw<ArgumentException>().WithMessage("*missing region 'VIC1'*");
        unknown.Should().Throw<ArgumentException>().WithMessage("*unknown region 'QLD1'*");
        duplicate.Should().Throw<ArgumentException>().WithMessage("*duplicate region 'nsw1'*");
    }

    [Fact]
    public void Create_RejectsOutcomeWhoseDemandDoesNotMatchItsSystemRegionTimeline()
    {
        var act = () => SystemDispatchOutcome.Create(System("NSW1"),
            [Outcome("NSW1", GenerationTechnology.Coal, [100], start: NemStart.AddHours(1))]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Dispatch timeline does not match region 'NSW1'*");
    }

    private static PowerSystem System(params string[] regionIds) => new(
        new PowerSystemId("test-system"),
        new ScenarioId("test-scenario"),
        regionIds.Select(regionId => new Region(
            regionId,
            [new GeneratingFleet(GenerationTechnology.Coal, Power.FromMegawatts(1_000))],
            Hourly([100, 100]))).ToArray());

    private static PowerSystem LinkedSystem() => System("NSW1", "VIC1").WithInterconnectors(
    [
        new Interconnector(
            "NSW1",
            "VIC1",
            Power.FromMegawatts(1_000)),
    ]);

    private static DispatchOutcome Outcome(
        string regionId,
        GenerationTechnology technology,
        double[] generation,
        double[]? demand = null,
        double[]? unserved = null,
        double[]? charge = null,
        double[]? discharge = null,
        double[]? curtailment = null,
        double[]? imports = null,
        double[]? exports = null,
        double[]? stateOfCharge = null,
        DateTimeOffset? start = null)
    {
        int length = generation.Length;
        FlowSeries generationFlow = Hourly(generation, start);
        FlowSeries demandFlow = Hourly(demand ?? generation, start);
        FlowSeries unservedFlow = Hourly(unserved ?? new double[length], start);
        FlowSeries chargeFlow = Hourly(charge ?? new double[length], start);
        FlowSeries dischargeFlow = Hourly(discharge ?? new double[length], start);
        FlowSeries curtailmentFlow = Hourly(curtailment ?? new double[length], start);
        FlowSeries importsFlow = Hourly(imports ?? new double[length], start);
        FlowSeries exportsFlow = Hourly(exports ?? new double[length], start);
        FlowSeries zero = Hourly(new double[length], start);
        FlowSeries delivered = generationFlow.Subtract(curtailmentFlow).Subtract(chargeFlow);
        return new DispatchOutcome(
            regionId,
            new Dictionary<GenerationTechnology, FlowSeries> { [technology] = generationFlow },
            new Dictionary<GenerationTechnology, FlowSeries> { [technology] = curtailmentFlow },
            new Dictionary<GenerationTechnology, FlowSeries> { [technology] = delivered },
            new Dictionary<GenerationTechnology, FlowSeries> { [technology] = chargeFlow },
            demandFlow,
            unservedFlow,
            chargeFlow,
            dischargeFlow,
            importsFlow,
            exportsFlow,
            stateOfCharge is null
                ? null
                : new Dictionary<StorageTechnology, StockSeries>
                {
                    [StorageTechnology.Battery] = new StockSeries(
                        start ?? NemStart,
                        TimeSpan.FromHours(1),
                        stateOfCharge),
                });
    }

    private static FlowSeries Hourly(double[] values, DateTimeOffset? start = null) =>
        new(start ?? NemStart, TimeSpan.FromHours(1), values);

    private static void AssertFlow(FlowSeries flow, params double[] expected) =>
        Enumerable.Range(0, flow.Length)
            .Select(index => flow[index].Megawatts)
            .Should().Equal(expected);
}