namespace NEM.Model.StorageSizing;

/// <summary>Terminal status of a storage sizing search or regional sizing result.</summary>
public enum StorageSizingStatus
{
    /// <summary>The configured reliability target was met.</summary>
    TargetMet,
    /// <summary>
    /// Available generation energy is below demand energy over the dispatch period, so no Battery
    /// size can meet the reliability target without additional generation.
    /// </summary>
    EnergyLimited,
    /// <summary>
    /// Every feasible larger Battery power, energy, and combined-growth probe failed to materially
    /// reduce unserved energy before the configured capacity limits were reached.
    /// </summary>
    StorageNoLongerImprovesReliability,
    /// <summary>The configured Battery capacity limit was reached before the target was met.</summary>
    BatteryCapacityLimitReached,
    /// <summary>The configured dispatch-pass limit was reached before the target was met.</summary>
    PassLimitReached,
}