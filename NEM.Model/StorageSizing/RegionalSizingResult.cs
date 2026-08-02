using NEM.Model.Simulation;
using NEM.Model.Units;

namespace NEM.Model.StorageSizing;

public sealed record RegionalSizingResult
{
    public RegionalSizingResult(
        DispatchOutcome dispatchOutcome,
        RegionalBatterySizing batterySizing,
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
        Status = status;
        TerminationEvidence = terminationEvidence;
    }

    public DispatchOutcome DispatchOutcome { get; }
    public RegionalBatterySizing BatterySizing { get; }
    public StorageSizingStatus Status { get; }
    public string TerminationEvidence { get; }
    public ReliabilityMetrics Reliability => DispatchOutcome.Reliability;
    public Energy TotalUnservedEnergy => Reliability.UnservedEnergy;
    public Power PeakUnservedPower => Reliability.PeakUnservedPower;
}