namespace NEMSweep.Model.Weather
{
    /// <summary>
    /// Unit and physical component of a resource trace. This is a series-level tag;
    /// trace values do not participate in generic arithmetic.
    /// </summary>
    public enum TraceUnit
    {
        /// <summary>Wind speed in metres per second (m/s).</summary>
        MetresPerSecond,

        /// <summary>Direct normal irradiation in watt-hours per square metre (Wh/m²).</summary>
        DirectNormalRadiationWattHoursPerSquareMetre,

        /// <summary>Global horizontal irradiation in watt-hours per square metre (Wh/m²).</summary>
        GlobalHorizontalRadiationWattHoursPerSquareMetre,

        /// <summary>Diffuse horizontal irradiation in watt-hours per square metre (Wh/m²).</summary>
        DiffuseHorizontalRadiationWattHoursPerSquareMetre,

        /// <summary>Dry-bulb air temperature in degrees Celsius (°C).</summary>
        DryBulbTemperatureDegreesCelsius,
    }
}