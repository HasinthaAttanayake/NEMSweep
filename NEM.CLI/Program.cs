using System.Text.Json;
using NEM.Contracts;

namespace NEM.CLI
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--generation-information")
            {
                try
                {
                    IReadOnlyList<GenerationInformationRow> rows =
                        GenerationInformationParser.Read(args[1]);
                    GenerationInformationDTO output = GenerationInformationExport.Create(args[1], rows);
                    string outputPath = GetWebDataPath("generation-information.json");
                    GenerationInformationExport.WriteJson(output, outputPath);
                    Console.WriteLine($"Loaded {rows.Count} generation-information rows.");
                    Console.WriteLine($"Wrote generation information to: {Path.GetFullPath(outputPath)}");
                    return 0;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Generation-information import failed: {exception.Message}");
                    return 1;
                }
            }

            if (args.Length == 2 && args[0] == "--epw-report")
            {
                EpwWeatherSeries weather = EpwParser.ReadTimeSeries(args[1]);
                EpwProvenanceReport report = EpwParser.ReadProvenance(args[1]);
                string outputDirectory = Path.GetDirectoryName(GetDefaultOutputPath())!;
                string provenanceOutputPath = Path.Combine(
                    outputDirectory,
                    "weather-provenance.json");
                string weatherDataOutputPath = Path.Combine(outputDirectory, "weather-data.json");
                WeatherDataDTO weatherData = EpwWeatherExport.Create(
                    EpwParser.ReadHeader(args[1]),
                    weather,
                    Path.GetFileName(args[1]));
                EpwProvenance.WriteJson(report, provenanceOutputPath);
                EpwWeatherExport.WriteJson(weatherData, weatherDataOutputPath);
                Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }));
                Console.WriteLine(
                    $"Daylight DNI shares total: {report.DaylightDniSourceShares.Values.Sum():F2}%");
                Console.WriteLine(
                    $"Constructed {weather.GlobalHorizontalRadiation.Length} GHI, " +
                    $"{weather.DirectNormalRadiation.Length} DNI, " +
                    $"{weather.DiffuseHorizontalRadiation.Length} DHI, " +
                    $"{weather.SolarZenith.Length} solar zenith, " +
                    $"{weather.DryBulbTemperature.Length} dry-bulb temperature, and " +
                    $"{weather.WindSpeed.Length} wind values.");
                Console.WriteLine($"Wrote provenance report to: {Path.GetFullPath(provenanceOutputPath)}");
                Console.WriteLine($"Wrote weather data to: {Path.GetFullPath(weatherDataOutputPath)}");
                return 0;
            }

            if (args.Length == 2 && args[0] == "--epw-series")
            {
                EpwWeatherSeries weather = EpwParser.ReadTimeSeries(args[1]);
                Console.WriteLine(
                    $"GHI: {weather.GlobalHorizontalRadiation.Length}; " +
                    $"DNI: {weather.DirectNormalRadiation.Length}; " +
                    $"DHI: {weather.DiffuseHorizontalRadiation.Length}; " +
                    $"Solar zenith: {weather.SolarZenith.Length}; " +
                    $"Dry bulb: {weather.DryBulbTemperature.Length}; " +
                    $"Wind: {weather.WindSpeed.Length} hourly values; " +
                    $"First timestamp: {weather.DirectNormalRadiation.InstantAt(0):o}");
                return 0;
            }

            if (args.Length == 2 && args[0] == "--epw-validate")
            {
                EpwFile epw = EpwParser.ReadValidated(args[1]);
                Console.WriteLine(
                    $"All structural validations passed for {epw.Rows.Count} rows; " +
                    $"source years: {string.Join(", ", epw.Rows.Select(row => row.Year).Distinct().Order())}");
                return 0;
            }

            if (args.Length == 2 && args[0] == "--epw-gaps")
            {
                try
                {
                    EpwFile epw = EpwParser.ReadRows(args[1]);
                    Console.WriteLine($"Rows: {epw.Rows.Count}; Gaps: 0");
                    return 0;
                }
                catch (EpwGapException exception)
                {
                    Console.Error.WriteLine(exception.Message);
                    return 1;
                }
            }

            if (args.Length == 2 && args[0] == "--epw-rows")
            {
                EpwFile epw = EpwParser.ReadRows(args[1]);
                Console.WriteLine($"Rows: {epw.Rows.Count}");
                PrintEpwRow(epw.Rows, 1);
                PrintEpwRow(epw.Rows, 4380);
                PrintEpwRow(epw.Rows, epw.Rows.Count);
                return 0;
            }

            if (args.Length == 2 && args[0] == "--epw-header")
            {
                EpwHeader header = EpwParser.ReadHeader(args[1]);
                Console.WriteLine(
                    $"City: {header.City}; TimeZone: {header.TimeZone}; " +
                    $"RecordsPerHour: {header.RecordsPerHour}; LeapYearObserved: {header.LeapYearObserved}; " +
                    $"DataStartLine: {header.DataStartLineNumber}");
                return 0;
            }

            try
            {
                OperationalDemandSettings settings = ReadOperationalDemandSettings();
                string archiveDirectory = Path.GetFullPath(
                    settings.ArchiveDirectory,
                    AppContext.BaseDirectory);
                OperationalDemandData demandData = OperationalDemandParser.ReadFinancialYear(
                    archiveDirectory,
                    settings.Region,
                    settings.PeriodStart);
                ModelInputOutputDTO output = OperationalDemandExport.Create(demandData);
                string outputPath = args.Length > 0 ? args[0] : GetDefaultOutputPath();
                OperationalDemandExport.WriteJson(output, outputPath);

                Console.WriteLine(
                    $"Loaded {demandData.Demand.Length} half-hour operational-demand intervals "
                    + $"for {demandData.Region} from {demandData.SourceArchives.Count} archives.");
                Console.WriteLine(
                    $"Period: {demandData.Demand.Start:o} to "
                    + $"{output.Scenario.PeriodEnd:o} (end exclusive).");
                Console.WriteLine($"Wrote demand data to: {Path.GetFullPath(outputPath)}");

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Operational-demand import failed: {ex.Message}");
                return 1;
            }
        }

        private static void PrintEpwRow(IReadOnlyList<EpwRow> rows, int rowNumber)
        {
            EpwRow row = rows[rowNumber - 1];
            Console.WriteLine(
                $"Row {rowNumber}: DryBulb={row.DryBulbTemperature}; " +
                $"DNI={row.DirectNormalRadiation}; WindSpeed={row.WindSpeed}");
        }

        static string GetDefaultOutputPath()
        {
            return GetWebDataPath("results.json");
        }

        static string GetWebDataPath(string fileName)
        {
            // Find solution root by looking for NEM.Web project directory
            string currentDir = AppContext.BaseDirectory;
            string solutionRoot = currentDir;

            // Navigate up from bin/Debug/net10.0 to project root, then to solution root
            for (int i = 0; i < 10; i++)
            {
                if (Directory.Exists(Path.Combine(solutionRoot, "NEM.Web")))
                {
                    break;
                }
                string? parent = Directory.GetParent(solutionRoot)?.FullName;
                if (parent == null) break;
                solutionRoot = parent;
            }

            return Path.Combine(solutionRoot, "NEM.Web", "wwwroot", "data", fileName);
        }

        private static OperationalDemandSettings ReadOperationalDemandSettings()
        {
            string settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            JsonElement section = document.RootElement.GetProperty("operationalDemand");
            return new OperationalDemandSettings(
                section.GetProperty("archiveDirectory").GetString()
                    ?? throw new FormatException("operationalDemand.archiveDirectory is required."),
                section.GetProperty("region").GetString()
                    ?? throw new FormatException("operationalDemand.region is required."),
                section.GetProperty("periodStart").GetDateTimeOffset());
        }
    }

    internal sealed record OperationalDemandSettings(
        string ArchiveDirectory,
        string Region,
        DateTimeOffset PeriodStart);
}

