namespace NEM.Model.Generation.Wind
{
    public sealed record WindPowerCurveSettings
    {
        public static WindPowerCurveSettings Default { get; } = new();

        public double HubHeightMetres { get; init; } = WindPowerCurve.DefaultHubHeightMetres;
        public double ShearExponent { get; init; } = WindPowerCurve.DefaultShearExponent;
        public double CutOutWindSpeedMetresPerSecond { get; init; } =
            WindPowerCurve.DefaultCutOutWindSpeedMetresPerSecond;
    }
}