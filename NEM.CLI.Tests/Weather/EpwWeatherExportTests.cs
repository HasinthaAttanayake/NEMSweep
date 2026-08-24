using AwesomeAssertions;
using NEM.CLI.Weather;
using NEM.Contracts;
using NEM.Model.Generation.Wind;
using NEM.Model.Series;
using NEM.Model.Units;
using NEM.Model.Weather;
using System.Text.Json;

namespace NEM.CLI.Tests.Weather;

public sealed class EpwWeatherExportTests
{
    [Fact]
    public void Create_RoundTripsSeparateSolarAndWindRoles()
    {
        EpwHeader solarHeader = Header("Solar site", -33.86, 151.21);
        EpwHeader windHeader = Header("Wind site", -32.1, 148.2);
        RegionalResourceProfile solar = Profile(8760, solarHeader.Latitude, solarHeader.Longitude);
        RegionalResourceProfile wind = Profile(8760, windHeader.Latitude, windHeader.Longitude);

        WeatherDataDTO export = EpwWeatherExport.Create(
            "NSW1", solarHeader, solar, "solar.epw", windHeader, wind, "wind.epw");
        WeatherDataDTO roundTripped = JsonSerializer.Deserialize<WeatherDataDTO>(
            JsonSerializer.Serialize(export))!;

        roundTripped.SchemaVersion.Should().Be(6);
        roundTripped.RegionId.Should().Be("NSW1");
        roundTripped.Solar.SourceFile.Should().Be("solar.epw");
        roundTripped.Solar.Location.Latitude.Should().Be(solarHeader.Latitude);
        roundTripped.Solar.SolarZenithDegrees.Should().Equal(
            Enumerable.Range(0, solar.SolarZenith.Length)
                .Select(index => solar.SolarZenith[index].Degrees));
        roundTripped.Solar.ProductionMegawattsAtOneMegawattAc.Should().HaveCount(8760);
        roundTripped.Wind.SourceFile.Should().Be("wind.epw");
        roundTripped.Wind.Location.Latitude.Should().Be(windHeader.Latitude);
        roundTripped.Wind.WindSpeedMetresPerSecond.Should().Equal(Enumerable.Repeat(5d, 8760));
        roundTripped.Wind.MeasurementHeightMetres.Should().Be(10);
        roundTripped.Wind.ProductionMegawattsAtOneMegawattInstalled.Should().HaveCount(8760);
        JsonSerializer.Serialize(roundTripped.Wind).Should().NotContain("Temperature");
    }

    [Fact]
    public void Create_AllowsOneSourceForBothRoles()
    {
        EpwHeader header = Header("Sydney", -33.86, 151.21);
        RegionalResourceProfile profile = Profile(8760, header.Latitude, header.Longitude);

        WeatherDataDTO export = EpwWeatherExport.Create(
            "NSW1", header, profile, "sydney.epw", header, profile, "sydney.epw");

        export.Solar.SourceFile.Should().Be(export.Wind.SourceFile);
        export.Solar.Location.Should().Be(export.Wind.Location);
    }

    [Fact]
    public void Create_RejectsMismatchedLengthWithRegionAndSources()
    {
        EpwHeader header = Header("Sydney", -33.86, 151.21);
        RegionalResourceProfile solar = Profile(8760, header.Latitude, header.Longitude);
        RegionalResourceProfile wind = Profile(8784, header.Latitude, header.Longitude);

        var act = () => EpwWeatherExport.Create(
            "NSW1", header, solar, "solar.epw", header, wind, "wind.epw");

        act.Should().Throw<FormatException>()
            .Which.Message.Should().Contain("NSW1")
            .And.Contain("solar.epw")
            .And.Contain("wind.epw");
    }

    private static EpwHeader Header(string city, double latitude, double longitude) => new(
        city, "947680", latitude, longitude, 10, false, 1, 1, 9);

    private static RegionalResourceProfile Profile(int length, double latitude, double longitude)
    {
        DateTimeOffset start = EpwParser.SyntheticNonLeapStart;
        TimeSpan resolution = TimeSpan.FromHours(1);
        return new RegionalResourceProfile(
            TraceSeries.GlobalHorizontalRadiation(start, resolution, Enumerable.Repeat(500d, length).ToArray()),
            TraceSeries.DirectNormalRadiation(start, resolution, Enumerable.Repeat(420d, length).ToArray()),
            TraceSeries.DiffuseHorizontalRadiation(start, resolution, Enumerable.Repeat(80d, length).ToArray()),
            SolarZenithSeries.Calculate(start, resolution, length, latitude, longitude),
            TraceSeries.DryBulbTemperature(start, resolution, Enumerable.Repeat(20d, length).ToArray()),
            TraceSeries.WindSpeed(start, resolution, Enumerable.Repeat(5d, length).ToArray(), 10));
    }
}