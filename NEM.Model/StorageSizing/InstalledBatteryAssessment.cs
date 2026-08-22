using NEM.Model.Simulation;

namespace NEM.Model.StorageSizing;

/// <summary>
/// Dispatch and reliability assessment of the Battery capacity installed before a sizing search.
/// </summary>
public sealed record InstalledBatteryAssessment
{
    /// <summary>Validates and creates an installed-capacity assessment.</summary>
    /// <param name="dispatchOutcome">Dispatch outcome with the originally installed Battery capacity.</param>
    /// <param name="batteryCapacity">Originally installed total Battery capacity.</param>
    /// <param name="meetsTarget">Whether the installed capacity meets the configured reliability target.</param>
    /// <param name="evidence">Human-readable evidence for the target assessment.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="dispatchOutcome"/> and <paramref name="batteryCapacity"/> describe different regions.
    /// </exception>
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

    /// <summary>Dispatch outcome with the originally installed Battery capacity.</summary>
    public DispatchOutcome DispatchOutcome { get; }
    /// <summary>Originally installed total Battery capacity.</summary>
    public RegionalBatterySizing BatteryCapacity { get; }
    /// <summary>Whether the installed capacity meets the configured reliability target.</summary>
    public bool MeetsTarget { get; }
    /// <summary>Human-readable evidence for the target assessment.</summary>
    public string Evidence { get; }
    /// <summary>Reliability calculated from <see cref="DispatchOutcome"/>.</summary>
    public ReliabilityMetrics Reliability => DispatchOutcome.Reliability;
}