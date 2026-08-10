using System.Diagnostics;
using AwesomeAssertions;
using NEM.Model.Generation.Wind;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.StorageSizing;
using NEM.Model.Units;
using NEM.Model.Weather;
using Xunit.Abstractions;

namespace NEM.Model.Tests.StorageSizing;

public sealed class FullYearSizingAcceptanceTests(ITestOutputHelper output)
{
    private const int HoursPerYear = 8_760;
    private const double WindCapacityMw = 100;
    private const double DemandMw = 50;
    private const double BalanceTolerance = 1e-9;
    private static readonly DateTimeOffset Start =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    [Trait("Category", "FullYearAcceptance")]
    public void Size_FullYear_CompletesAndPreservesImplementedInvariants()
    {
        PowerSystem installedSystem = CreateFullYearSystem();
        var stopwatch = Stopwatch.StartNew();

        StorageSizingRunResult result = StorageSizingService.Size(
            installedSystem,
            new StorageSizingOptions(
                Power.FromMegawatts(100),
                Energy.FromMegawattHours(800),
                targetUsePercentage: 0,
                maximumPasses: 256));

        stopwatch.Stop();
        RegionalSizingResult regionalResult = result.Regions.Should().ContainSingle().Subject;
        DispatchOutcome outcome = regionalResult.DispatchOutcome;
        StorageFleet battery = result.PowerSystem.Regions.Single().StorageFleets
            .Single(fleet => fleet.StorageTechnology == StorageTechnology.Battery);
        output.WriteLine($"Elapsed: {stopwatch.Elapsed.TotalSeconds:F3} seconds");
        output.WriteLine($"Dispatch passes: {result.DispatchPassCount}");
        output.WriteLine(
            $"Selected Battery: {battery.PowerCapacity.Megawatts:F0} MW / "
            + $"{battery.StorageCapacity.MegawattHours:F0} MWh");

        result.Status.Should().Be(StorageSizingStatus.TargetMet);
        outcome.Demand.Length.Should().Be(HoursPerYear);
        regionalResult.Reliability.UnservedEnergy.Should().Be(Energy.Zero);
        AssertHourlyInvariants(outcome, battery);
        AssertRunEnergyLedger(outcome);
        AssertStorageMonotonicity(result.PowerSystem, battery, regionalResult.Reliability.UnservedEnergy);
    }

    private static void AssertHourlyInvariants(DispatchOutcome outcome, StorageFleet battery)
    {
        StockSeries stateOfCharge = outcome.StateOfChargeByTechnology[StorageTechnology.Battery];
        FlowSeries windGeneration = outcome.PerFleetGeneration[GenerationTechnology.Wind];
        for (int hour = 0; hour < outcome.Demand.Length; hour++)
        {
            double generationMw = windGeneration[hour].Megawatts;
            double inputsMw = generationMw
                + outcome.Discharge[hour].Megawatts
                + outcome.Imports[hour].Megawatts
                + outcome.Unserved[hour].Megawatts;
            double outputsMw = outcome.Demand[hour].Megawatts
                + outcome.Charge[hour].Megawatts
                + outcome.Exports[hour].Megawatts
                + outcome.Curtailment[hour].Megawatts;

            inputsMw.Should().BeApproximately(outputsMw, BalanceTolerance);
            generationMw.Should().BeInRange(0, WindCapacityMw);
            outcome.Charge[hour].Should().BeLessThanOrEqualTo(battery.PowerCapacity);
            outcome.Discharge[hour].Should().BeLessThanOrEqualTo(battery.PowerCapacity);
            stateOfCharge[hour].Should().BeInRange(Energy.Zero, battery.StorageCapacity);
            (outcome.Curtailment[hour] > Power.Zero
                && outcome.Unserved[hour] > Power.Zero).Should().BeFalse();
        }
    }

