using NEMSweep.Contracts;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Demand;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.CLI.Weather;
using NEMSweep.Model.Economics;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Scenarios;
using NEMSweep.Model.Series;
using NEMSweep.Model.StorageSizing;
using NEMSweep.Model.Units;
using NEMSweep.Model.Weather;
using System.Security.Cryptography;
using System.Text.Json;
using DomainScenario = NEMSweep.Model.Scenarios.Scenario;

namespace NEMSweep.CLI.Scenarios;

internal static class ScenarioRunner
{
    private static readonly TimeSpan HourlyResolution = TimeSpan.FromHours(1);

    internal static ScenarioDispatchResult RunDispatch(
        ScenarioSettings settings,
        WorkspacePaths paths)
    {
        var demandInputs = new Dictionary<string, LoadedInput<OperationalDemandData>>(
            StringComparer.OrdinalIgnoreCase);
        var demandByRegion = new Dictionary<string, FlowSeries>(StringComparer.OrdinalIgnoreCase);
        FlowSeries? scenarioTimeline = null;

        foreach (ScenarioRegionSettings regionSettings in settings.Regions)
        {
            string demandPath = ResolveScenarioInputPath(paths, regionSettings.DemandFile);
            LoadedInput<OperationalDemandData> demandInput = ReadInput(() => ReadDemand(demandPath));
            if (!string.Equals(
                    demandInput.Value.Region,
                    regionSettings.RegionId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ScenarioRunException(
                    SweepFailureStage.Input,
                    "demandRegionMismatch",
                    $"Demand artifact region '{demandInput.Value.Region}' does not match scenario region '{regionSettings.RegionId}'.");
            }

            FlowSeries hourlyDemand = demandInput.Value.Demand.ResampleToHourly();
            if (scenarioTimeline is null)
            {
                scenarioTimeline = hourlyDemand;
            }
            else if (!SameTimeline(scenarioTimeline, hourlyDemand))
            {
                throw new ScenarioRunException(
                    SweepFailureStage.Input,
                    "demandTimelineMismatch",
                    $"Demand timeline for region '{regionSettings.RegionId}' does not align with the other scenario regions.");
            }

            demandInputs.Add(regionSettings.RegionId, demandInput);
            demandByRegion.Add(regionSettings.RegionId, hourlyDemand);
        }

        FlowSeries timeline = scenarioTimeline
            ?? throw new ScenarioRunException(
                SweepFailureStage.Input,
                "missingScenarioRegions",
                "Scenario must define at least one region.");
        DomainScenario scenario = BuildScenario(settings, timeline);
        var weatherInputs = new Dictionary<string, LoadedInput<WeatherDataDTO>>(
            StringComparer.OrdinalIgnoreCase);
        var resourcesByRegion = new Dictionary<string, RegionalResourceProfile?>(
            StringComparer.OrdinalIgnoreCase);
        foreach (ScenarioRegionSettings regionSettings in settings.Regions)
        {
            string weatherPath = ResolveScenarioInputPath(paths, regionSettings.WeatherFile);
            LoadedInput<WeatherDataDTO> weatherInput = ReadInput(() => ReadWeather(weatherPath));
            if (!string.Equals(
                    weatherInput.Value.RegionId,
                    regionSettings.RegionId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ScenarioRunException(
                    SweepFailureStage.Input,
                    "weatherRegionMismatch",
                    $"Weather artifact region '{weatherInput.Value.RegionId}' does not match scenario region '{regionSettings.RegionId}'.");
            }

            resourcesByRegion.Add(
                regionSettings.RegionId,
                ReadInput(() => ReadWeatherForTimeline(
                    weatherInput.Value,
                    demandByRegion[regionSettings.RegionId])));
            weatherInputs.Add(regionSettings.RegionId, weatherInput);
        }

        PowerSystem powerSystem = ScenarioDerivation.Derive(
            scenario,
            demandByRegion,
            resourcesByRegion,
            CreateDataCentreDemandComponents(settings, scenario));
        StorageSizingOptions sizingOptions = ScenarioConfig.CreateSizingOptions(settings.StorageSizing);
        StorageSizingRunResult sizingResult = Size(powerSystem, sizingOptions);
        return new ScenarioDispatchResult(
            scenario,
            powerSystem,
            sizingResult,
            Cost(scenario, sizingResult),
            demandInputs,
            weatherInputs);
    }

    /// <summary>
    /// Resolves a scenario input against the data root. A scenario names its artifacts by file name,
    /// so the data root is the single place they are looked for; an absolute path is used as given.
    /// Provenance callers use this too, so they hash the exact bytes dispatch consumed.
    /// </summary>
    /// <param name="paths">Workspace the configured path is resolved against.</param>
    /// <param name="configuredPath">The path as written in the scenario config.</param>
    internal static string ResolveScenarioInputPath(WorkspacePaths paths, string configuredPath) =>
        Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(configuredPath, paths.DataRoot);

    private static bool SameTimeline(FlowSeries first, FlowSeries second) =>
        first.Start == second.Start
        && first.Resolution == second.Resolution
        && first.Length == second.Length;

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

        return result;
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
            new CostBasis(settings.CostBasis.Year, settings.CostBasis.RealDiscountRate),
            settings.Interconnectors?.Select(interconnector =>
                new ScenarioInterconnector(
                    interconnector.FromRegionId,
                    interconnector.ToRegionId,
                    Power.FromMegawatts(interconnector.CapacityMw),
                    Distance.FromKilometres(interconnector.RouteLengthKm),
                    new TransmissionCostParameters(
                        DistancePowerCost.FromAudPerKmPerMw(
                            interconnector.CapitalCostAudPerKmPerMw),
                        AnnualDistancePowerCost.FromAudPerKmPerMwYear(
                            interconnector.FixedOperatingCostAudPerKmPerMwYear)),
                    interconnector.TechnicalLifeYears)).ToArray());
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<DemandComponent>>?
        CreateDataCentreDemandComponents(ScenarioSettings settings, DomainScenario scenario)
    {
        if (settings.Regions.All(region => region.DataCentreNameplateMw == 0))
        {
            return null;
        }

        int intervalCount = checked((int)((scenario.PeriodEnd - scenario.PeriodStart).Ticks
            / HourlyResolution.Ticks));
        var components = new Dictionary<string, IReadOnlyList<DemandComponent>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (ScenarioRegionSettings region in settings.Regions)
        {
            if (region.DataCentreNameplateMw == 0)
            {
                components[region.RegionId] = [];
                continue;
            }

            FlowSeries demand = DataCentreDemand.Expand(
                Power.FromMegawatts(region.DataCentreNameplateMw),
                scenario.PeriodStart,
                HourlyResolution,
                intervalCount);
            components[region.RegionId] = [new DemandComponent("Data centre", demand)];
        }

        return components;
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

    internal static RegionalResourceProfile ReadWeatherForTimeline(
        WeatherDataDTO weather,
        FlowSeries timeline)
    {
        if (weather.Resolution != HourlyResolution)
        {
            throw new FormatException("Scenario weather must use hourly resolution.");
        }

        SolarWeatherData solar = weather.Solar;
        WindWeatherData wind = weather.Wind;
        int sourceLength = solar.GlobalHorizontalRadiationWhPerSquareMetre.Length;
        double[][] sourceSeries =
        [
            solar.DirectNormalRadiationWhPerSquareMetre,
            solar.DiffuseHorizontalRadiationWhPerSquareMetre,
            solar.SolarZenithDegrees,
            solar.DryBulbTemperatureDegreesCelsius,
            solar.ProductionMegawattsAtOneMegawattAc,
            wind.WindSpeedMetresPerSecond,
            wind.ProductionMegawattsAtOneMegawattInstalled,
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
                string reason = instant.Month == 2 && instant.Day == 29
                    ? $"Weather source has no typical-year value for {instant:MM-dd HH}:00; "
                        + "29 February is missing."
                    : $"Weather source has no typical-year value for {instant:MM-dd HH}:00.";
                throw new ScenarioRunException(
                    SweepFailureStage.Input,
                    "weatherMissingLeapDay",
                    reason);
            }

            globalHorizontalRadiation[index] = solar.GlobalHorizontalRadiationWhPerSquareMetre[sourceIndex];
            directNormalRadiation[index] = solar.DirectNormalRadiationWhPerSquareMetre[sourceIndex];
            diffuseHorizontalRadiation[index] = solar.DiffuseHorizontalRadiationWhPerSquareMetre[sourceIndex];
            dryBulbTemperature[index] = solar.DryBulbTemperatureDegreesCelsius[sourceIndex];
            windSpeed[index] = wind.WindSpeedMetresPerSecond[sourceIndex];
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
                solar.Location.Latitude,
                solar.Location.Longitude),
            TraceSeries.DryBulbTemperature(
                timeline.Start, HourlyResolution, dryBulbTemperature),
            TraceSeries.WindSpeed(
                timeline.Start,
                HourlyResolution,
                windSpeed,
                wind.MeasurementHeightMetres));
    }
}

internal sealed record LoadedInput<T>(
    T Value,
    DispatchInputArtifactDTO Artifact);

internal sealed record ScenarioDispatchResult(
    DomainScenario Scenario,
    PowerSystem PowerSystem,
    StorageSizingRunResult SizingResult,
    PowerSystemCostBreakdown CostBreakdown,
    IReadOnlyDictionary<string, LoadedInput<OperationalDemandData>> DemandInputs,
    IReadOnlyDictionary<string, LoadedInput<WeatherDataDTO>> WeatherInputs);