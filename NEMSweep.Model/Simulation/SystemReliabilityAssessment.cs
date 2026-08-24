namespace NEMSweep.Model.Simulation;

/// <summary>
/// Reliability-target verdict for one regional dispatch outcome.
/// </summary>
public sealed record RegionReliabilityVerdict(
    string RegionId,
    double AchievedUsePercentage,
    bool WithinTarget);

/// <summary>
/// Immutable reliability-target assessment for a whole-system dispatch outcome.
/// The system passes only when its aggregated USE and every regional USE meet the target.
/// </summary>
public sealed record SystemReliabilityAssessment
{
    private SystemReliabilityAssessment(
        double targetUsePercentage,
        double achievedUsePercentage,
        bool withinTarget,
        IReadOnlyList<RegionReliabilityVerdict> regions)
    {
        TargetUsePercentage = targetUsePercentage;
        AchievedUsePercentage = achievedUsePercentage;
        WithinTarget = withinTarget;
        Regions = Array.AsReadOnly(regions.ToArray());
    }

    /// <summary>Maximum unserved energy as a percentage of demand permitted by the target.</summary>
    public double TargetUsePercentage { get; }

    /// <summary>Whole-system unserved energy as a percentage of whole-system demand.</summary>
    public double AchievedUsePercentage { get; }

    /// <summary>Whether the system measurement and every regional verdict meet the target.</summary>
    public bool WithinTarget { get; }

    /// <summary>Reliability-target verdicts for the validated regional dispatch evidence.</summary>
    public IReadOnlyList<RegionReliabilityVerdict> Regions { get; }

    /// <summary>
    /// Assesses a whole-system dispatch outcome against one maximum USE percentage target.
    /// </summary>
    public static SystemReliabilityAssessment Create(
        SystemDispatchOutcome dispatchOutcome,
        double targetUsePercentage)
    {
        ArgumentNullException.ThrowIfNull(dispatchOutcome);
        if (double.IsNaN(targetUsePercentage)
            || double.IsInfinity(targetUsePercentage)
            || targetUsePercentage < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetUsePercentage));
        }

        RegionReliabilityVerdict[] regions = dispatchOutcome.RegionalOutcomes
            .Select(outcome => new RegionReliabilityVerdict(
                outcome.RegionId,
                outcome.Reliability.UnservedEnergyPercentageOfDemand,
                outcome.Reliability.UnservedEnergyPercentageOfDemand <= targetUsePercentage))
            .ToArray();
        double achievedUsePercentage = dispatchOutcome.Reliability.UnservedEnergyPercentageOfDemand;
        bool systemMeasurementWithinTarget = achievedUsePercentage <= targetUsePercentage;

        return new SystemReliabilityAssessment(
            targetUsePercentage,
            achievedUsePercentage,
            systemMeasurementWithinTarget && regions.All(region => region.WithinTarget),
            regions);
    }
}