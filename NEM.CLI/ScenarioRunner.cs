using NEM.Contracts;
using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.Units;
using NEM.Model.Weather;
using System.Text.Json;

namespace NEM.CLI;

internal static class ScenarioRunner
{
    private static readonly TimeSpan HourlyResolution = TimeSpan.FromHours(1);
    private const double EpwWindMeasurementHeightMetres = 10;

    public static DispatchResultsDTO Run(ScenarioRunnerSettings settings, string solutionRoot)
    {
        string demandPath = Path.GetFullPath(settings.DemandFile, solutionRoot);
        string weatherPath = Path.GetFullPath(settings.WeatherFile, solutionRoot);
        OperationalDemandData demandData = ReadDemand(demandPath);
        FlowSeries hourlyDemand = demandData.Demand.ResampleToHourly();
        WeatherDataDTO weatherData = ReadWeather(weatherPath);
        RegionalResourceProfile resources = ReadWeatherForTimeline(weatherData, hourlyDemand);
        GeneratingFleet[] fleets = BuildFleets(settings.Fleets, hourlyDemand);
        var region = new Region(
            demandData.Region,
            fleets,
            hourlyDemand,
            resourceProfile: resources);
        DispatchOutcome outcome = Dispatcher.Dispatch(region);

        return DispatchResultsExport.Create(
            demandData,
            weatherPath,
            settings.Description,
            fleets,
            outcome);
    }

    private static OperationalDemandData ReadDemand(string path)
    {
        ModelInputOutputDTO demand = JsonSerializer.Deserialize<ModelInputOutputDTO>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new FormatException("Demand source JSON is empty.");
        if (demand.SchemaVersion != 2)
        {
            throw new FormatException(
                $"Demand source schema {demand.SchemaVersion} is not supported; expected schema 2.");
        }

        return new OperationalDemandData(
            demand.Scenario.Region,
            new FlowSeries(
                demand.Scenario.PeriodStart,
                demand.Scenario.Resolution,
                demand.DataSeries.DemandMw),
            demand.DataSources.DemandSourceFiles);
    }

    private static WeatherDataDTO ReadWeather(string path)
    {
        WeatherDataDTO weather = JsonSerializer.Deserialize<WeatherDataDTO>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new FormatException("Weather source JSON is empty.");
        if (weather.SchemaVersion != 5)
        {
            throw new FormatException(
                $"Weather source schema {weather.SchemaVersion} is not supported; expected schema 5.");
        }

        return weather;
    }

    private static GeneratingFleet[] BuildFleets(
        IReadOnlyList<FleetScenarioSettings> settings,
        FlowSeries timeline)
    {
        if (settings.Count == 0)
        {
            throw new FormatException("scenario.fleets must contain at least one fleet.");
        }

        return settings.Select(fleetSettings =>
        {
            if (!Enum.TryParse(fleetSettings.Technology, true, out TechnologyKey technology))
            {
                throw new FormatException(
                    $"Unknown scenario fleet technology '{fleetSettings.Technology}'.");
            }

            IReadOnlyDictionary<DateOnly, double>? monthlyCapacityFactors = technology == TechnologyKey.Hydro
                ? BuildMonthlyCapacityFactors(
                    timeline,
                    fleetSettings.MonthlyCapacityFactor
                        ?? throw new FormatException(
                            "Hydro scenario fleets require monthlyCapacityFactor."))
                : null;
            return new GeneratingFleet(
                technology,
                Power.FromMegawatts(fleetSettings.NameplateCapacityMw),
                monthlyCapacityFactors);
        }).ToArray();
    }

    private static IReadOnlyDictionary<DateOnly, double> BuildMonthlyCapacityFactors(
        FlowSeries timeline,
        double capacityFactor)
    {
        DateTimeOffset periodEnd = timeline.Start.AddTicks(timeline.Resolution.Ticks * timeline.Length);
        var month = new DateOnly(timeline.Start.Year, timeline.Start.Month, 1);
        var factors = new Dictionary<DateOnly, double>();
        while (month.ToDateTime(TimeOnly.MinValue) < periodEnd.Date)
        {
            factors.Add(month, capacityFactor);
            month = month.AddMonths(1);
        }

        return factors;
    }

