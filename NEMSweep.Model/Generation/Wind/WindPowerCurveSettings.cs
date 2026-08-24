namespace NEMSweep.Model.Generation.Wind
{
    /// <summary>
    /// Turbine and site assumptions applied when converting a wind trace into generation. Every
    /// value defaults, so a caller overrides only what it means to change.
    /// </summary>
    public sealed record WindPowerCurveSettings
    {
        /// <summary>The default settings: 120 m hub height, 0.2 shear, 20 m/s cut-out.</summary>
        public static WindPowerCurveSettings Default { get; } = new();

        /// <summary>Turbine hub height in metres. Must be positive.</summary>
        public double HubHeightMetres { get; init; } = WindPowerCurve.DefaultHubHeightMetres;

        /// <summary>
        /// Wind-shear exponent used to extrapolate measured wind speed to hub height. Must not be
        /// negative.
        /// </summary>
        public double ShearExponent { get; init; } = WindPowerCurve.DefaultShearExponent;

        /// <summary>
        /// Speed above which the turbine shuts down and produces nothing. Must be at least
        /// <see cref="WindPowerCurve.MinimumCutOutWindSpeedMetresPerSecond"/>.
        /// </summary>
        public double CutOutWindSpeedMetresPerSecond { get; init; } =
            WindPowerCurve.DefaultCutOutWindSpeedMetresPerSecond;
    }
}