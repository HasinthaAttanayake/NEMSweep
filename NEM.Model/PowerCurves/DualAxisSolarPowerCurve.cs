using NEM.Model.Series;
using NEM.Model.Units;

namespace NEM.Model.PowerCurves
{
    /// <summary>
    /// Converts dual-axis global tilted irradiation and dry-bulb temperature into
    /// interval-average AC power for an N-type HJT solar asset.
    /// </summary>
    public static class DualAxisSolarPowerCurve
    {
        public const double StandardTestIrradianceWattsPerSquareMetre = 1000.0;
        public const double SystemFactor = 0.95;
        public const double CellTemperatureRiseAboveDryBulbDegreesCelsius = 25.0;
        public const double ReferenceCellTemperatureDegreesCelsius = 25.0;
        public const double TemperatureCoefficientPerDegreeCelsius = -0.0027;

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

            double intervalHours = globalTiltedIrradiation.Resolution.TotalHours;
            var megawatts = new double[globalTiltedIrradiation.Length];
            for (int index = 0; index < megawatts.Length; index++)
            {
                double averageIrradianceWattsPerSquareMetre =
                    globalTiltedIrradiation[index] / intervalHours;

                double cellTemperatureDegreesCelsius =
                    dryBulbTemperature[index]
                    + CellTemperatureRiseAboveDryBulbDegreesCelsius;
                double temperatureFactor =
                    1.0
                    + TemperatureCoefficientPerDegreeCelsius
                    * (cellTemperatureDegreesCelsius - ReferenceCellTemperatureDegreesCelsius);
                double lossFactor = SystemFactor * temperatureFactor;

                double unconstrainedPowerMegawatts =
                    acCapacity.Megawatts
                    / StandardTestIrradianceWattsPerSquareMetre
                    * averageIrradianceWattsPerSquareMetre
                    * lossFactor;

                megawatts[index] = Math.Clamp(
                    unconstrainedPowerMegawatts, 0.0, acCapacity.Megawatts);
            }

            return new FlowSeries(
                globalTiltedIrradiation.Start,
                globalTiltedIrradiation.Resolution,
                megawatts);
        }
    }
}