using FluentAssertions;
using NEM.Model.PowerCurves;
using NEM.Model.Series;
using NEM.Model.Units;

namespace NEM.CLI.Tests;

public sealed class WindPowerCurveIntegrationTests
{
    [Fact]
    public void Calculate_ProducesExpectedCapacityFactor_FromParsedEpwWindTrace()
    {
        const double epwWindMeasurementHeightMetres = 10.0;
        const double hubHeightWindSpeedMetresPerSecond = 6.0;
        double measuredWindSpeedMetresPerSecond = hubHeightWindSpeedMetresPerSecond
            / Math.Pow(
                WindPowerCurve.DefaultHubHeightMetres / epwWindMeasurementHeightMetres,
                WindPowerCurve.DefaultShearExponent);
        var fixture = new EpwFixture();
        var timestamp = new DateTime(2025, 1, 1);
        for (int index = 0; index < 8760; index++)
        {
            fixture.AddRow(
                timestamp.Year,
                timestamp.Month,
                timestamp.Day,
                timestamp.Hour + 1,
                windSpeed: measuredWindSpeedMetresPerSecond);
            timestamp = timestamp.AddHours(1);
        }

        string path = fixture.Write();
        try
        {
            EpwWeatherSeries weather = EpwParser.ReadTimeSeries(path);
            Power installedCapacity = Power.FromMegawatts(100.0);

            FlowSeries generation = WindPowerCurve.Calculate(
                weather.WindSpeed,
                installedCapacity);

            Energy maximumEnergy = installedCapacity * TimeSpan.FromHours(8760);
            double capacityFactor = generation.Integrate().MegawattHours
                / maximumEnergy.MegawattHours;
            capacityFactor.Should().BeApproximately(0.1676470588235294, 1e-12);
        }
        finally
        {
            File.Delete(path);
        }
    }
}