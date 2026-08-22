using NEM.Model.Series;
using NEM.Model.Units;

namespace NEM.Model.Weather
{
    /// <summary>
    /// A resource trace: wind speed (m/s) or EPW radiation (Wh/m²). Traces are inputs
    /// to conversion, not participants in arithmetic — read at index <c>t</c>, feed to a
    /// conversion, get a <see cref="Power"/>. They carry no flow/stock category because
    /// the split does not apply: integrating wind speed yields metres of air, and the
    /// EPW radiation field is already integrated over the hour.
    /// <para>
    /// Two guarantees are encoded in the unit tag and carried height:
    /// the Wh/m² tag prevents multiplying an already-integrated field by Δt "to get
    /// energy"; and a carried measurement height makes the hub-height correction a
    /// visible one-way transition, so it cannot be applied twice silently.
    /// </para>
    /// </summary>
    public sealed class TraceSeries : TimeSeries
    {
        private TraceSeries(
            DateTimeOffset start,
            TimeSpan resolution,
            double[] values,
            TraceUnit unit,
            double? measurementHeightMetres)
            : base(start, resolution, values)
        {
            Unit = unit;
            MeasurementHeightMetres = measurementHeightMetres;
        }

        /// <summary>The unit these trace values are expressed in.</summary>
        public TraceUnit Unit { get; }

        /// <summary>
        /// Measurement height in metres — present for wind speed (the hub-height
        /// correction depends on it), absent otherwise.
        /// </summary>
        public double? MeasurementHeightMetres { get; }

        /// <summary>Raw trace value at <paramref name="index"/>, in this series' <see cref="Unit"/>.</summary>
        public double this[int index] => RawValue(index);

        /// <summary>
        /// A wind-speed trace (m/s) at a stated measurement height. The height is
        /// required and must be positive — it is needed for the hub-height correction.
        /// </summary>
        public static TraceSeries WindSpeed(
            DateTimeOffset start,
            TimeSpan resolution,
            double[] metresPerSecond,
            double measurementHeightMetres)
        {
            ArgumentNullException.ThrowIfNull(metresPerSecond);

            if (double.IsNaN(measurementHeightMetres)
                || double.IsInfinity(measurementHeightMetres)
                || measurementHeightMetres <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(measurementHeightMetres),
                    measurementHeightMetres,
                    "Wind-speed traces require a positive measurement height.");
            }

            for (int index = 0; index < metresPerSecond.Length; index++)
            {
                if (metresPerSecond[index] < 0.0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(metresPerSecond),
                        metresPerSecond[index],
                        $"Wind speed cannot be negative (index {index}).");
                }
            }

            return new TraceSeries(
                start, resolution, metresPerSecond, TraceUnit.MetresPerSecond, measurementHeightMetres);
        }

        /// <summary>
        /// A Direct Normal Radiation trace (Wh/m²), already integrated
        /// over the hour. No measurement height applies. Named for the component, not the
        /// bare unit, so a Global Horizontal or Diffuse Horizontal field cannot be passed
        /// where Direct Normal is required.
        /// </summary>
        public static TraceSeries DirectNormalRadiation(
            DateTimeOffset start,
            TimeSpan resolution,
            double[] wattHoursPerSquareMetre)
        {
            return new TraceSeries(
                start, resolution, wattHoursPerSquareMetre, TraceUnit.DirectNormalRadiationWattHoursPerSquareMetre, null);
        }

        /// <summary>
        /// A Global Horizontal Radiation trace (Wh/m²), already integrated
        /// over the hour. No measurement height applies. Named for the component, not the
        /// bare unit, so a Direct Normal or Diffuse Horizontal field cannot be passed
        /// where Global Horizontal is required.
        /// </summary>
        public static TraceSeries GlobalHorizontalRadiation(
            DateTimeOffset start,
            TimeSpan resolution,
            double[] wattHoursPerSquareMetre)
        {
            return new TraceSeries(
                start, resolution, wattHoursPerSquareMetre, TraceUnit.GlobalHorizontalRadiationWattHoursPerSquareMetre, null);
        }

        /// <summary>
        /// A Diffuse Horizontal Radiation trace (Wh/m²), already integrated
        /// over the hour. No measurement height applies. Named for the component, not the
        /// bare unit, so a Global Horizontal or Direct Normal field cannot be passed
        /// where Diffuse Horizontal is required.
        /// </summary>
        public static TraceSeries DiffuseHorizontalRadiation(
            DateTimeOffset start,
            TimeSpan resolution,
            double[] wattHoursPerSquareMetre)
        {
            return new TraceSeries(
                start, resolution, wattHoursPerSquareMetre, TraceUnit.DiffuseHorizontalRadiationWattHoursPerSquareMetre, null);
        }

        /// <summary>A dry-bulb temperature trace (degrees Celsius). No measurement height applies.</summary>
        public static TraceSeries DryBulbTemperature(
            DateTimeOffset start,
            TimeSpan resolution,
            double[] degreesCelsius)
        {
            return new TraceSeries(
                start, resolution, degreesCelsius, TraceUnit.DryBulbTemperatureDegreesCelsius, null);
        }

    }
}
