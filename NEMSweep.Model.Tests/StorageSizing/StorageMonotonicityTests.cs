using AwesomeAssertions;
using NEMSweep.Model.Generation.Solar;
using NEMSweep.Model.Generation.Wind;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Scenarios;
using NEMSweep.Model.Series;
using NEMSweep.Model.Simulation;
using NEMSweep.Model.Units;
using NEMSweep.Model.Weather;

namespace NEMSweep.Model.Tests.StorageSizing;

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

    [Fact]
    public void Dispatch_RandomizedDispatchableCharging_IncreasingBatteryCapacityDoesNotIncreaseUse()
    {
        var random = new Random(20260812);
        for (int sample = 0; sample < 100; sample++)
        {
            double[] demand = Enumerable.Range(0, 24)
                .Select(hour => hour % 4 == 0 ? 0d : random.Next(20, 101))
                .ToArray();
            double[] solar = Enumerable.Range(0, 24)
                .Select(hour => hour % 4 == 0 ? 2_000d : 0d)
                .ToArray();
            double energyMwh = random.Next(20, 201);
            double powerMw = random.Next(10, 51);

            DispatchOutcome baseCase = DispatchWithDispatchableCharging(
                demand,
                solar,
                energyMwh,
                powerMw);
            DispatchOutcome greaterEnergy = DispatchWithDispatchableCharging(
                demand,
                solar,
                energyMwh + 20,
                powerMw);
            DispatchOutcome greaterPower = DispatchWithDispatchableCharging(
                demand,
                solar,
                energyMwh,
                powerMw + 10);

            greaterEnergy.Reliability.UnservedEnergy.Should().BeLessThanOrEqualTo(
                baseCase.Reliability.UnservedEnergy,
                "sample {0}: increasing battery energy must not increase USE", sample);
            greaterPower.Reliability.UnservedEnergy.Should().BeLessThanOrEqualTo(
                baseCase.Reliability.UnservedEnergy,
                "sample {0}: increasing battery power must not increase USE", sample);
        }
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
                    Power.FromMegawatts(powerMw),
                    new StorageTechnologyProfile(15u, 0.87),
                    Energy.Zero),
            ]);
        var system = new PowerSystem(
            new PowerSystemId("test-system"),
            new ScenarioId("test-scenario"),
            [region]);

        return Dispatcher.Dispatch(system).Single();
    }

    private static DispatchOutcome DispatchWithDispatchableCharging(
        double[] demandMw,
        double[] directNormalRadiation,
        double energyMwh,
        double powerMw)
    {
        FlowSeries demand = Flow(demandMw);
        var region = new Region(
            "NSW1",
            [
                new GeneratingFleet(GenerationTechnology.Solar, Power.FromMegawatts(100)),
                new GeneratingFleet(
                    GenerationTechnology.Coal,
                    Power.FromMegawatts(100),
                    shortRunMarginalCost: GenerationEnergyCost.FromAudPerMwhGenerated(20)),
                new GeneratingFleet(
                    GenerationTechnology.Gas,
                    Power.FromMegawatts(100),
                    shortRunMarginalCost: GenerationEnergyCost.FromAudPerMwhGenerated(80)),
            ],
            demand,
            resourceProfile: Resources(demand, directNormalRadiation),
            storageFleets:
            [
                new StorageFleet(
                    StorageTechnology.Battery,
                    Energy.FromMegawattHours(energyMwh),
                    Power.FromMegawatts(powerMw),
                    new StorageTechnologyProfile(15u, 0.87),
                    Energy.Zero),
            ]);
        var system = new PowerSystem(
            new PowerSystemId("dispatchable-charging-monotonicity"),
            new ScenarioId("dispatchable-charging-monotonicity"),
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
