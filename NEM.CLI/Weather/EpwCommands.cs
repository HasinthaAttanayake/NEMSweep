using NEM.CLI.Application;
using NEM.CLI.Infrastructure;
using NEM.Contracts;
using NEM.Model.Weather;

namespace NEM.CLI.Weather;

internal static class EpwCommands
{
    public static int WriteReport(
        CliContext context,
        string regionId,
        string solarSourcePath,
        string? windSourcePath = null)
    {
        windSourcePath ??= solarSourcePath;
        EpwFile solarEpw = EpwParser.ReadValidated(solarSourcePath);
        EpwFile windEpw = string.Equals(solarSourcePath, windSourcePath, StringComparison.OrdinalIgnoreCase)
            ? solarEpw
            : EpwParser.ReadValidated(windSourcePath);
        RegionalResourceProfile solarWeather = EpwParser.ReadTimeSeries(solarEpw);
        RegionalResourceProfile windWeather = ReferenceEquals(solarEpw, windEpw)
            ? solarWeather
            : EpwParser.ReadTimeSeries(windEpw);
        EpwProvenanceReport report = EpwParser.ReadProvenance(solarEpw);
        WeatherDataDTO weatherData = EpwWeatherExport.Create(
            regionId,
            solarEpw.Header,
            solarWeather,
            Path.GetFileName(solarSourcePath),
            windEpw.Header,
            windWeather,
            Path.GetFileName(windSourcePath));
        EpwProvenance.WriteJson(report, context.Paths.WeatherProvenancePath);
        EpwWeatherExport.WriteJson(weatherData, context.Paths.WeatherDataPath(regionId));
        context.Output.WriteLine(JsonFile.SerializeReadable(report));
        context.Output.WriteLine(
            $"Daylight DNI shares total: {report.DaylightDniSourceShares.Values.Sum():F2}%");
        context.Output.WriteLine(
            $"Constructed {solarWeather.GlobalHorizontalRadiation.Length} GHI, "
            + $"{solarWeather.DirectNormalRadiation.Length} DNI, "
            + $"{solarWeather.DiffuseHorizontalRadiation.Length} DHI, "
            + $"{solarWeather.SolarZenith.Length} solar zenith, "
            + $"{solarWeather.DryBulbTemperature.Length} dry-bulb temperature, and "
            + $"{windWeather.WindSpeed.Length} wind values.");
        context.Output.WriteLine(
            $"Wrote provenance report to: {Path.GetFullPath(context.Paths.WeatherProvenancePath)}");
        context.Output.WriteLine(
            $"Wrote weather data to: {Path.GetFullPath(context.Paths.WeatherDataPath(regionId))}");
        return 0;
    }

    public static int PrintSeries(CliContext context, string path)
    {
        RegionalResourceProfile weather = EpwParser.ReadTimeSeries(path);
        context.Output.WriteLine(
            $"GHI: {weather.GlobalHorizontalRadiation.Length}; "
            + $"DNI: {weather.DirectNormalRadiation.Length}; "
            + $"DHI: {weather.DiffuseHorizontalRadiation.Length}; "
            + $"Solar zenith: {weather.SolarZenith.Length}; "
            + $"Dry bulb: {weather.DryBulbTemperature.Length}; "
            + $"Wind: {weather.WindSpeed.Length} hourly values; "
            + $"First timestamp: {weather.DirectNormalRadiation.InstantAt(0):o}");
        return 0;
    }

    public static int Validate(CliContext context, string path)
    {
        EpwFile epw = EpwParser.ReadValidated(path);
        context.Output.WriteLine(
            $"All structural validations passed for {epw.Rows.Count} rows; "
            + $"source years: {string.Join(", ", epw.Rows.Select(row => row.Year).Distinct().Order())}");
        return 0;
    }

    public static int PrintGaps(CliContext context, string path)
    {
        EpwFile epw = EpwParser.ReadRows(path);
        context.Output.WriteLine($"Rows: {epw.Rows.Count}; Gaps: 0");
        return 0;
    }

    public static int PrintRows(CliContext context, string path)
    {
        EpwFile epw = EpwParser.ReadRows(path);
        context.Output.WriteLine($"Rows: {epw.Rows.Count}");
        PrintRow(context.Output, epw.Rows, 1);
        PrintRow(context.Output, epw.Rows, 4380);
        PrintRow(context.Output, epw.Rows, epw.Rows.Count);
        return 0;
    }

    public static int PrintHeader(CliContext context, string path)
    {
        EpwHeader header = EpwParser.ReadHeader(path);
        context.Output.WriteLine(
            $"City: {header.City}; TimeZone: {header.TimeZone}; "
            + $"RecordsPerHour: {header.RecordsPerHour}; LeapYearObserved: {header.LeapYearObserved}; "
            + $"DataStartLine: {header.DataStartLineNumber}");
        return 0;
    }

    private static void PrintRow(TextWriter output, IReadOnlyList<EpwRow> rows, int rowNumber)
    {
        EpwRow row = rows[rowNumber - 1];
        output.WriteLine(
            $"Row {rowNumber}: DryBulb={row.DryBulbTemperature}; "
            + $"DNI={row.DirectNormalRadiation}; WindSpeed={row.WindSpeed}");
    }
}