using NEMSweep.Model.Series;
using NEMSweep.Model.Units;
using NEMSweep.Model.Weather;

namespace NEMSweep.Model.Generation.Solar;

/// <summary>
/// Global tilted irradiation received by a dual-axis tracking array during each
/// interval, in watt-hours per square metre (Wh/m²).
/// </summary>
public sealed class GlobalTiltedIrradiationSeries : TimeSeries
{
    /// <summary>
    /// Fraction of ground-reflected irradiance assumed to reach the tilted array. A fixed
    /// generic-ground value rather than a site-specific measurement.
    /// </summary>
    public const double GroundAlbedo = 0.2;

    private GlobalTiltedIrradiationSeries(
        DateTimeOffset start,
        TimeSpan resolution,
        double[] wattHoursPerSquareMetre)
        : base(start, resolution, wattHoursPerSquareMetre)
    {
    }

    /// <summary>Irradiation received during the interval at <paramref name="index"/>.</summary>
    /// <param name="index">Zero-based interval index.</param>
    public Irradiation this[int index] =>
        Irradiation.FromWattHoursPerSquareMetre(RawValue(index));

    /// <summary>
    /// Calculates global tilted irradiation on a dual-axis tracking array (always normal to
    /// the sun) from horizontal and direct-normal radiation components and solar zenith angle.
    /// </summary>
    /// <param name="globalHorizontalRadiation">Global horizontal radiation trace, in Wh/m² per interval.</param>
    /// <param name="directNormalRadiation">Direct normal radiation trace, in Wh/m² per interval.</param>
    /// <param name="diffuseHorizontalRadiation">Diffuse horizontal radiation trace, in Wh/m² per interval.</param>
    /// <param name="solarZenith">Solar zenith angle series; irradiation is zero when the zenith is 90 degrees or more.</param>
    /// <returns>Global tilted irradiation in Wh/m² per interval.</returns>
    /// <exception cref="ArgumentException">
    /// A radiation series is not in its expected trace unit, or the series are not aligned.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A radiation value is negative.</exception>
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
