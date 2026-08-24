using System.Text.Json.Serialization;

namespace NEMSweep.Contracts;

/// <summary>Schema 6 weather resource artifact with separate solar and wind provenance.</summary>
/// <remarks>
/// Source ownership is intentionally recorded on each role because solar and wind traces may be
/// assembled from different EPW files and locations. The arrays remain positional: index <c>i</c>
/// in every role is the interval at <see cref="Start"/> plus <c>i</c> times <see cref="Resolution"/>.
/// </remarks>
/// <param name="SchemaVersion">Schema version of this artifact.</param>
/// <param name="RegionId">NEM region this weather resource applies to (for example <c>NSW1</c>).</param>
/// <param name="Start">Timestamp of the first interval in every series, in NEM market time (UTC+10).</param>
/// <param name="Resolution">Duration of one interval, shared by every series in this artifact.</param>
/// <param name="Solar">Solar resource and traced generation for this region.</param>
/// <param name="Wind">Wind resource and traced generation for this region.</param>
public sealed record WeatherDataDTO(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("regionId")] string RegionId,
    [property: JsonPropertyName("start")] DateTimeOffset Start,
    [property: JsonPropertyName("resolution")] TimeSpan Resolution,
    [property: JsonPropertyName("solar")] SolarWeatherData Solar,
    [property: JsonPropertyName("wind")] WindWeatherData Wind);

/// <summary>Named site the weather data for one role (solar or wind) was drawn from.</summary>
/// <param name="City">Name of the site's city or locality, as given in the source EPW file.</param>
/// <param name="Wmo">World Meteorological Organization station identifier for the site.</param>
/// <param name="Latitude">Site latitude in decimal degrees.</param>
/// <param name="Longitude">Site longitude in decimal degrees.</param>
public readonly record struct WeatherLocation(
    string City,
    string Wmo,
    double Latitude,
    double Longitude);

/// <summary>
/// Solar resource series for one region, sourced from an EPW file for a typical meteorological
/// year, plus the generation traced from it. Every array is positional against
/// <see cref="WeatherDataDTO.Start"/> and <see cref="WeatherDataDTO.Resolution"/>.
/// </summary>
/// <param name="SourceFile">Name of the EPW file the solar series was read from.</param>
/// <param name="Location">Site the EPW file describes.</param>
/// <param name="GlobalHorizontalRadiationWhPerSquareMetre">
/// Global horizontal irradiation, integrated over the interval, in Wh/m². This is energy per unit
/// area, not an irradiance in W/m².
/// </param>
/// <param name="DirectNormalRadiationWhPerSquareMetre">
/// Direct normal irradiation, integrated over the interval, in Wh/m². This is energy per unit
/// area, not an irradiance in W/m².
/// </param>
/// <param name="DiffuseHorizontalRadiationWhPerSquareMetre">
/// Diffuse horizontal irradiation, integrated over the interval, in Wh/m². This is energy per unit
/// area, not an irradiance in W/m².
/// </param>
/// <param name="SolarZenithDegrees">Solar zenith angle at the interval, in degrees.</param>
/// <param name="DryBulbTemperatureDegreesCelsius">Ambient dry-bulb air temperature for the interval, in degrees Celsius.</param>
/// <param name="ProductionMegawattsAtOneMegawattAc">
/// Modelled solar output for the interval, normalised to a 1 MW AC installation. Multiply by a
/// site's installed AC capacity in MW to get its traced output in MW.
/// </param>
public sealed record SolarWeatherData(
    string SourceFile,
    WeatherLocation Location,
    double[] GlobalHorizontalRadiationWhPerSquareMetre,
    double[] DirectNormalRadiationWhPerSquareMetre,
    double[] DiffuseHorizontalRadiationWhPerSquareMetre,
    double[] SolarZenithDegrees,
    double[] DryBulbTemperatureDegreesCelsius,
    double[] ProductionMegawattsAtOneMegawattAc);

/// <summary>
/// Wind resource series for one region, sourced from an EPW file for a typical meteorological
/// year, plus the generation traced from it. Every array is positional against
/// <see cref="WeatherDataDTO.Start"/> and <see cref="WeatherDataDTO.Resolution"/>.
/// </summary>
/// <param name="SourceFile">Name of the EPW file the wind series was read from.</param>
/// <param name="Location">Site the EPW file describes.</param>
/// <param name="WindSpeedMetresPerSecond">Wind speed for the interval, in m/s, at <see cref="MeasurementHeightMetres"/>.</param>
/// <param name="MeasurementHeightMetres">Height above ground, in metres, that <see cref="WindSpeedMetresPerSecond"/> was measured at.</param>
/// <param name="ProductionMegawattsAtOneMegawattInstalled">
/// Modelled wind output for the interval, normalised to 1 MW of installed capacity. Multiply by a
/// site's installed capacity in MW to get its traced output in MW.
/// </param>
public sealed record WindWeatherData(
    string SourceFile,
    WeatherLocation Location,
    double[] WindSpeedMetresPerSecond,
    double MeasurementHeightMetres,
    double[] ProductionMegawattsAtOneMegawattInstalled);