    private static void AssertRunEnergyLedger(DispatchOutcome outcome)
    {
        double efficiency = BatteryProfile().RoundTripEfficiency;
        StockSeries stateOfCharge = outcome.StateOfChargeByTechnology[StorageTechnology.Battery];
        int lastHour = outcome.Demand.Length - 1;
        Energy initialStorage = stateOfCharge[0];
        Energy finalStorage = stateOfCharge[lastHour]
            + (outcome.Charge[lastHour] * outcome.Demand.Resolution * efficiency)
            - (outcome.Discharge[lastHour] * outcome.Demand.Resolution);
        Energy chargedEnergy = outcome.Charge.Integrate();
        Energy storageLosses = chargedEnergy * (1 - efficiency);
        Energy energyIn = outcome.PerFleetGeneration.Values
            .Select(series => series.Integrate())
            .Aggregate(Energy.Zero, (total, generated) => total + generated)
            + outcome.Imports.Integrate()
            + initialStorage
            + outcome.Unserved.Integrate();
        Energy energyOut = outcome.Demand.Integrate()
            + outcome.Exports.Integrate()
            + outcome.Curtailment.Integrate()
            + finalStorage
            + storageLosses;

        energyIn.MegawattHours.Should().BeApproximately(
            energyOut.MegawattHours,
            BalanceTolerance * Math.Max(1, energyIn.MegawattHours));
        (chargedEnergy - outcome.Discharge.Integrate() - finalStorage + initialStorage)
            .MegawattHours.Should().BeApproximately(
                storageLosses.MegawattHours,
                BalanceTolerance * Math.Max(1, chargedEnergy.MegawattHours));
    }

    private static void AssertStorageMonotonicity(
        PowerSystem sizedSystem,
        StorageFleet battery,
        Energy sizedUnservedEnergy)
    {
        Region region = sizedSystem.Regions.Single().WithBatteryStorage(
            battery.StorageCapacity + Energy.FromMegawattHours(1),
            battery.PowerCapacity);
        PowerSystem largerStorageSystem = sizedSystem.WithRegions([region]);

        Energy largerStorageUnservedEnergy = Dispatcher.Dispatch(largerStorageSystem)
            .Single()
            .Reliability.UnservedEnergy;

        largerStorageUnservedEnergy.Should().BeLessThanOrEqualTo(sizedUnservedEnergy);
    }

    private static PowerSystem CreateFullYearSystem()
    {
        double[] demandMw = Enumerable.Range(0, HoursPerYear)
            .Select(hour => hour % 24 < 12 ? 0 : DemandMw)
            .ToArray();
        double[] windSpeed = Enumerable.Range(0, HoursPerYear)
            .Select(hour => hour % 24 < 12
                ? WindPowerCurve.RatedWindSpeedMetresPerSecond
                : 0)
            .ToArray();
        var demand = new FlowSeries(Start, TimeSpan.FromHours(1), demandMw);
        var zeros = new double[HoursPerYear];
        var resources = new RegionalResourceProfile(
            TraceSeries.GlobalHorizontalRadiation(Start, demand.Resolution, zeros),
            TraceSeries.DirectNormalRadiation(Start, demand.Resolution, zeros),
            TraceSeries.DiffuseHorizontalRadiation(Start, demand.Resolution, zeros),
            SolarZenithSeries.Calculate(
                Start,
                demand.Resolution,
                demand.Length,
                latitude: -33.8688,
                longitude: 151.2093),
            TraceSeries.DryBulbTemperature(Start, demand.Resolution, zeros),
            TraceSeries.WindSpeed(
                Start,
                demand.Resolution,
                windSpeed,
                WindPowerCurve.DefaultHubHeightMetres));
        var region = new Region(
            "NSW1",
            [new GeneratingFleet(
                GenerationTechnology.Wind,
                Power.FromMegawatts(WindCapacityMw))],
            demand,
            resourceProfile: resources,
            storageTechnologyProfiles: new Dictionary<
                StorageTechnology,
                StorageTechnologyProfile>
            {
                [StorageTechnology.Battery] = BatteryProfile(),
            });
        return new PowerSystem(
            new PowerSystemId("full-year-sizing-acceptance"),
            new ScenarioId("full-year-sizing-acceptance"),
            [region]);
    }

    private static StorageTechnologyProfile BatteryProfile() => new(15u, 0.87);
}