namespace NEMSweep.Model.Units;

/// <summary>
/// Energy in megawatt-hours (MWh).
/// <para>
/// Interval energy (net import/export over a period) is signed, so negatives
/// are allowed here; only NaN and infinity are rejected. Where energy is a
/// stored level (state of charge) it cannot go below zero, but that constraint
/// is enforced where storage state is tracked, not on this scalar.
/// </para>
/// <para>
/// Energy sums across both space and time, so <c>+</c> is defined.
/// </para>
/// </summary>
public readonly record struct Energy : IComparable<Energy>
{
    /// <summary>The value in megawatt-hours. Signed.</summary>
    public double MegawattHours { get; }

    private Energy(double megawattHours) => MegawattHours = megawattHours;

    /// <summary>Zero energy. Seed for summing a collection of energies.</summary>
    public static Energy Zero { get; } = new(0);

    /// <summary>
    /// Creates an <see cref="Energy"/> from a value in megawatt-hours.
    /// </summary>
    public static Energy FromMegawattHours(double megawattHours)
    {
        if (double.IsNaN(megawattHours) || double.IsInfinity(megawattHours))
        {
            throw new ArgumentException(
                "Energy must be a finite number.",
                nameof(megawattHours));
        }

        return new Energy(megawattHours);
    }

    /// <summary>
    /// Energy from average <paramref name="power"/> sustained over
    /// <paramref name="interval"/>: MWh = MW × hours. The interval is required
    /// and must be positive, because a duration cannot be zero or run backwards.
    /// </summary>
    /// <param name="power">Average power over the interval (MW).</param>
    /// <param name="interval">Duration the average applies to; must be positive.</param>
    public static Energy From(Power power, TimeSpan interval)
    {
        RequirePositive(interval, nameof(interval));
        return FromMegawattHours(power.Megawatts * interval.TotalHours);
    }

    /// <summary>Sums two energies. Valid across both space and time.</summary>
    public static Energy operator +(Energy a, Energy b)
        => FromMegawattHours(a.MegawattHours + b.MegawattHours);

    /// <summary>Subtracts one energy from another.</summary>
    public static Energy operator -(Energy a, Energy b)
        => FromMegawattHours(a.MegawattHours - b.MegawattHours);

    /// <summary>Scales an energy by a dimensionless factor.</summary>
    public static Energy operator *(Energy energy, double factor)
        => FromMegawattHours(energy.MegawattHours * factor);

    /// <summary>Scales an energy by a dimensionless factor.</summary>
    public static Energy operator *(double factor, Energy energy)
        => FromMegawattHours(energy.MegawattHours * factor);

    /// <summary>Average power over <paramref name="interval"/>: MW = MWh ÷ hours.</summary>
    public static Power operator /(Energy energy, TimeSpan interval)
    {
        RequirePositive(interval, nameof(interval));
        return Power.FromMegawatts(energy.MegawattHours / interval.TotalHours);
    }

    /// <summary>
    /// Duration this energy would last at <paramref name="power"/> (e.g. storage
    /// duration): hours = MWh ÷ MW. The power rating must be non-negative, and a
    /// zero rating is only valid when the energy is also zero, in which case the
    /// duration is zero.
    /// </summary>
    public static TimeSpan operator /(Energy energy, Power power)
    {
        if (power.Megawatts < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(power), power.Megawatts,
                "A power rating cannot be negative when deriving a duration.");
        }

        if (power.Megawatts == 0)
        {
            if (energy.MegawattHours == 0)
            {
                return TimeSpan.Zero;
            }

            throw new DivideByZeroException(
                "Cannot derive a duration from non-zero energy at zero power.");
        }

        double hours = energy.MegawattHours / power.Megawatts;
        if (hours < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(energy), energy.MegawattHours,
                "A storage duration cannot be negative; energy must be non-negative here.");
        }

        return TimeSpan.FromHours(hours);
    }

    /// <summary>Dimensionless ratio (e.g. renewable share, capacity factor).</summary>
    public static double operator /(Energy numerator, Energy denominator)
    {
        if (denominator.MegawattHours == 0)
        {
            throw new DivideByZeroException("Cannot divide energy by zero energy.");
        }

        return numerator.MegawattHours / denominator.MegawattHours;
    }

    /// <summary>Whether the left energy is less than the right.</summary>
    public static bool operator <(Energy a, Energy b) => a.MegawattHours < b.MegawattHours;

    /// <summary>Whether the left energy is greater than the right.</summary>
    public static bool operator >(Energy a, Energy b) => a.MegawattHours > b.MegawattHours;

    /// <summary>Whether the left energy is less than or equal to the right.</summary>
    public static bool operator <=(Energy a, Energy b) => a.MegawattHours <= b.MegawattHours;

    /// <summary>Whether the left energy is greater than or equal to the right.</summary>
    public static bool operator >=(Energy a, Energy b) => a.MegawattHours >= b.MegawattHours;

    /// <summary>Orders this energy against another by megawatt-hours.</summary>
    public int CompareTo(Energy other) => MegawattHours.CompareTo(other.MegawattHours);

    /// <summary>The lesser of two energies.</summary>
    public static Energy Min(Energy a, Energy b) => a.MegawattHours <= b.MegawattHours ? a : b;

    /// <summary>The greater of two energies.</summary>
    public static Energy Max(Energy a, Energy b) => a.MegawattHours >= b.MegawattHours ? a : b;

    private static void RequirePositive(TimeSpan interval, string paramName)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                paramName, interval, "Interval must be positive.");
        }
    }
}
