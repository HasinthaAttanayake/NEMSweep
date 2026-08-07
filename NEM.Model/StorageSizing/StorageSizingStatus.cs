namespace NEM.Model.StorageSizing;

/// <summary>Terminal status of a storage sizing search or regional sizing result.</summary>
public enum StorageSizingStatus
{
    /// <summary>The configured reliability target was met.</summary>
    TargetMet,
    /// <summary>The configured Battery capacity limit was reached before the target was met.</summary>
    BatteryCapacityLimitReached,
    /// <summary>The configured dispatch-pass limit was reached before the target was met.</summary>
    PassLimitReached,
}