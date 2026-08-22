namespace NEM.Model.Units
{
    /// <summary>Radiant energy received per unit area during an interval, in Wh/m².</summary>
    public readonly record struct Irradiation
    {
        /// <summary>The value in watt-hours per square metre. Unsigned.</summary>
        public double WattHoursPerSquareMetre { get; }

        private Irradiation(double wattHoursPerSquareMetre) =>
            WattHoursPerSquareMetre = wattHoursPerSquareMetre;

        /// <summary>Creates an <see cref="Irradiation"/> from a non-negative, finite value in Wh/m².</summary>
        public static Irradiation FromWattHoursPerSquareMetre(double wattHoursPerSquareMetre)
        {
            if (!double.IsFinite(wattHoursPerSquareMetre) || wattHoursPerSquareMetre < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(wattHoursPerSquareMetre),
                    wattHoursPerSquareMetre,
                    "Irradiation must be non-negative and finite.");
            }

            return new Irradiation(wattHoursPerSquareMetre);
        }

        /// <summary>Average irradiance over an interval: W/m² = Wh/m² ÷ hours.</summary>
        public static Irradiance operator /(Irradiation irradiation, TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(interval), interval, "Interval must be positive.");
            }

            return Irradiance.FromWattsPerSquareMetre(
                irradiation.WattHoursPerSquareMetre / interval.TotalHours);
        }
    }
}