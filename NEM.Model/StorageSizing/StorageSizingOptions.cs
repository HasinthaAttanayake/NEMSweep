using NEM.Model.Units;

namespace NEM.Model.StorageSizing;

public sealed record StorageSizingOptions
{
    public const double DefaultTargetUsePercentage = 0.002;
    public const double MinimumPowerMw = 30;
    public const double MinimumEnergyMwh = 120;
    public const int DefaultMaximumPasses = 256;

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

    public Power MaximumPower { get; }
    public Energy MaximumEnergy { get; }
    public double TargetUsePercentage { get; }
    public int MaximumPasses { get; }
}