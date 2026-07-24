using NEM.Model.Series;
using NEM.Model.Units;

namespace NEM.Model.PowerCurves
{
    /// <summary>
    /// Global tilted irradiation received by a dual-axis tracking array during each
    /// interval, in watt-hours per square metre (Wh/m²).
    /// </summary>
    public sealed class GlobalTiltedIrradiationSeries : TimeSeries
    {
        public const double GroundAlbedo = 0.2;

        private GlobalTiltedIrradiationSeries(
            DateTimeOffset start,
            TimeSpan resolution,
            double[] wattHoursPerSquareMetre)
            : base(start, resolution, wattHoursPerSquareMetre)
        {
        }

        public Irradiation this[int index] =>
            Irradiation.FromWattHoursPerSquareMetre(RawValue(index));

        public static GlobalTiltedIrradiationSeries Calculate(
            TraceSeries globalHorizontalRadiation,
            TraceSeries directNormalRadiation,
            TraceSeries diffuseHorizontalRadiation,
            SolarZenithSeries solarZenith)
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
            ArgumentNullException.ThrowIfNull(solarZenith);

            globalHorizontalRadiation.RequireAligned(directNormalRadiation);
            globalHorizontalRadiation.RequireAligned(diffuseHorizontalRadiation);
            globalHorizontalRadiation.RequireAligned(solarZenith);

            var values = new double[globalHorizontalRadiation.Length];
            for (int index = 0; index < values.Length; index++)
            {
                double globalHorizontal = RequireNonNegative(
                    globalHorizontalRadiation[index], nameof(globalHorizontalRadiation), index);
                double directNormal = RequireNonNegative(
                    directNormalRadiation[index], nameof(directNormalRadiation), index);
                double diffuseHorizontal = RequireNonNegative(
                    diffuseHorizontalRadiation[index], nameof(diffuseHorizontalRadiation), index);
                double zenithDegrees = solarZenith[index].Degrees;

                if (zenithDegrees >= 90.0)
                {
                    values[index] = 0.0;
                    continue;
                }

                double panelTiltRadians = zenithDegrees * Math.PI / 180.0;
                double incidenceAngleRadians = 0.0;
                double skyViewFactor = (1.0 + Math.Cos(panelTiltRadians)) / 2.0;
                double groundViewFactor = (1.0 - Math.Cos(panelTiltRadians)) / 2.0;

                values[index] =
                    directNormal * Math.Cos(incidenceAngleRadians)
                    + diffuseHorizontal * skyViewFactor
                    + globalHorizontal * GroundAlbedo * groundViewFactor;
            }

            return new GlobalTiltedIrradiationSeries(
                globalHorizontalRadiation.Start,
                globalHorizontalRadiation.Resolution,
                values);
        }

        private static void RequireUnit(
            TraceSeries? series,
            TraceUnit expected,
            string paramName)
        {
            ArgumentNullException.ThrowIfNull(series, paramName);
            if (series.Unit != expected)
            {
                throw new ArgumentException(
                    $"Expected {expected}, but received {series.Unit}.",
                    paramName);
            }
        }

        private static double RequireNonNegative(double value, string paramName, int index)
        {
            if (value < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    value,
                    $"Radiation cannot be negative (index {index}).");
            }

            return value;
        }
    }
}