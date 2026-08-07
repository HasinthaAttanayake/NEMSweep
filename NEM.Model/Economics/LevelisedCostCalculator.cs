using NEM.Model.Units;

namespace NEM.Model.Economics;

public static class LevelisedCostCalculator
{
    public static decimal CapitalRecoveryFactor(decimal rate, uint years)
    {
        ArgumentOutOfRangeException.ThrowIfZero(years);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(rate, -1m);

        if (rate == 0m)
        {
            return 1m / years;
        }

        decimal factor = (decimal)Math.Pow((double)(1m + rate), years);
        return rate * factor / (factor - 1m);
    }

    public static Money Annuitise(Money capex, decimal rate, uint years) =>
        capex * CapitalRecoveryFactor(rate, years);
}