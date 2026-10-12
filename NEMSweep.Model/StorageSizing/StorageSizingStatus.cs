namespace NEMSweep.Model.StorageSizing;

/// <summary>Terminal status of a whole-system storage sizing search or regional sizing result.</summary>
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
    /// For a regional result, its best larger candidate did not reduce its own unserved energy by
    /// more than 0.001 MWh. For a whole-system result, growth made no progress although every
    /// still-failing region had a larger candidate.
    /// </summary>
    StorageNoLongerImprovesReliability,
    /// <summary>
    /// For a regional result, no larger candidate was available within its configured Battery
    /// power and energy limits. For a whole-system no-progress result, at least one still-failing
    /// region had no larger candidate.
    /// </summary>
    BatteryCapacityLimitReached,
    /// <summary>
    /// The search exhausted its dispatch-pass budget during growth or refinement; the target may
    /// already be met if refinement was interrupted. A regional result carries this status only
    /// when that region still fails its target.
    /// </summary>
    PassLimitReached,
}