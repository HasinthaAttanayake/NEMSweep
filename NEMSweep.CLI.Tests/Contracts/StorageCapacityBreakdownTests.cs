using AwesomeAssertions;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Tests.Contracts;

public sealed class StorageCapacityBreakdownTests
{
    [Fact]
    public void From_SizedRun_SeparatesDeclaredBatteryFromAddedBattery()
    {
        StorageCapacityBreakdownDTO breakdown = StorageCapacityBreakdown.From(
            [Fleet("Battery", energyMwh: 400, powerMw: 100)],
            Sizing(initialEnergyMwh: 150, initialPowerMw: 40, finalEnergyMwh: 400, finalPowerMw: 100));

        breakdown.DeclaredBatteryEnergyMwh.Should().Be(150);
        breakdown.DeclaredBatteryPowerMw.Should().Be(40);
        breakdown.AddedBatteryEnergyMwh.Should().Be(250);
        breakdown.AddedBatteryPowerMw.Should().Be(60);
        breakdown.TotalBatteryEnergyMwh.Should().Be(400);
        breakdown.TotalBatteryPowerMw.Should().Be(100);
    }

    [Fact]
    public void From_FixedFleet_TreatsEveryInstalledBatteryAsDeclared()
    {
        StorageCapacityBreakdownDTO breakdown = StorageCapacityBreakdown.From(
            [Fleet("Battery", energyMwh: 400, powerMw: 100)],
            sizing: null);

        breakdown.DeclaredBatteryEnergyMwh.Should().Be(400);
        breakdown.AddedBatteryEnergyMwh.Should().Be(0);
        breakdown.AddedBatteryPowerMw.Should().Be(0);
        breakdown.TotalBatteryEnergyMwh.Should().Be(400);
    }

    /// <summary>
    /// The whole point of the block: a result with declared pumped hydro has no other field
    /// standing for battery alone, so the installed total cannot be read as one.
    /// </summary>
    [Fact]
    public void From_PumpedHydroAlongsideBattery_KeepsTheTwoApartAndReconcilesToTheTotal()
    {
        DispatchStorageFleetDTO[] fleets =
        [
            Fleet("Battery", energyMwh: 400, powerMw: 100),
            Fleet("PumpedHydro", energyMwh: 15_680, powerMw: 2_500),
        ];

        StorageCapacityBreakdownDTO breakdown = StorageCapacityBreakdown.From(
            fleets,
            Sizing(initialEnergyMwh: 0, initialPowerMw: 0, finalEnergyMwh: 400, finalPowerMw: 100));

        breakdown.TotalBatteryEnergyMwh.Should().Be(400);
        breakdown.AddedBatteryEnergyMwh.Should().Be(400);
        breakdown.PumpedHydroEnergyMwh.Should().Be(15_680);
        breakdown.PumpedHydroPowerMw.Should().Be(2_500);
        breakdown.OtherStorageEnergyMwh.Should().Be(0);

        double installedEnergy = fleets.Sum(fleet => fleet.EnergyCapacityMwh);
        (breakdown.TotalBatteryEnergyMwh
            + breakdown.PumpedHydroEnergyMwh
            + breakdown.OtherStorageEnergyMwh).Should().Be(installedEnergy);
    }

    [Fact]
    public void From_UnknownTechnology_CountsAsOtherStorageRatherThanBattery()
    {
        StorageCapacityBreakdownDTO breakdown = StorageCapacityBreakdown.From(
            [Fleet("CompressedAir", energyMwh: 90, powerMw: 30)],
            sizing: null);

        breakdown.TotalBatteryEnergyMwh.Should().Be(0);
        breakdown.PumpedHydroEnergyMwh.Should().Be(0);
        breakdown.OtherStorageEnergyMwh.Should().Be(90);
        breakdown.OtherStoragePowerMw.Should().Be(30);
    }

    [Fact]
    public void From_TechnologyNameCasing_DoesNotChangeWhichFleetIsBattery()
    {
        StorageCapacityBreakdownDTO breakdown = StorageCapacityBreakdown.From(
            [Fleet("battery", energyMwh: 10, powerMw: 5), Fleet("pumpedhydro", energyMwh: 20, powerMw: 6)],
            sizing: null);

        breakdown.TotalBatteryEnergyMwh.Should().Be(10);
        breakdown.PumpedHydroEnergyMwh.Should().Be(20);
        breakdown.OtherStorageEnergyMwh.Should().Be(0);
    }

    /// <summary>
    /// Declared capacity comes from separate evidence to installed capacity, so the two can
    /// disagree. A negative "added" would read as a figure rather than as nonsense.
    /// </summary>
    [Fact]
    public void From_DeclaredExceedingInstalled_ReportsNoAddedCapacityRatherThanANegativeOne()
    {
        StorageCapacityBreakdownDTO breakdown = StorageCapacityBreakdown.From(
            [Fleet("Battery", energyMwh: 100, powerMw: 25)],
            Sizing(initialEnergyMwh: 150, initialPowerMw: 40, finalEnergyMwh: 100, finalPowerMw: 25));

        breakdown.AddedBatteryEnergyMwh.Should().Be(0);
        breakdown.AddedBatteryPowerMw.Should().Be(0);
    }

    [Fact]
    public void Sum_AddsEveryFieldAcrossRegions()
    {
        StorageCapacityBreakdownDTO total = StorageCapacityBreakdown.Sum(
        [
            new StorageCapacityBreakdownDTO(1, 2, 3, 4, 5, 6, 7, 8, 9, 10),
            new StorageCapacityBreakdownDTO(10, 20, 30, 40, 50, 60, 70, 80, 90, 100),
        ]);

        total.Should().Be(new StorageCapacityBreakdownDTO(11, 22, 33, 44, 55, 66, 77, 88, 99, 110));
    }

    [Fact]
    public void Sum_NoRegions_IsZeroRatherThanAbsent()
    {
        StorageCapacityBreakdown.Sum([])
            .Should().Be(new StorageCapacityBreakdownDTO(0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
    }

    private static DispatchStorageFleetDTO Fleet(string technology, double energyMwh, double powerMw) =>
        new(technology, energyMwh, powerMw);

    private static StorageSizingOutcomeDTO Sizing(
        double initialEnergyMwh,
        double initialPowerMw,
        double finalEnergyMwh,
        double finalPowerMw) =>
        new(
            StorageSizingOutcome.Resized,
            initialEnergyMwh,
            initialPowerMw,
            finalEnergyMwh,
            finalPowerMw,
            MaximumEnergyMwh: 100_000,
            MaximumPowerMw: 10_000,
            PassesUsed: 7);
}
