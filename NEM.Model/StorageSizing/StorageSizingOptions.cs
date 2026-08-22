using NEM.Model.Units;

namespace NEM.Model.StorageSizing;

/// <summary>
/// The bounds and target that govern a storage sizing search: how reliable the system must be, and
/// how much Battery capacity the search may consider before giving up.
/// </summary>
/// <remarks>
/// The maxima are commercial limits supplied by the caller, not physical ones, and they are applied
/// per region rather than to the system total. Reaching one is a reportable outcome
/// (<see cref="StorageSizingStatus.BatteryCapacityLimitReached"/>) rather than a failure, because
/// "no battery this size meets the standard" is itself a result worth publishing.
/// </remarks>
public sealed record StorageSizingOptions
{
    /// <summary>
    /// Default reliability target as a percentage of demand energy: 0.002%, the National
    /// Electricity Rules reliability standard.
    /// </summary>
    public const double DefaultTargetUsePercentage = 0.002;

    /// <summary>
    /// Floor a new or undersized Battery is raised to before growth begins, in MW. Probing upward
    /// from a fleet too small to matter wastes passes on candidates that cannot change the outcome.
    /// </summary>
    public const double MinimumPowerMw = 30;

    /// <summary>
    /// Floor a new or undersized Battery is raised to before growth begins, in MWh. With
    /// <see cref="MinimumPowerMw"/> this sets the four-hour minimum duration every sized candidate
    /// preserves.
    /// </summary>
    public const double MinimumEnergyMwh = 120;

    /// <summary>
    /// Default cap on whole-system dispatch passes. Each pass re-dispatches every region for the
    /// full period, so this bounds the search's wall-clock cost.
    /// </summary>
    public const int DefaultMaximumPasses = 256;

    /// <summary>Validates and creates a set of sizing bounds.</summary>
    /// <param name="maximumPower">
    /// Largest Battery power the search may consider, per region. Must be at least
    /// <see cref="MinimumPowerMw"/>.
    /// </param>
    /// <param name="maximumEnergy">
    /// Largest Battery energy the search may consider, per region. Must be at least
    /// <see cref="MinimumEnergyMwh"/> and must support four hours at
    /// <paramref name="maximumPower"/>.
    /// </param>
    /// <param name="targetUsePercentage">
    /// Maximum unserved energy as a percentage of demand energy. Defaults to
    /// <see cref="DefaultTargetUsePercentage"/>.
    /// </param>
    /// <param name="maximumPasses">Cap on whole-system dispatch passes. Must be positive.</param>
    public StorageSizingOptions(
        Power maximumPower,
        Energy maximumEnergy,
        double targetUsePercentage = DefaultTargetUsePercentage,
        int maximumPasses = DefaultMaximumPasses)
    {
        if (maximumPower < Power.FromMegawatts(MinimumPowerMw))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPower));
        }

        if (maximumEnergy < Energy.FromMegawattHours(MinimumEnergyMwh))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEnergy));
        }

        if (maximumEnergy < maximumPower * TimeSpan.FromHours(4))
        {
            throw new ArgumentException(
                "Maximum energy must support four hours at maximum power.",
                nameof(maximumEnergy));
        }

        if (double.IsNaN(targetUsePercentage)
            || double.IsInfinity(targetUsePercentage)
            || targetUsePercentage < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetUsePercentage));
        }

        if (maximumPasses <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPasses));
        }

        MaximumPower = maximumPower;
        MaximumEnergy = maximumEnergy;
        TargetUsePercentage = targetUsePercentage;
        MaximumPasses = maximumPasses;
    }

    /// <summary>Largest Battery power the search may consider, per region.</summary>
    public Power MaximumPower { get; }

    /// <summary>Largest Battery energy the search may consider, per region.</summary>
    public Energy MaximumEnergy { get; }

    /// <summary>
    /// Maximum unserved energy as a percentage of demand energy. Both the system measurement and
    /// every region must be within it for a run to pass.
    /// </summary>
    public double TargetUsePercentage { get; }

    /// <summary>Cap on whole-system dispatch passes before the search reports exhaustion.</summary>
    public int MaximumPasses { get; }
}