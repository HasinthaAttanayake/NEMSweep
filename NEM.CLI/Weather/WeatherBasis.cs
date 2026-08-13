using System.Globalization;
using NEM.Contracts;

namespace NEM.CLI.Weather;

/// <summary>
/// Describes what a scenario's weather input represents. Scenario runs map an EPW-derived typical
/// meteorological year onto the dispatch period by calendar hour, so every run against this input
/// is a typical-year run — a fact a reader needs stated before treating a storage or reliability
/// result as covering extreme conditions.
/// </summary>
internal static class WeatherBasis
{
    public static WeatherBasisDTO Create(WeatherDataDTO weather)
    {
        ArgumentNullException.ThrowIfNull(weather);
        string locationName = string.IsNullOrWhiteSpace(weather.Solar.Location.Wmo)
            ? weather.Solar.Location.City
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0} (WMO {1})",
                weather.Solar.Location.City,
                weather.Solar.Location.Wmo);
        return new WeatherBasisDTO(
            WeatherBasisKind.TypicalMeteorologicalYear,
            weather.Solar.SourceFile,
            locationName,
            string.Format(
                CultureInfo.InvariantCulture,
                "Typical meteorological year from {0} for {1}, applied to the dispatch period by "
                + "calendar hour. It represents typical rather than extreme years, so it does not "
                + "contain the tail weather events that drive storage and reliability outcomes.",
                weather.Solar.SourceFile,
                locationName));
    }
}
