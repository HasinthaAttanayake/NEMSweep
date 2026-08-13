using System.Text.Json.Serialization;

namespace NEM.Contracts;

/// <summary>Schema 6 weather resource artifact with separate solar and wind provenance.</summary>
/// <remarks>
/// Source ownership is intentionally recorded on each role because solar and wind traces may be
/// assembled from different EPW files and locations. The arrays remain positional: index <c>i</c>
/// in every role is the interval at <see cref="Start"/> plus <c>i</c> times <see cref="Resolution"/>.
/// </remarks>
public sealed record WeatherDataDTO(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("regionId")] string RegionId,
    [property: JsonPropertyName("start")] DateTimeOffset Start,
    [property: JsonPropertyName("resolution")] TimeSpan Resolution,
    [property: JsonPropertyName("solar")] SolarWeatherData Solar,
    [property: JsonPropertyName("wind")] WindWeatherData Wind);

public readonly record struct WeatherLocation(
    string City,
    string Wmo,
    double Latitude,
    double Longitude);

public sealed record SolarWeatherData(
    string SourceFile,
    WeatherLocation Location,
    double[] GlobalHorizontalRadiationWhPerSquareMetre,
    double[] DirectNormalRadiationWhPerSquareMetre,
    double[] DiffuseHorizontalRadiationWhPerSquareMetre,
    double[] SolarZenithDegrees,
    double[] DryBulbTemperatureDegreesCelsius,
    double[] ProductionMegawattsAtOneMegawattAc);

public sealed record WindWeatherData(
    string SourceFile,
    WeatherLocation Location,
    double[] WindSpeedMetresPerSecond,
    double MeasurementHeightMetres,
    double[] ProductionMegawattsAtOneMegawattInstalled);