    private static RegionalResourceProfile ReadWeatherForTimeline(
        WeatherDataDTO weather,
        FlowSeries timeline)
    {
        if (weather.Resolution != HourlyResolution)
        {
            throw new FormatException("Scenario weather must use hourly resolution.");
        }

        WeatherSeriesData source = weather.DataSeries;
        int sourceLength = source.GlobalHorizontalRadiationWhPerSquareMetre.Length;
        double[][] sourceSeries =
        [
            source.DirectNormalRadiationWhPerSquareMetre,
            source.DiffuseHorizontalRadiationWhPerSquareMetre,
            source.DryBulbTemperatureDegreesCelsius,
            source.WindSpeedMetresPerSecond,
        ];
        if (sourceLength == 0 || sourceSeries.Any(values => values.Length != sourceLength))
        {
            throw new FormatException("Weather source series are empty or misaligned.");
        }

        var indexesByCalendarHour = Enumerable.Range(0, sourceLength).ToDictionary(index =>
        {
            DateTimeOffset instant = weather.Start.AddTicks(weather.Resolution.Ticks * index);
            return (instant.Month, instant.Day, instant.Hour);
        });
        var globalHorizontalRadiation = new double[timeline.Length];
        var directNormalRadiation = new double[timeline.Length];
        var diffuseHorizontalRadiation = new double[timeline.Length];
        var dryBulbTemperature = new double[timeline.Length];
        var windSpeed = new double[timeline.Length];

        for (int index = 0; index < timeline.Length; index++)
        {
            DateTimeOffset instant = timeline.InstantAt(index);
            if (!indexesByCalendarHour.TryGetValue(
                (instant.Month, instant.Day, instant.Hour),
                out int sourceIndex))
            {
                throw new InvalidOperationException(
                    $"Weather source has no typical-year value for {instant:MM-dd HH}:00.");
            }

            globalHorizontalRadiation[index] = source.GlobalHorizontalRadiationWhPerSquareMetre[sourceIndex];
            directNormalRadiation[index] = source.DirectNormalRadiationWhPerSquareMetre[sourceIndex];
            diffuseHorizontalRadiation[index] = source.DiffuseHorizontalRadiationWhPerSquareMetre[sourceIndex];
            dryBulbTemperature[index] = source.DryBulbTemperatureDegreesCelsius[sourceIndex];
            windSpeed[index] = source.WindSpeedMetresPerSecond[sourceIndex];
        }

        return new RegionalResourceProfile(
            TraceSeries.GlobalHorizontalRadiation(
                timeline.Start, HourlyResolution, globalHorizontalRadiation),
            TraceSeries.DirectNormalRadiation(
                timeline.Start, HourlyResolution, directNormalRadiation),
            TraceSeries.DiffuseHorizontalRadiation(
                timeline.Start, HourlyResolution, diffuseHorizontalRadiation),
            SolarZenithSeries.Calculate(
                timeline.Start,
                HourlyResolution,
                timeline.Length,
                weather.Location.Latitude,
                weather.Location.Longitude),
            TraceSeries.DryBulbTemperature(
                timeline.Start, HourlyResolution, dryBulbTemperature),
            TraceSeries.WindSpeed(
                timeline.Start,
                HourlyResolution,
                windSpeed,
                weather.WindMeasurementHeightMetres));
    }
}

internal sealed record ScenarioRunnerSettings(
    string DemandFile,
    string WeatherFile,
    string Description,
    FleetScenarioSettings[] Fleets);

internal sealed record FleetScenarioSettings(
    string Technology,
    double NameplateCapacityMw,
    double? MonthlyCapacityFactor = null);