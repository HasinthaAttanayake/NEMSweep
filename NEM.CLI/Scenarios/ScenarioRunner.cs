using NEM.Contracts;
using NEM.CLI.Configuration;
using NEM.CLI.Demand;
using NEM.CLI.Infrastructure;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.Units;
using NEM.Model.Weather;
using System.Security.Cryptography;
using System.Text.Json;
using DomainScenario = NEM.Model.Scenarios.Scenario;

namespace NEM.CLI.Scenarios;

internal static class ScenarioRunner
{
    private static readonly TimeSpan HourlyResolution = TimeSpan.FromHours(1);

    public static DispatchResultsDTO Run(ScenarioSettings settings, string solutionRoot)
    {
        string demandPath = Path.GetFullPath(settings.DemandFile, solutionRoot);
        string weatherPath = Path.GetFullPath(settings.WeatherFile, solutionRoot);
        LoadedInput<OperationalDemandData> demandInput = ReadDemand(demandPath);
        OperationalDemandData demandData = demandInput.Value;
        FlowSeries hourlyDemand = demandData.Demand.ResampleToHourly();
        LoadedInput<WeatherDataDTO> weatherInput = ReadWeather(weatherPath);
        WeatherDataDTO weatherData = weatherInput.Value;
        RegionalResourceProfile resources = ReadWeatherForTimeline(weatherData, hourlyDemand);
        DomainScenario scenario = BuildScenario(settings, demandData.Region, hourlyDemand);
        PowerSystem powerSystem = ScenarioDerivation.Derive(scenario, hourlyDemand, resources);
        Region region = powerSystem.Regions.Single();
        DispatchOutcome outcome = Dispatcher.Dispatch(region);

        return DispatchResultsExport.Create(
            demandData,
            demandInput.Artifact,
            weatherInput.Artifact,
            scenario,
            powerSystem,
            outcome);
    }

    private static LoadedInput<OperationalDemandData> ReadDemand(string path)
    {
        byte[] contents = File.ReadAllBytes(path);
        ModelInputOutputDTO demand = JsonSerializer.Deserialize<ModelInputOutputDTO>(
            contents,
            JsonFile.ReadOptions)
            ?? throw new FormatException("Demand source JSON is empty.");
        if (demand.SchemaVersion != 2)
        {
            throw new FormatException(
                $"Demand source schema {demand.SchemaVersion} is not supported; expected schema 2.");
        }

        var demandData = new OperationalDemandData(
            demand.Scenario.Region,
            new FlowSeries(
                demand.Scenario.PeriodStart,
                demand.Scenario.Resolution,
                demand.DataSeries.DemandMw),
            demand.DataSources.DemandSourceFiles);
        return new LoadedInput<OperationalDemandData>(
            demandData,
            CreateArtifact(path, demand.SchemaVersion, contents));
    }

    private static LoadedInput<WeatherDataDTO> ReadWeather(string path)
    {
        byte[] contents = File.ReadAllBytes(path);
        WeatherDataDTO weather = JsonSerializer.Deserialize<WeatherDataDTO>(
            contents,
            JsonFile.ReadOptions)
            ?? throw new FormatException("Weather source JSON is empty.");
        if (weather.SchemaVersion != 5)
        {
            throw new FormatException(
                $"Weather source schema {weather.SchemaVersion} is not supported; expected schema 5.");
        }

        return new LoadedInput<WeatherDataDTO>(
            weather,
            CreateArtifact(path, weather.SchemaVersion, contents));
    }

    internal static DispatchInputArtifactDTO CreateArtifact(
        string path,
        int schemaVersion,
        ReadOnlySpan<byte> contents) =>
        new(
            Path.GetFileName(path),
            schemaVersion,
            Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant());

    private static DomainScenario BuildScenario(
        ScenarioSettings settings,
        string regionId,
        FlowSeries timeline)
    {
        if (settings.Fleets.Length == 0)
        {
            throw new FormatException("scenario.fleets must contain at least one fleet.");
        }

        ScenarioFleet[] fleets = settings.Fleets.Select(fleetSettings =>
        {
            if (!Enum.TryParse(fleetSettings.Technology, true, out TechnologyKey technology))
            {
                throw new FormatException(
                    $"Unknown scenario fleet technology '{fleetSettings.Technology}'.");
            }

            IReadOnlyDictionary<DateOnly, double>? monthlyCapacityFactors =
                fleetSettings.MonthlyCapacityFactors?.ToDictionary(
                    entry => entry.Month,
                    entry => entry.CapacityFactor);
            return new ScenarioFleet(
                technology,
                Power.FromMegawatts(fleetSettings.NameplateCapacityMw),
                monthlyCapacityFactors);
        }).ToArray();

        return new DomainScenario(
            new ScenarioId(settings.Id),
            settings.Name,
            regionId,
            timeline.Start,
            timeline.Start.AddTicks(timeline.Resolution.Ticks * timeline.Length),
            fleets);
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

internal sealed record LoadedInput<T>(
    T Value,
    DispatchInputArtifactDTO Artifact);