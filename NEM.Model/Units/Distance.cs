namespace NEM.Model.Units;

/// <summary>A physical length in kilometres, such as a transmission line route length.</summary>
public readonly record struct Distance : IComparable<Distance>
{
    public double Kilometres { get; }

    private Distance(double kilometres) => Kilometres = kilometres;

    /// <summary>Zero distance. Seed for summing a collection of distances.</summary>
    public static Distance Zero { get; } = new(0);

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

    public static Distance operator +(Distance a, Distance b) =>
        FromKilometres(a.Kilometres + b.Kilometres);

    public int CompareTo(Distance other) => Kilometres.CompareTo(other.Kilometres);
}
