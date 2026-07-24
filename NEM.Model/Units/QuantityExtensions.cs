namespace NEM.Model.Units
{
    /// <summary>
    /// Aggregation over quantity collections. These types have no LINQ
    /// <c>Sum()</c>, so summing seeds from <c>Zero</c>.
    /// </summary>
    public static class QuantityExtensions
    {
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
}