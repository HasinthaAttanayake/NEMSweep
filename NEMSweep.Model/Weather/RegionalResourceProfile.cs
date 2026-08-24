using NEMSweep.Model.Series;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Weather
{
    /// <summary>Aligned weather and solar-position traces representing a NEM region.</summary>
    public sealed class RegionalResourceProfile
    {
        /// <summary>
        /// Creates a <see cref="RegionalResourceProfile"/> from six traces that must all share
        /// the same start, resolution, and length. Each trace must carry the unit its parameter
        /// name implies, and <paramref name="solarZenith"/> is required (not null).
        /// </summary>
        /// <param name="globalHorizontalRadiation">Global horizontal radiation trace (Wh/m²).</param>
        /// <param name="directNormalRadiation">Direct normal radiation trace (Wh/m²).</param>
        /// <param name="diffuseHorizontalRadiation">Diffuse horizontal radiation trace (Wh/m²).</param>
        /// <param name="solarZenith">Geometric solar zenith angles for the region.</param>
        /// <param name="dryBulbTemperature">Dry-bulb temperature trace (degrees Celsius).</param>
        /// <param name="windSpeed">Wind speed trace (m/s).</param>
        public RegionalResourceProfile(
            TraceSeries globalHorizontalRadiation,
            TraceSeries directNormalRadiation,
            TraceSeries diffuseHorizontalRadiation,
            SolarZenithSeries solarZenith,
            TraceSeries dryBulbTemperature,
            TraceSeries windSpeed)
        {
            RequireUnit(
                globalHorizontalRadiation,
                TraceUnit.GlobalHorizontalRadiationWattHoursPerSquareMetre,
                nameof(globalHorizontalRadiation));
            RequireUnit(
                directNormalRadiation,
                TraceUnit.DirectNormalRadiationWattHoursPerSquareMetre,
                nameof(directNormalRadiation));
            RequireUnit(
                diffuseHorizontalRadiation,
                TraceUnit.DiffuseHorizontalRadiationWattHoursPerSquareMetre,
                nameof(diffuseHorizontalRadiation));
            RequireUnit(
                dryBulbTemperature,
                TraceUnit.DryBulbTemperatureDegreesCelsius,
                nameof(dryBulbTemperature));
            RequireUnit(windSpeed, TraceUnit.MetresPerSecond, nameof(windSpeed));
            ArgumentNullException.ThrowIfNull(solarZenith);

            globalHorizontalRadiation.RequireAligned(directNormalRadiation);
            globalHorizontalRadiation.RequireAligned(diffuseHorizontalRadiation);
            globalHorizontalRadiation.RequireAligned(solarZenith);
            globalHorizontalRadiation.RequireAligned(dryBulbTemperature);
            globalHorizontalRadiation.RequireAligned(windSpeed);

            GlobalHorizontalRadiation = globalHorizontalRadiation;
            DirectNormalRadiation = directNormalRadiation;
            DiffuseHorizontalRadiation = diffuseHorizontalRadiation;
            SolarZenith = solarZenith;
            DryBulbTemperature = dryBulbTemperature;
            WindSpeed = windSpeed;
        }

        /// <summary>Global horizontal radiation trace (Wh/m²).</summary>
        public TraceSeries GlobalHorizontalRadiation { get; }

        /// <summary>Direct normal radiation trace (Wh/m²).</summary>
        public TraceSeries DirectNormalRadiation { get; }

        /// <summary>Diffuse horizontal radiation trace (Wh/m²).</summary>
        public TraceSeries DiffuseHorizontalRadiation { get; }

        /// <summary>Geometric solar zenith angles for the region, aligned with the other traces.</summary>
        public SolarZenithSeries SolarZenith { get; }

        /// <summary>Dry-bulb temperature trace (degrees Celsius).</summary>
        public TraceSeries DryBulbTemperature { get; }

        /// <summary>Wind speed trace (m/s).</summary>
        public TraceSeries WindSpeed { get; }

        /// <summary>
        /// The region's weather site, taken from <see cref="SolarZenith"/>. Used as the region's
        /// representative point for deriving transmission line distance between regions.
        /// </summary>
        public GeoCoordinate Location =>
            GeoCoordinate.FromDegrees(SolarZenith.Latitude, SolarZenith.Longitude);

        internal void RequireAligned(TimeSeries timeline) =>
            GlobalHorizontalRadiation.RequireAligned(timeline);

        private static void RequireUnit(
            TraceSeries trace,
            TraceUnit expectedUnit,
            string parameterName)
        {
            ArgumentNullException.ThrowIfNull(trace, parameterName);
            if (trace.Unit != expectedUnit)
            {
                throw new ArgumentException(
                    $"Expected {expectedUnit}, but received {trace.Unit}.",
                    parameterName);
            }
        }
    }
}