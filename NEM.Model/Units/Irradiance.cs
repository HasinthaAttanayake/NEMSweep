namespace NEM.Model.Units
{
    /// <summary>Radiant power received per unit area, in W/m².</summary>
    public readonly record struct Irradiance
    {
        /// <summary>The value in watts per square metre. Unsigned.</summary>
        public double WattsPerSquareMetre { get; }

        private Irradiance(double wattsPerSquareMetre) =>
            WattsPerSquareMetre = wattsPerSquareMetre;

        /// <summary>Creates an <see cref="Irradiance"/> from a non-negative, finite value in W/m².</summary>
        public static Irradiance FromWattsPerSquareMetre(double wattsPerSquareMetre)
        {
            if (!double.IsFinite(wattsPerSquareMetre) || wattsPerSquareMetre < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(wattsPerSquareMetre),
                    wattsPerSquareMetre,
                    "Irradiance must be non-negative and finite.");
            }

            return new Irradiance(wattsPerSquareMetre);
        }

        /// <summary>Dimensionless ratio between two irradiances.</summary>
        public static double operator /(Irradiance numerator, Irradiance denominator)
        {
            if (denominator.WattsPerSquareMetre == 0.0)
            {
                throw new DivideByZeroException("Cannot divide irradiance by zero irradiance.");
            }

            return numerator.WattsPerSquareMetre / denominator.WattsPerSquareMetre;
        }
    }
}