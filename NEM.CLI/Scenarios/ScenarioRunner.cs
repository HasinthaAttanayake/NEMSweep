using NEM.Contracts;
using NEM.CLI.Configuration;
using NEM.CLI.Demand;
using NEM.CLI.Infrastructure;
using NEM.CLI.Weather;
using NEM.Model.Economics;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.StorageSizing;
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
        LoadedInput<OperationalDemandData> demandInput = ReadInput(() => ReadDemand(demandPath));
        OperationalDemandData demandData = demandInput.Value;
        FlowSeries hourlyDemand = demandData.Demand.ResampleToHourly();
        LoadedInput<WeatherDataDTO> weatherInput = ReadInput(() => ReadWeather(weatherPath));
        WeatherDataDTO weatherData = weatherInput.Value;
        RegionalResourceProfile resources = ReadInput(
            () => ReadWeatherForTimeline(weatherData, hourlyDemand));
        DomainScenario scenario = BuildScenario(settings, hourlyDemand);
        if (scenario.Regions.Count != 1
            || !string.Equals(
                scenario.Regions[0].RegionId,
                demandData.Region,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ScenarioRunException(
                SweepFailureStage.Input,
                "unsupportedRegionCount",
                "The current scenario runner requires exactly one scenario region matching its demand input.");
        }

        IReadOnlyDictionary<string, IReadOnlyList<DemandComponent>>? additiveDemandComponents =
            CreateDataCentreDemandComponents(settings, scenario);

        PowerSystem powerSystem = ScenarioDerivation.Derive(
            scenario,
            new Dictionary<string, FlowSeries>(StringComparer.OrdinalIgnoreCase)
            {
                [demandData.Region] = hourlyDemand,
            },
            new Dictionary<string, RegionalResourceProfile?>(StringComparer.OrdinalIgnoreCase)
            {
                [demandData.Region] = resources,
            },
            additiveDemandComponents);
        StorageSizingSettings sizing = settings.StorageSizing;
        var sizingOptions = new StorageSizingOptions(
            Power.FromMegawatts(sizing.MaximumPowerMw),
            Energy.FromMegawattHours(sizing.MaximumEnergyMwh),
            sizing.TargetUsePercentage,
            sizing.MaximumPasses);
        StorageSizingRunResult sizingResult = Size(powerSystem, sizingOptions);
        PowerSystemCostBreakdown costBreakdown = Cost(scenario, sizingResult);

        return DispatchResultsExport.Create(new DispatchExportRequest(
            demandData,
            demandInput.Artifact,
            weatherInput.Artifact,
            WeatherBasis.Create(weatherData),
            scenario,
            sizingResult,
            sizingOptions,
            sizing.ReliabilityStandardName,
            costBreakdown));
    }

    private static StorageSizingRunResult Size(
        PowerSystem powerSystem,
        StorageSizingOptions options)
    {
        StorageSizingRunResult result;
        try
        {
            result = StorageSizingService.Size(powerSystem, options);
        }
        catch (Exception exception) when (exception is not ScenarioRunException)
        {
            throw new ScenarioRunException(
                SweepFailureStage.Dispatch,
                "dispatchFailed",
                exception.Message,
                exception);
        }

        return result.Status == StorageSizingStatus.TargetMet
            ? result
            : throw new ScenarioRunException(
                SweepFailureStage.Sizing,
                JsonNamingPolicy.CamelCase.ConvertName(result.Status.ToString()),
                $"Storage sizing ended with {result.Status}: {result.TerminationEvidence}");
    }

    private static PowerSystemCostBreakdown Cost(
        DomainScenario scenario,
        StorageSizingRunResult sizingResult)
    {
        try
        {
            return PowerSystemCostCalculator.Calculate(scenario, sizingResult);
        }
        catch (Exception exception) when (exception is not ScenarioRunException)
        {
            throw new ScenarioRunException(
                SweepFailureStage.Costing,
                "costingFailed",
                exception.Message,
                exception);
        }
    }

    /// <summary>Runs an input read, attributing any failure to the input stage.</summary>
    private static T ReadInput<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception
            is FormatException or IOException or JsonException or InvalidOperationException)
        {
            throw new ScenarioRunException(
                SweepFailureStage.Input,
                exception is IOException ? "inputUnreadable" : "invalidInput",
                exception.Message,
                exception);
        }
    }

    private static LoadedInput<OperationalDemandData> ReadDemand(string path)
    {
        byte[] contents = File.ReadAllBytes(path);
        ModelInputOutputDTO demand = JsonSerializer.Deserialize<ModelInputOutputDTO>(
            contents,
            JsonFile.ReadOptions)
            ?? throw new FormatException("Demand source JSON is empty.");
        if (demand.SchemaVersion != ArtifactSchemaVersions.OperationalDemand)
        {
            throw new FormatException(
                $"Demand source schema {demand.SchemaVersion} is not supported; expected schema "
                + $"{ArtifactSchemaVersions.OperationalDemand}.");
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
        if (weather.SchemaVersion != ArtifactSchemaVersions.Weather)
        {
            throw new FormatException(
                $"Weather source schema {weather.SchemaVersion} is not supported; expected schema "
                + $"{ArtifactSchemaVersions.Weather}.");
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
        FlowSeries timeline)
    {
        ScenarioRegion[] regions = settings.Regions.Select(regionSettings =>
        {
            ScenarioGeneratingFleet[] generatingFleets = regionSettings.GeneratingFleets.Select(
                generatingFleetSettings => CreateGeneratingFleet(generatingFleetSettings)).ToArray();
            ScenarioStorageFleet[] storageFleets = regionSettings.StorageFleets.Select(
                storageFleetSettings => CreateStorageFleet(storageFleetSettings)).ToArray();
            return new ScenarioRegion(regionSettings.RegionId, generatingFleets, storageFleets);
        }).ToArray();

        return new DomainScenario(
            new ScenarioId(settings.Id),
            settings.Name,
            timeline.Start,
            timeline.Start.AddTicks(timeline.Resolution.Ticks * timeline.Length),
            regions,
            new CostBasis(settings.CostBasis.Year, settings.CostBasis.RealDiscountRate));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<DemandComponent>>?
        CreateDataCentreDemandComponents(ScenarioSettings settings, DomainScenario scenario)
    {
        if (settings.DataCentreNameplateMw == 0)
        {
            return null;
        }

        int intervalCount = checked((int)((scenario.PeriodEnd - scenario.PeriodStart).Ticks
            / HourlyResolution.Ticks));
        FlowSeries demand = DataCentreDemand.Expand(
            Power.FromMegawatts(settings.DataCentreNameplateMw),
            scenario.PeriodStart,
            HourlyResolution,
            intervalCount);
        return new Dictionary<string, IReadOnlyList<DemandComponent>>(StringComparer.OrdinalIgnoreCase)
        {
            [scenario.Regions.Single().RegionId] = [new DemandComponent("Data centre", demand)],
        };
    }

    private static ScenarioGeneratingFleet CreateGeneratingFleet(
        GeneratingFleetSettings generatingFleetSettings)
    {
        if (!Enum.TryParse(
            generatingFleetSettings.Technology,
            true,
            out GenerationTechnology technology))
        {
            throw new FormatException(
                $"Unknown scenario generating fleet technology "
                + $"'{generatingFleetSettings.Technology}'.");
        }

        IReadOnlyDictionary<DateOnly, double>? monthlyCapacityFactors =
            generatingFleetSettings.MonthlyCapacityFactors?.ToDictionary(
                entry => entry.Month,
                entry => entry.CapacityFactor);
        return new ScenarioGeneratingFleet(
            technology,
            Power.FromMegawatts(generatingFleetSettings.NameplateCapacityMw),
            new GenerationCostParameters(
                PowerCapacityCost.FromAudPerMwCapacity(
                    generatingFleetSettings.CostParameters.CapitalCostAudPerMw),
                AnnualPowerCapacityCost.FromAudPerMwYear(
                    generatingFleetSettings.CostParameters.FixedOperatingCostAudPerMwYear),
                GenerationEnergyCost.FromAudPerMwhGenerated(
                    generatingFleetSettings.CostParameters.VariableOperatingCostAudPerMwh),
                FuelPrice.FromAudPerGjThermal(
                    generatingFleetSettings.CostParameters.FuelPriceAudPerGj)),
            new GenerationTechnologyProfile(
                HeatRate.FromGigajoulesPerMegawattHour(
                    generatingFleetSettings.TechnologyProfile.HeatRateGjPerMwh),
                generatingFleetSettings.TechnologyProfile.TechnicalLifeYears),
            monthlyCapacityFactors);
    }

    private static ScenarioStorageFleet CreateStorageFleet(
        StorageFleetSettings storageFleetSettings)
    {
        if (!Enum.TryParse(
            storageFleetSettings.Technology,
            true,
            out StorageTechnology technology))
        {
            throw new FormatException(
                $"Unknown scenario storage fleet technology '{storageFleetSettings.Technology}'.");
        }

        StorageCostParametersSettings costs = storageFleetSettings.CostParameters;
        return new ScenarioStorageFleet(
            technology,
            Energy.FromMegawattHours(storageFleetSettings.InitialEnergyCapacityMwh),
            Power.FromMegawatts(storageFleetSettings.InitialPowerCapacityMw),
            new StorageCostParameters(
                PowerCapacityCost.FromAudPerMwCapacity(costs.PowerCapitalCostAudPerMw),
                EnergyCapacityCost.FromAudPerMwhCapacity(costs.EnergyCapitalCostAudPerMwh),
                AnnualPowerCapacityCost.FromAudPerMwYear(
                    costs.FixedOperatingCostAudPerMwYear)),
            new StorageTechnologyProfile(
                storageFleetSettings.TechnologyProfile.TechnicalLifeYears,
                storageFleetSettings.TechnologyProfile.RoundTripEfficiency));
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