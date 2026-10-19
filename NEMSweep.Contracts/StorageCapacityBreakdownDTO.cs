using System.Text.Json.Serialization;

namespace NEMSweep.Contracts;

/// <summary>
/// Installed storage energy and power, split by what the sizing search may grow and what it may
/// not. Published because the two figures a reader wants are not the same quantity and were not
/// separately available: <c>storageEnergyMwh</c> counts every storage technology, while
/// <see cref="StorageSizingOutcomeDTO.FinalEnergyMwh"/> counts Battery alone, and a result with
/// declared pumped hydro has no field standing for "battery built". Quoting the former as a battery
/// figure overstates it by whatever fixed storage the scenario declared.
/// </summary>
/// <param name="DeclaredBatteryEnergyMwh">Battery energy the scenario declared, before sizing.</param>
/// <param name="DeclaredBatteryPowerMw">Battery power the scenario declared, before sizing.</param>
/// <param name="AddedBatteryEnergyMwh">Battery energy the sizing search added. Zero in <c>fixedFleet</c>.</param>
/// <param name="AddedBatteryPowerMw">Battery power the sizing search added. Zero in <c>fixedFleet</c>.</param>
/// <param name="TotalBatteryEnergyMwh">Battery energy installed in the dispatched system.</param>
/// <param name="TotalBatteryPowerMw">Battery power installed in the dispatched system.</param>
/// <param name="PumpedHydroEnergyMwh">Declared pumped-hydro energy. Never sized.</param>
/// <param name="PumpedHydroPowerMw">Declared pumped-hydro power. Never sized.</param>
/// <param name="OtherStorageEnergyMwh">Declared energy of any storage technology that is neither. Never sized.</param>
/// <param name="OtherStoragePowerMw">Declared power of any storage technology that is neither. Never sized.</param>
/// <remarks>
/// The energy fields sum to the installed storage energy a result reports elsewhere, and the power
/// fields likewise, so the breakdown reconciles with the existing totals rather than restating them
/// on a different basis.
/// </remarks>
public sealed record StorageCapacityBreakdownDTO(
    [property: JsonRequired] double DeclaredBatteryEnergyMwh,
    [property: JsonRequired] double DeclaredBatteryPowerMw,
    [property: JsonRequired] double AddedBatteryEnergyMwh,
    [property: JsonRequired] double AddedBatteryPowerMw,
    [property: JsonRequired] double TotalBatteryEnergyMwh,
    [property: JsonRequired] double TotalBatteryPowerMw,
    [property: JsonRequired] double PumpedHydroEnergyMwh,
    [property: JsonRequired] double PumpedHydroPowerMw,
    [property: JsonRequired] double OtherStorageEnergyMwh,
    [property: JsonRequired] double OtherStoragePowerMw);

/// <summary>
/// Derives a <see cref="StorageCapacityBreakdownDTO"/> from the storage fleets a run dispatched and
/// the sizing evidence it recorded. One implementation, shared by every artifact that publishes the
/// breakdown, so a region, the system and a sweep scalar cannot disagree about what "battery" means.
/// </summary>
public static class StorageCapacityBreakdown
{
    /// <summary>The storage technology the sizing search may grow. Everything else is fixed.</summary>
    public const string BatteryTechnology = "Battery";

    /// <summary>The storage technology the worked example declares and holds fixed.</summary>
    public const string PumpedHydroTechnology = "PumpedHydro";

    /// <summary>Splits installed storage by technology, and Battery by declared against added.</summary>
    /// <param name="fleets">Every storage fleet of the dispatched system, at its post-sizing capacity.</param>
    /// <param name="sizing">
    /// Sizing evidence for the same scope, or <see langword="null"/> for a fixed-fleet run. Its
    /// initial capacity is what the scenario declared, so it is the only thing that can tell
    /// declared Battery from added Battery once the two have been summed into one fleet.
    /// </param>
    public static StorageCapacityBreakdownDTO From(
        IEnumerable<DispatchStorageFleetDTO> fleets,
        StorageSizingOutcomeDTO? sizing)
    {
        ArgumentNullException.ThrowIfNull(fleets);
        double batteryEnergy = 0;
        double batteryPower = 0;
        double pumpedHydroEnergy = 0;
        double pumpedHydroPower = 0;
        double otherEnergy = 0;
        double otherPower = 0;
        foreach (DispatchStorageFleetDTO fleet in fleets)
        {
            if (IsTechnology(fleet.Technology, BatteryTechnology))
            {
                batteryEnergy += fleet.EnergyCapacityMwh;
                batteryPower += fleet.PowerCapacityMw;
            }
            else if (IsTechnology(fleet.Technology, PumpedHydroTechnology))
            {
                pumpedHydroEnergy += fleet.EnergyCapacityMwh;
                pumpedHydroPower += fleet.PowerCapacityMw;
            }
            else
            {
                otherEnergy += fleet.EnergyCapacityMwh;
                otherPower += fleet.PowerCapacityMw;
            }
        }

        // A fixed-fleet run grew nothing, so everything installed was declared. Clamped because the
        // declared figure comes from separate evidence: a sizing outcome that shrank a fleet would
        // otherwise publish a negative "added", which reads as a number rather than as nonsense.
        double declaredEnergy = sizing?.InitialEnergyMwh ?? batteryEnergy;
        double declaredPower = sizing?.InitialPowerMw ?? batteryPower;
        return new StorageCapacityBreakdownDTO(
            declaredEnergy,
            declaredPower,
            Math.Max(0, batteryEnergy - declaredEnergy),
            Math.Max(0, batteryPower - declaredPower),
            batteryEnergy,
            batteryPower,
            pumpedHydroEnergy,
            pumpedHydroPower,
            otherEnergy,
            otherPower);
    }

    /// <summary>Sums region breakdowns into the whole-system breakdown.</summary>
    /// <param name="regions">One breakdown per region, each already derived from that region's fleets.</param>
    public static StorageCapacityBreakdownDTO Sum(IEnumerable<StorageCapacityBreakdownDTO> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var total = new StorageCapacityBreakdownDTO(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        foreach (StorageCapacityBreakdownDTO region in regions)
        {
            total = new StorageCapacityBreakdownDTO(
                total.DeclaredBatteryEnergyMwh + region.DeclaredBatteryEnergyMwh,
                total.DeclaredBatteryPowerMw + region.DeclaredBatteryPowerMw,
                total.AddedBatteryEnergyMwh + region.AddedBatteryEnergyMwh,
                total.AddedBatteryPowerMw + region.AddedBatteryPowerMw,
                total.TotalBatteryEnergyMwh + region.TotalBatteryEnergyMwh,
                total.TotalBatteryPowerMw + region.TotalBatteryPowerMw,
                total.PumpedHydroEnergyMwh + region.PumpedHydroEnergyMwh,
                total.PumpedHydroPowerMw + region.PumpedHydroPowerMw,
                total.OtherStorageEnergyMwh + region.OtherStorageEnergyMwh,
                total.OtherStoragePowerMw + region.OtherStoragePowerMw);
        }

        return total;
    }

    /// <summary>
    /// Matches a published technology name. Ordinal-ignore-case because the name is the scenario
    /// author's string, and a scenario declaring <c>battery</c> is declaring the fleet the search
    /// grows.
    /// </summary>
    private static bool IsTechnology(string technology, string expected) =>
        string.Equals(technology, expected, StringComparison.OrdinalIgnoreCase);
}
