using NEM.Model.Simulation;
using NEM.Model.Units;

namespace NEM.Model.StorageSizing;

/// <summary>Final Battery sizing and dispatch evidence for one region.</summary>
public sealed record RegionalSizingResult
{
    public RegionalSizingResult(
        DispatchOutcome dispatchOutcome,
        RegionalBatterySizing batterySizing,
        bool meetsTarget,
        StorageSizingStatus status,
        string terminationEvidence)
    {
        ArgumentNullException.ThrowIfNull(dispatchOutcome);
        ArgumentNullException.ThrowIfNull(batterySizing);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminationEvidence);
        if (!string.Equals(
                dispatchOutcome.RegionId,
                batterySizing.RegionId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Dispatch outcome and Battery sizing must describe the same region.",
                nameof(batterySizing));
        }

        DispatchOutcome = dispatchOutcome;
        BatterySizing = batterySizing;
        MeetsTarget = meetsTarget;
        Status = status;
        TerminationEvidence = terminationEvidence;
    }

    /// <summary>Final dispatch outcome for the sized regional system.</summary>
    public DispatchOutcome DispatchOutcome { get; }
    /// <summary>Total Battery capacity selected for the region.</summary>
    public RegionalBatterySizing BatterySizing { get; }
    /// <summary>
    /// Whether the final dispatch meets the configured reliability target. The search owns this
    /// verdict so that everything downstream reports the same answer the sizing decision used.
    /// </summary>
    public bool MeetsTarget { get; }
    /// <summary>Terminal status as it applies to this region.</summary>
    public StorageSizingStatus Status { get; }
    /// <summary>Human-readable evidence explaining the regional status.</summary>
    public string TerminationEvidence { get; }
    /// <summary>Reliability calculated from <see cref="DispatchOutcome"/>.</summary>
    public ReliabilityMetrics Reliability => DispatchOutcome.Reliability;
    /// <summary>Total demand energy unserved by the final dispatch.</summary>
    public Energy TotalUnservedEnergy => Reliability.UnservedEnergy;
    /// <summary>Largest hourly unserved-demand power in the final dispatch.</summary>
    public Power PeakUnservedPower => Reliability.PeakUnservedPower;
}