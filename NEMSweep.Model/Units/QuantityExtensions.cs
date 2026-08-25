namespace NEMSweep.Model.Units;

/// <summary>
/// Aggregation over quantity collections. These types have no LINQ
/// <c>Sum()</c>, so summing seeds from <c>Zero</c>.
/// </summary>
public static class QuantityExtensions
{
    /// <summary>Sums a collection of powers (MW), seeding from <see cref="Power.Zero"/>.</summary>
    public static Power Sum(this IEnumerable<Power> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var total = Power.Zero;
        foreach (var power in source)
        {
            total += power;
        }

        return total;
    }

    /// <summary>Sums a collection of energies (MWh), seeding from <see cref="Energy.Zero"/>.</summary>
    public static Energy Sum(this IEnumerable<Energy> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var total = Energy.Zero;
        foreach (var energy in source)
        {
            total += energy;
        }

        return total;
    }
}
