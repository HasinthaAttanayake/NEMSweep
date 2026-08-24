namespace NEMSweep.Model.Units;

internal static class DecimalPhysicalBoundary
{
    public static decimal RequireNonNegativeFinite(double value, string paramName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Physical quantity must be finite.", paramName);
        }

        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "Physical quantity cannot be negative.");
        }

        if (value >= (double)decimal.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                "Physical quantity is too large for monetary arithmetic.");
        }

        return (decimal)value;
    }
}