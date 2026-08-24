namespace NEMSweep.Model.Units;

/// <summary>A physical length in kilometres, such as a transmission line route length.</summary>
public readonly record struct Distance : IComparable<Distance>
{
    /// <summary>The value in kilometres. Unsigned, because a route length cannot be negative.</summary>
    public double Kilometres { get; }

    private Distance(double kilometres) => Kilometres = kilometres;

    /// <summary>Zero distance. Seed for summing a collection of distances.</summary>
    public static Distance Zero { get; } = new(0);

    /// <summary>Creates a <see cref="Distance"/> from a non-negative, finite number of kilometres.</summary>
    public static Distance FromKilometres(double kilometres)
    {
        if (!double.IsFinite(kilometres) || kilometres < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kilometres), kilometres,
                "Distance must be a finite, non-negative number of kilometres.");
        }

        return new Distance(kilometres);
    }

    /// <summary>Sums two distances.</summary>
    public static Distance operator +(Distance a, Distance b) =>
        FromKilometres(a.Kilometres + b.Kilometres);

    /// <summary>Orders this distance against another by kilometres.</summary>
    public int CompareTo(Distance other) => Kilometres.CompareTo(other.Kilometres);
}
