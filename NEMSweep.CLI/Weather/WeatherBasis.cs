using System.Globalization;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Weather;

/// <summary>
/// Describes what a scenario's weather input represents. Scenario runs map an EPW-derived typical
/// meteorological year onto the dispatch period by calendar hour, so every run against this input
/// is a typical-year run, a fact a reader needs stated before treating a storage or reliability
/// result as covering extreme conditions.
/// </summary>
internal static class WeatherBasis
{
    public static WeatherBasisDTO Create(WeatherDataDTO weather)
    {
        ArgumentNullException.ThrowIfNull(weather);
        WeatherSiteDTO solar = CreateSite(weather.Solar.SourceFile, weather.Solar.Location);
        WeatherSiteDTO wind = CreateSite(weather.Wind.SourceFile, weather.Wind.Location);
        return new WeatherBasisDTO(
            WeatherBasisKind.TypicalMeteorologicalYear,
            solar,
            wind,
            CreateDescription(solar, wind));
    }

    private static WeatherSiteDTO CreateSite(string sourceFile, WeatherLocation location) =>
        new(
            sourceFile,
            string.IsNullOrWhiteSpace(location.Wmo)
                ? location.City
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} (WMO {1})",
                    location.City,
                    location.Wmo));

    private static string CreateDescription(WeatherSiteDTO solar, WeatherSiteDTO wind) =>
        solar == wind
            ? string.Format(
                CultureInfo.InvariantCulture,
                "Typical meteorological year using solar and wind data from {0} at {1}, applied to "
                + "the dispatch period by calendar hour. It represents typical rather than extreme "
                + "years, so it does not contain the tail weather events that drive storage and "
                + "reliability outcomes.",
                solar.SourceFile,
                solar.LocationName)
            : string.Format(
                CultureInfo.InvariantCulture,
                "Typical meteorological year using solar data from {0} at {1} and wind data from "
                + "{2} at {3}, applied to the dispatch period by calendar hour. It represents typical "
                + "rather than extreme years, so it does not contain the tail weather events that "
                + "drive storage and reliability outcomes.",
                solar.SourceFile,
                solar.LocationName,
                wind.SourceFile,
                wind.LocationName);
}
