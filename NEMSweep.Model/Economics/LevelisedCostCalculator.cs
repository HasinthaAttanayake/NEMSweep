using NEMSweep.Model.Units;

namespace NEMSweep.Model.Economics;

/// <summary>
/// Turns a one-off capital cost into the equal annual payment that repays it, at a real discount
/// rate, over an asset's technical life. This is how a single modelled year can carry its share of
/// assets that last decades.
/// </summary>
/// <remarks>
/// The rate is real, not nominal: costs elsewhere in the model are stated in the scenario's
/// real-dollar year, so inflation must not be applied twice.
/// </remarks>
public static class LevelisedCostCalculator
{
    /// <summary>
    /// The capital recovery factor <c>r(1+r)^n / ((1+r)^n - 1)</c>: the fraction of a capital sum
    /// repaid each year by a level annuity over <paramref name="years"/> years.
    /// </summary>
    /// <param name="rate">Real discount rate as a fraction, so 0.07 is 7%. Must be greater than -1.</param>
    /// <param name="years">Asset technical life. Must be positive.</param>
    /// <returns>
    /// The annual fraction of capital recovered. At a zero rate the formula is undefined and the
    /// factor degenerates to straight-line recovery, <c>1/n</c>, which is what is returned.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="years"/> is zero, or <paramref name="rate"/> is -1 or less.
    /// </exception>
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

    /// <summary>
    /// The equivalent annual cost of a capital sum: <paramref name="capex"/> multiplied by
    /// <see cref="CapitalRecoveryFactor"/>.
    /// </summary>
    /// <param name="capex">The one-off capital cost.</param>
    /// <param name="rate">Real discount rate as a fraction, so 0.07 is 7%.</param>
    /// <param name="years">Asset technical life in years.</param>
    /// <returns>The annual charge that repays the capital over its life at that rate.</returns>
    public static Money Annuitise(Money capex, decimal rate, uint years) =>
        capex * CapitalRecoveryFactor(rate, years);
}