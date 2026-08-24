using NEMSweep.Model.Units;

namespace NEMSweep.Model.StorageSizing;

/// <summary>Evidence from one successful whole-system dispatch attempted by storage sizing.</summary>
public sealed record StorageSizingPass(
    int Pass,
    IReadOnlyList<StorageSizingRegionPass> Regions,
    Energy SystemUnservedEnergy,
    int SystemUnservedHours);

/// <summary>One region's candidate capacity and reliability result within a sizing pass.</summary>
public sealed record StorageSizingRegionPass(
    string RegionId,
    Energy EnergyCapacity,
    Power PowerCapacity,
    Energy UnservedEnergy,
    int UnservedHours);