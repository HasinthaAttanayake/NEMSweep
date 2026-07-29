using NEM.Model.Series;

namespace NEM.Model.Weather
{
    /// <summary>Aligned weather and solar-position traces representing a NEM region.</summary>
    public sealed class RegionalResourceProfile
    {
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

        public TraceSeries GlobalHorizontalRadiation { get; }
        public TraceSeries DirectNormalRadiation { get; }
        public TraceSeries DiffuseHorizontalRadiation { get; }
        public SolarZenithSeries SolarZenith { get; }
        public TraceSeries DryBulbTemperature { get; }
        public TraceSeries WindSpeed { get; }

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