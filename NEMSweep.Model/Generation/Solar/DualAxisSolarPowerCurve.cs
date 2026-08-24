using NEMSweep.Model.Series;
using NEMSweep.Model.Units;
using NEMSweep.Model.Weather;

namespace NEMSweep.Model.Generation.Solar
{
    /// <summary>
    /// Converts dual-axis global tilted irradiation and dry-bulb temperature into
    /// interval-average AC power for an N-type HJT solar asset.
    /// </summary>
    public static class DualAxisSolarPowerCurve
    {
        /// <summary>
        /// Standard test condition irradiance (1000 W/m²) that a panel's nameplate AC capacity is
        /// rated against. Actual irradiance is compared to this to scale output.
        /// </summary>
        public static Irradiance StandardTestIrradiance { get; } =
            Irradiance.FromWattsPerSquareMetre(1000.0);
        /// <summary>
        /// Fraction of DC output that survives inverter, wiring, and soiling losses to reach AC
        /// terms, before any temperature derating is applied.
        /// </summary>
        public const double SystemFactor = 0.95;
        /// <summary>Assumed rise of cell temperature above ambient dry-bulb temperature, in degrees Celsius.</summary>
        public const double CellTemperatureRiseAboveDryBulbDegreesCelsius = 25.0;
        /// <summary>Reference cell temperature at which the panel's rated output applies, in degrees Celsius.</summary>
        public const double ReferenceCellTemperatureDegreesCelsius = 25.0;
        /// <summary>
        /// Fractional change in output per degree Celsius that cell temperature is above
        /// <see cref="ReferenceCellTemperatureDegreesCelsius"/>. Negative because output falls as
        /// cells heat up.
        /// </summary>
        public const double TemperatureCoefficientPerDegreeCelsius = -0.0027;

        /// <summary>
        /// Calculates dual-axis AC output from raw irradiance components and dry-bulb temperature,
        /// deriving global tilted irradiation internally before applying the power curve.
        /// </summary>
        /// <param name="globalHorizontalRadiation">Global horizontal radiation trace, in Wh/m² per interval.</param>
        /// <param name="directNormalRadiation">Direct normal radiation trace, in Wh/m² per interval.</param>
        /// <param name="diffuseHorizontalRadiation">Diffuse horizontal radiation trace, in Wh/m² per interval.</param>
        /// <param name="dryBulbTemperature">Ambient dry-bulb temperature trace, in degrees Celsius.</param>
        /// <param name="solarZenith">Solar zenith angle series used to orient the tracking array.</param>
        /// <param name="acCapacity">Installed AC nameplate capacity in MW. Must not be negative.</param>
        /// <returns>Interval-average AC power in MW, clamped between zero and <paramref name="acCapacity"/>.</returns>
        public static FlowSeries Calculate(
            TraceSeries globalHorizontalRadiation,
            TraceSeries directNormalRadiation,
            TraceSeries diffuseHorizontalRadiation,
            TraceSeries dryBulbTemperature,
            SolarZenithSeries solarZenith,
            Power acCapacity)
        {
            GlobalTiltedIrradiationSeries globalTiltedIrradiation =
                GlobalTiltedIrradiationSeries.Calculate(
                    globalHorizontalRadiation,
                    directNormalRadiation,
                    diffuseHorizontalRadiation,
                    solarZenith);

            return Calculate(globalTiltedIrradiation, dryBulbTemperature, acCapacity);
        }

        /// <summary>
        /// Calculates dual-axis AC output from already-derived global tilted irradiation and
        /// dry-bulb temperature, applying temperature derating and clamping to nameplate capacity.
        /// </summary>
        /// <param name="globalTiltedIrradiation">Irradiation received by the tracking array per interval.</param>
        /// <param name="dryBulbTemperature">Ambient dry-bulb temperature trace, in degrees Celsius.</param>
        /// <param name="acCapacity">Installed AC nameplate capacity in MW. Must not be negative.</param>
        /// <returns>Interval-average AC power in MW, clamped between zero and <paramref name="acCapacity"/>.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="dryBulbTemperature"/> is not in the expected trace unit, or the two
        /// series are not aligned.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="acCapacity"/> is negative.</exception>
        public static FlowSeries Calculate(
            GlobalTiltedIrradiationSeries globalTiltedIrradiation,
            TraceSeries dryBulbTemperature,
            Power acCapacity)
        {
            ArgumentNullException.ThrowIfNull(globalTiltedIrradiation);
            ArgumentNullException.ThrowIfNull(dryBulbTemperature);

            if (dryBulbTemperature.Unit != TraceUnit.DryBulbTemperatureDegreesCelsius)
            {
                throw new ArgumentException(
                    $"Expected {TraceUnit.DryBulbTemperatureDegreesCelsius}, but received {dryBulbTemperature.Unit}.",
                    nameof(dryBulbTemperature));
            }

            if (acCapacity.Megawatts < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(acCapacity), acCapacity.Megawatts, "AC capacity cannot be negative.");
            }

            globalTiltedIrradiation.RequireAligned(dryBulbTemperature);

            var megawatts = new double[globalTiltedIrradiation.Length];
            for (int index = 0; index < megawatts.Length; index++)
            {
                Irradiance averageIrradiance =
                    globalTiltedIrradiation[index] / globalTiltedIrradiation.Resolution;

                double cellTemperatureDegreesCelsius =
                    dryBulbTemperature[index]
                    + CellTemperatureRiseAboveDryBulbDegreesCelsius;
                double temperatureFactor =
                    1.0
                    + TemperatureCoefficientPerDegreeCelsius
                    * (cellTemperatureDegreesCelsius - ReferenceCellTemperatureDegreesCelsius);
                double lossFactor = SystemFactor * temperatureFactor;

                double irradianceFactor = averageIrradiance / StandardTestIrradiance;
                Power unconstrainedPower = acCapacity * irradianceFactor * lossFactor;
                Power output = Power.Min(
                    Power.Max(unconstrainedPower, Power.Zero),
                    acCapacity);

                megawatts[index] = output.Megawatts;
            }

            return new FlowSeries(
                globalTiltedIrradiation.Start,
                globalTiltedIrradiation.Resolution,
                megawatts);
        }
    }
}