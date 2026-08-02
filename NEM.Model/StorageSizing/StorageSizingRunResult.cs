using System.Collections.ObjectModel;
using NEM.Model.Grid;

namespace NEM.Model.StorageSizing;

public sealed class StorageSizingRunResult
{
    public StorageSizingRunResult(
        PowerSystem powerSystem,
        IReadOnlyList<RegionalSizingResult> regions,
        IReadOnlyList<InstalledBatteryAssessment> installedBatteryAssessments,
        int dispatchPassCount,
        StorageSizingStatus status,
        string terminationEvidence)
    {
        ArgumentNullException.ThrowIfNull(powerSystem);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(installedBatteryAssessments);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminationEvidence);
        if (regions.Any(region => region is null))
        {
            throw new ArgumentException("Regional results cannot contain null.", nameof(regions));
        }

        if (installedBatteryAssessments.Any(assessment => assessment is null))
        {
            throw new ArgumentException(
                "Installed Battery assessments cannot contain null.",
                nameof(installedBatteryAssessments));
        }

        if (dispatchPassCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dispatchPassCount));
        }

        PowerSystem = powerSystem;
        Regions = new ReadOnlyCollection<RegionalSizingResult>(regions.ToArray());
        InstalledBatteryAssessments = new ReadOnlyCollection<InstalledBatteryAssessment>(
            installedBatteryAssessments.ToArray());
        DispatchPassCount = dispatchPassCount;
        Status = status;
        TerminationEvidence = terminationEvidence;
    }

    public PowerSystem PowerSystem { get; }
    public IReadOnlyList<RegionalSizingResult> Regions { get; }
    public IReadOnlyList<InstalledBatteryAssessment> InstalledBatteryAssessments { get; }
    public int DispatchPassCount { get; }
    public StorageSizingStatus Status { get; }
    public string TerminationEvidence { get; }
}