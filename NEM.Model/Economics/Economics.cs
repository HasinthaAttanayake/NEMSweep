using NEM.Model.Units;

namespace NEM.Model.Economics
{
    public static class Economics
    {
        /// <summary>
        /// Capital-recovery factor for an asset with a fixed annual payment:
        /// <c>r(1 + r)^n / ((1 + r)^n - 1)</c>.
        /// </summary>
        /// <param name="rate">Dimensionless annual discount rate; 0.07 represents 7%.</param>
        /// <param name="years">Positive asset life in years.</param>
        /// <returns>The dimensionless annual capital-recovery factor.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="years"/> is zero or <paramref name="rate"/> is at most -1.
        /// </exception>
        public static decimal CapitalRecoveryFactor(decimal rate, uint years)
        {
            ArgumentOutOfRangeException.ThrowIfZero(years);

            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(rate, -1m);

            if (rate == 0m)
            {
                return 1m / years;
            }

            var factor = (decimal)Math.Pow((double)(1m + rate), years);
            var numerator = rate * factor;
            var denominator = factor - 1m;

            return numerator / denominator;
        }

        /// <summary>
        /// Converts one-time capital expenditure into an equivalent annual cost.
        /// </summary>
        /// <param name="capex">One-time capital expenditure in AUD.</param>
        /// <param name="rate">Dimensionless annual discount rate; 0.07 represents 7%.</param>
        /// <param name="years">Positive asset life in years.</param>
        /// <returns>The equivalent annual capital cost in AUD.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="years"/> is zero or <paramref name="rate"/> is at most -1.
        /// </exception>
        public static Money Annuitise(Money capex, decimal rate, uint years)
        {
            return capex * CapitalRecoveryFactor(rate, years);
        }

        /// <summary>
        /// Calculates annual levelised cost of electricity from annualised capital,
        /// fixed operating, and fuel costs divided by annual generation.
        /// </summary>
        /// <param name="totalCapex">One-time capital expenditure in AUD.</param>
        /// <param name="annualFixedOpex">Annual fixed operating cost in AUD.</param>
        /// <param name="annualFuelCost">Annual fuel cost in AUD.</param>
        /// <param name="annualGeneration">Positive annual electricity delivered to load in MWh.</param>
        /// <param name="discountRate">Dimensionless annual discount rate; 0.07 represents 7%.</param>
        /// <param name="assetLifetime">Positive asset life in years.</param>
        /// <returns>The levelised cost in AUD per MWh delivered.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the asset life or discount rate is invalid, or annual generation is not positive.
        /// </exception>
        public static EnergyPrice LevelisedCostOfElectricity(Money totalCapex, Money annualFixedOpex, Money annualFuelCost, Energy annualGeneration, decimal discountRate, uint assetLifetime)
        {
            if (annualGeneration.MegawattHours <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(annualGeneration),
                    "Annual generation must be positive.");
            }

            var annualisedCapex = Annuitise(totalCapex, discountRate, assetLifetime);
            var singleYearLCoE = (annualisedCapex + annualFixedOpex + annualFuelCost).Per(annualGeneration);
            return singleYearLCoE;
        }
    }
}