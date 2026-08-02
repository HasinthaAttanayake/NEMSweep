using FluentAssertions;
using NEM.Model.Generation.Solar;
using NEM.Model.Generation.Wind;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.Units;
using NEM.Model.Weather;

namespace NEM.Model.Tests.StorageSizing;

public sealed class StorageMonotonicityTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 1, 12, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    public void Dispatch_IncreasingBatteryEnergyAtFixedPower_DoesNotIncreaseUse()
    {
        double[] demand = [0, 0, 0, 20, 20, 20, 20, 20, 20, 20, 20];
        double[] solar = [2_000, 2_000, 2_000, 0, 0, 0, 0, 0, 0, 0, 0];

        DispatchOutcome smaller = Dispatch(demand, solar, energyMwh: 120, powerMw: 30);
        DispatchOutcome larger = Dispatch(demand, solar, energyMwh: 240, powerMw: 30);

        larger.Reliability.UnservedEnergy.Should()
            .BeLessThanOrEqualTo(smaller.Reliability.UnservedEnergy);
    }

    [Fact]
    public void Dispatch_IncreasingBatteryPowerAtFixedEnergy_DoesNotIncreaseUse()
    {
        double[] demand = [0, 50];
        double[] solar = [2_000, 0];

        DispatchOutcome smaller = Dispatch(demand, solar, energyMwh: 240, powerMw: 30);
        DispatchOutcome larger = Dispatch(demand, solar, energyMwh: 240, powerMw: 60);

        larger.Reliability.UnservedEnergy.Should()
            .BeLessThanOrEqualTo(smaller.Reliability.UnservedEnergy);
    }

    private static DispatchOutcome Dispatch(
        double[] demandMw,
        double[] directNormalRadiation,
        double energyMwh,
        double powerMw)
    {
        FlowSeries demand = Flow(demandMw);
        var region = new Region(
            "NSW1",
            [new GeneratingFleet(GenerationTechnology.Solar, Power.FromMegawatts(100))],
            demand,
            resourceProfile: Resources(demand, directNormalRadiation),
            storageFleets:
            [
                new StorageFleet(
                    StorageTechnology.Battery,
                    Energy.FromMegawattHours(energyMwh),
                    Power.FromMegawatts(powerMw)),
            ]);
        var system = new PowerSystem(
            new PowerSystemId("test-system"),
            new ScenarioId("test-scenario"),
            [region]);

        return Dispatcher.Dispatch(system).Single();
    }

    private static FlowSeries Flow(double[] values) =>
        new(Start, TimeSpan.FromHours(1), values);

    internal static RegionalResourceProfile Resources(
        FlowSeries timeline,
        double[] directNormalRadiation)
    {
        var zeros = new double[timeline.Length];
        var ratedWind = Enumerable.Repeat(
            WindPowerCurve.RatedWindSpeedMetresPerSecond,
            timeline.Length).ToArray();
        return new RegionalResourceProfile(
            TraceSeries.GlobalHorizontalRadiation(timeline.Start, timeline.Resolution, zeros),
            TraceSeries.DirectNormalRadiation(
                timeline.Start,
                timeline.Resolution,
                directNormalRadiation),
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
                ratedWind,
                WindPowerCurve.DefaultHubHeightMetres));
    }
}
