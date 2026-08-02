using NEM.Model.Simulation;

namespace NEM.Model.StorageSizing;

public sealed record InstalledBatteryAssessment
{
    public InstalledBatteryAssessment(
        DispatchOutcome dispatchOutcome,
        RegionalBatterySizing batteryCapacity,
        bool meetsTarget,
        string evidence)
    {
        ArgumentNullException.ThrowIfNull(dispatchOutcome);
        ArgumentNullException.ThrowIfNull(batteryCapacity);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);
        if (!string.Equals(
                dispatchOutcome.RegionId,
                batteryCapacity.RegionId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Dispatch outcome and installed Battery capacity must describe the same region.",
                nameof(batteryCapacity));
        }

        DispatchOutcome = dispatchOutcome;
        BatteryCapacity = batteryCapacity;
        MeetsTarget = meetsTarget;
        Evidence = evidence;
    }

    public DispatchOutcome DispatchOutcome { get; }
    public RegionalBatterySizing BatteryCapacity { get; }
    public bool MeetsTarget { get; }
    public string Evidence { get; }
    public ReliabilityMetrics Reliability => DispatchOutcome.Reliability;
}