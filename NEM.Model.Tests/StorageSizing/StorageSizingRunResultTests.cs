using FluentAssertions;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.StorageSizing;
using NEM.Model.Units;

namespace NEM.Model.Tests.StorageSizing;

public sealed class StorageSizingRunResultTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    public void Constructor_RejectsDispatchFromDifferentGenerationTopology()
    {
        FlowSeries demand = new(Start, TimeSpan.FromHours(1), [10]);
        PowerSystem finalSystem = SystemWith(GenerationTechnology.Coal, demand);
        DispatchOutcome gasDispatch = Dispatcher.Dispatch(
            SystemWith(GenerationTechnology.Gas, demand)).Single();
        var regionalResult = new RegionalSizingResult(
            gasDispatch,
            new RegionalBatterySizing("NSW1", Energy.Zero, Power.Zero, wasChanged: false),
            meetsTarget: true,
            StorageSizingStatus.TargetMet,
            "Test dispatch is compliant.");

        var act = () => new StorageSizingRunResult(
            finalSystem,
            [regionalResult],
            [],
            dispatchPassCount: 1,
            StorageSizingStatus.TargetMet,
            "Test dispatch is compliant.");

        act.Should().Throw<ArgumentException>().WithParameterName("regions");
    }

    private static PowerSystem SystemWith(
        GenerationTechnology technology,
        FlowSeries demand) =>
        new(
            new PowerSystemId($"{technology}-system"),
            new ScenarioId("scenario"),
            [new Region(
                "NSW1",
                [new GeneratingFleet(technology, Power.FromMegawatts(20))],
                demand)]);
}