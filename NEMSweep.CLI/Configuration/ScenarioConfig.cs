using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.Contracts;
using NEMSweep.Model.StorageSizing;
using NEMSweep.Model.Units;

namespace NEMSweep.CLI.Configuration;

internal static class ScenarioConfig
{
    public static ScenarioSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Scenario config was not found.", path);
        }

        byte[] contents = File.ReadAllBytes(path);
        using JsonDocument document = JsonDocument.Parse(contents);
        if (document.RootElement.TryGetProperty("demandFile", out _)
            || document.RootElement.TryGetProperty("weatherFile", out _)
            || document.RootElement.TryGetProperty("dataCentreNameplateMw", out _))
        {
            throw new JsonException("Scenario input fields must be defined on each region.");
        }

        // The schema version is read before deserialising, not after. A file written for an older
        // schema is missing whatever the newer one requires, so deserialising first would report a
        // missing property where the honest answer is that the whole file predates this version.
        RequireCurrentSchemaVersion(document.RootElement);

        ScenarioSettings scenario = JsonFile.ReadConfig<ScenarioSettings>(contents)
            ?? throw new FormatException("Scenario config is empty.");
        Validate(scenario);
        return scenario;
    }

    /// <summary>
    /// Maps the configured storage-sizing block onto the model's options. The one place this
    /// mapping lives, so a dispatch run and the artifact it publishes cannot describe different
    /// bounds.
    /// </summary>
    public static StorageSizingOptions CreateSizingOptions(StorageSizingSettings sizing)
    {
        ArgumentNullException.ThrowIfNull(sizing);
        return new StorageSizingOptions(
            Power.FromMegawatts(sizing.MaximumPowerMw),
            Energy.FromMegawattHours(sizing.MaximumEnergyMwh),
            sizing.TargetUsePercentage,
            sizing.MaximumPasses);
    }

    /// <summary>
    /// Reports a schema-version mismatch from the raw document, before deserialisation can fail on
    /// a property the file's own schema version never had.
    /// </summary>
    private static void RequireCurrentSchemaVersion(JsonElement root)
    {
        if (!root.TryGetProperty("schemaVersion", out JsonElement version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out int declared))
        {
            // Absent or malformed: leave it to deserialisation and Validate, which report the
            // property itself rather than guessing at a version.
            return;
        }

        RequireCurrentSchemaVersion(declared);
    }

    private static void RequireCurrentSchemaVersion(int declared)
    {
        if (declared != ArtifactSchemaVersions.ScenarioConfig)
        {
            throw new FormatException(
                $"Scenario config schema version found {declared}; "
                + $"expected {ArtifactSchemaVersions.ScenarioConfig}.");
        }
    }

    private static void Validate(ScenarioSettings scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        RequireCurrentSchemaVersion(scenario.SchemaVersion);

        ArgumentException.ThrowIfNullOrWhiteSpace(scenario.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario.Name);

        ArgumentNullException.ThrowIfNull(scenario.CostBasis);
        if (scenario.CostBasis.Year is < 2000 or > 2100)
        {
            throw new FormatException($"scenario.costBasis.year {scenario.CostBasis.Year} must be between 2000 and 2100.");
        }

        ArgumentNullException.ThrowIfNull(scenario.Regions);
        if (scenario.Regions.Length == 0)
        {
            throw new FormatException("scenario.regions must define at least one region.");
        }

        ArgumentNullException.ThrowIfNull(scenario.StorageSizing);
        if (double.IsNaN(scenario.StorageSizing.TargetUsePercentage)
            || double.IsInfinity(scenario.StorageSizing.TargetUsePercentage)
            || scenario.StorageSizing.TargetUsePercentage <= 0
            || scenario.StorageSizing.TargetUsePercentage > 100)
        {
            throw new FormatException($"scenario.storageSizing.targetUsePercentage {scenario.StorageSizing.TargetUsePercentage} must be greater than 0 and at most 100.");
        }

        var regionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ScenarioRegionSettings? region in scenario.Regions)
        {
            if (region is null || string.IsNullOrWhiteSpace(region.RegionId))
            {
                throw new FormatException("scenario.regions.regionId must be a known, non-blank region ID.");
            }

            if (!NemRegions.IsKnown(region.RegionId))
            {
                throw new FormatException($"scenario.regions region '{region.RegionId}' is not a known NEM region.");
            }

            if (!regionIds.Add(region.RegionId))
            {
                throw new FormatException($"scenario.regions region '{region.RegionId}' must be distinct.");
            }

            if (string.IsNullOrWhiteSpace(region.DemandFile))
            {
                throw new FormatException($"scenario.regions region '{region.RegionId}' field 'demandFile' must not be blank.");
            }

            if (string.IsNullOrWhiteSpace(region.WeatherFile))
            {
                throw new FormatException($"scenario.regions region '{region.RegionId}' field 'weatherFile' must not be blank.");
            }

            ValidateRegionalNonNegative(region.DataCentreNameplateMw, region.RegionId, "dataCentreNameplateMw");

            if (region.GeneratingFleets is null || region.GeneratingFleets.Length == 0)
            {
                throw new FormatException($"scenario.regions region '{region.RegionId}' must define generatingFleets.");
            }

            if (region.StorageFleets is null || region.StorageFleets.Length == 0)
            {
                throw new FormatException($"scenario.regions region '{region.RegionId}' must define storageFleets.");
            }

            ValidateGeneratingFleets(region);
            ValidateStorageFleets(region);
        }

        ValidateInterconnectors(scenario.Interconnectors, regionIds);
    }


    /// <summary>Validates one region's generating fleets and their cost and technology blocks.</summary>
    private static void ValidateGeneratingFleets(ScenarioRegionSettings region)
    {
        foreach (GeneratingFleetSettings? fleet in region.GeneratingFleets)
        {
            string technology = fleet?.Technology ?? "<blank>";
            if (fleet is null || string.IsNullOrWhiteSpace(fleet.Technology))
            {
                throw new FormatException($"scenario.regions region '{region.RegionId}' generating fleet technology '{technology}' must not be blank.");
            }

            if (fleet.CostParameters is null || fleet.TechnologyProfile is null)
            {
                throw new FormatException($"scenario.regions region '{region.RegionId}' generating fleet technology '{fleet.Technology}' must define costParameters and technologyProfile.");
            }

            ValidateNonNegative(fleet.NameplateCapacityMw, region.RegionId, fleet.Technology, "nameplateCapacityMw");
            ValidateNonNegative(fleet.TechnologyProfile.HeatRateGjPerMwh, region.RegionId, fleet.Technology, "heatRateGjPerMwh");
            ValidateNonNegative(fleet.TechnologyProfile.EmissionsIntensityTonnesPerMwh, region.RegionId, fleet.Technology, "emissionsIntensityTonnesPerMwh");
            ValidateCosts(region.RegionId, fleet.Technology, fleet.CostParameters);
            if (fleet.MonthlyCapacityFactors is not null)
            {
                foreach (MonthlyCapacityFactorSettings? factor in fleet.MonthlyCapacityFactors)
                {
                    if (factor is null || double.IsNaN(factor.CapacityFactor) || double.IsInfinity(factor.CapacityFactor)
                        || factor.CapacityFactor <= 0 || factor.CapacityFactor > 1)
                    {
                        double value = factor?.CapacityFactor ?? double.NaN;
                        throw new FormatException($"scenario.regions region '{region.RegionId}' generating fleet technology '{fleet.Technology}' field 'capacityFactor' value {value} must be finite, greater than 0, and at most 1.");
                    }
                }
            }
        }
    }

    /// <summary>Validates one region's storage fleets and their cost and technology blocks.</summary>
    private static void ValidateStorageFleets(ScenarioRegionSettings region)
    {
        foreach (StorageFleetSettings? fleet in region.StorageFleets)
        {
            string technology = fleet?.Technology ?? "<blank>";
            if (fleet is null || string.IsNullOrWhiteSpace(fleet.Technology))
            {
                throw new FormatException($"scenario.regions region '{region.RegionId}' storage fleet technology '{technology}' must not be blank.");
            }

            if (fleet.CostParameters is null || fleet.TechnologyProfile is null)
            {
                throw new FormatException($"scenario.regions region '{region.RegionId}' storage fleet technology '{fleet.Technology}' must define costParameters and technologyProfile.");
            }

            double efficiency = fleet.TechnologyProfile.RoundTripEfficiency;
            if (double.IsNaN(efficiency) || double.IsInfinity(efficiency) || efficiency < 0 || efficiency > 1)
            {
                throw new FormatException($"scenario.regions region '{region.RegionId}' storage fleet technology '{fleet.Technology}' field 'roundTripEfficiency' must be finite and between 0 and 1.");
            }

            ValidateCosts(region.RegionId, fleet.Technology, fleet.CostParameters);
        }
    }

    private static void ValidateInterconnectors(
        ScenarioInterconnectorSettings[]? interconnectors,
        IReadOnlySet<string> regionIds)
    {
        if (interconnectors is null)
        {
            return;
        }

        var directions = new HashSet<(string From, string To)>();
        foreach (ScenarioInterconnectorSettings? interconnector in interconnectors)
        {
            if (interconnector is null
                || string.IsNullOrWhiteSpace(interconnector.FromRegionId)
                || string.IsNullOrWhiteSpace(interconnector.ToRegionId))
            {
                throw new FormatException("scenario.interconnectors fromRegionId and toRegionId must be non-blank.");
            }

            string from = interconnector.FromRegionId;
            string to = interconnector.ToRegionId;
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException("scenario.interconnectors fromRegionId and toRegionId must identify different regions.");
            }

            if (!regionIds.Contains(from) || !regionIds.Contains(to))
            {
                throw new FormatException($"scenario.interconnectors regions '{from}' and '{to}' must belong to scenario.regions.");
            }

            var direction = (from.ToUpperInvariant(), to.ToUpperInvariant());
            if (!directions.Add(direction))
            {
                throw new FormatException($"scenario.interconnectors cannot duplicate direction '{from}' to '{to}'.");
            }

            ValidateNonNegative(interconnector.CapacityMw, "interconnectors", "capacityMw");
            if (double.IsNaN(interconnector.RouteLengthKm)
                || double.IsInfinity(interconnector.RouteLengthKm)
                || interconnector.RouteLengthKm <= 0)
            {
                throw new FormatException(
                    $"scenario.interconnectors field 'routeLengthKm' value {interconnector.RouteLengthKm} "
                    + "must be finite and positive.");
            }

            ValidateNonNegative(interconnector.CapitalCostAudPerKmPerMw, "interconnectors", "capitalCostAudPerKmPerMw");
            ValidateNonNegative(interconnector.FixedOperatingCostAudPerKmPerMwYear, "interconnectors", "fixedOperatingCostAudPerKmPerMwYear");
            if (interconnector.TechnicalLifeYears == 0)
            {
                throw new FormatException("scenario.interconnectors technicalLifeYears must be nonzero.");
            }
        }
    }

    private static void ValidateCosts(string regionId, string technology, CostParametersSettings costs)
    {
        ValidateNonNegative(costs.CapitalCostAudPerMw, regionId, technology, "capitalCostAudPerMw");
        ValidateNonNegative(costs.FixedOperatingCostAudPerMwYear, regionId, technology, "fixedOperatingCostAudPerMwYear");
        ValidateNonNegative(costs.VariableOperatingCostAudPerMwh, regionId, technology, "variableOperatingCostAudPerMwh");
        ValidateNonNegative(costs.FuelPriceAudPerGj, regionId, technology, "fuelPriceAudPerGj");
    }

    private static void ValidateCosts(string regionId, string technology, StorageCostParametersSettings costs)
    {
        ValidateNonNegative(costs.PowerCapitalCostAudPerMw, regionId, technology, "powerCapitalCostAudPerMw");
        ValidateNonNegative(costs.EnergyCapitalCostAudPerMwh, regionId, technology, "energyCapitalCostAudPerMwh");
        ValidateNonNegative(costs.FixedOperatingCostAudPerMwYear, regionId, technology, "fixedOperatingCostAudPerMwYear");
    }

    private static void ValidateNonNegative(double value, string regionId, string technology, string field)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            throw new FormatException($"scenario.regions region '{regionId}' technology '{technology}' field '{field}' must be finite and non-negative.");
        }
    }

    private static void ValidateNonNegative(double value, string scope, string field)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            throw new FormatException($"scenario.{scope} field '{field}' must be finite and non-negative.");
        }
    }

    private static void ValidateRegionalNonNegative(double value, string regionId, string field)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            throw new FormatException($"scenario.regions region '{regionId}' field '{field}' must be finite and non-negative.");
        }
    }

    private static void ValidateNonNegative(decimal value, string regionId, string technology, string field)
    {
        if (value < 0)
        {
            throw new FormatException($"scenario.regions region '{regionId}' technology '{technology}' field '{field}' must be non-negative.");
        }
    }

    private static void ValidateNonNegative(decimal value, string scope, string field)
    {
        if (value < 0)
        {
            throw new FormatException($"scenario.{scope} field '{field}' must be non-negative.");
        }
    }
}

internal sealed record ScenarioSettings(
    int SchemaVersion,
    string Id,
    string Name,
    CostBasisSettings CostBasis,
    ScenarioRegionSettings[] Regions,
    StorageSizingSettings StorageSizing,
    JsonObject? Provenance = null,
    ScenarioInterconnectorSettings[]? Interconnectors = null)
{
    [JsonIgnore]
    public string DemandFile => Regions.Single().DemandFile;

    [JsonIgnore]
    public string WeatherFile => Regions.Single().WeatherFile;

    [JsonIgnore]
    public double DataCentreNameplateMw => Regions.Single().DataCentreNameplateMw;
}

internal sealed record ScenarioRegionSettings(
    string RegionId,
    GeneratingFleetSettings[] GeneratingFleets,
    StorageFleetSettings[] StorageFleets,
    string DemandFile,
    string WeatherFile,
    double DataCentreNameplateMw = 0);

internal sealed record CostBasisSettings(int Year, decimal RealDiscountRate);

internal sealed record StorageSizingSettings(
    double MaximumPowerMw,
    double MaximumEnergyMwh,
    double TargetUsePercentage = 0.002,
    int MaximumPasses = 256,
    string? ReliabilityStandardName = null);

internal sealed record GeneratingFleetSettings(
    string Technology,
    double NameplateCapacityMw,
    CostParametersSettings CostParameters,
    GenerationTechnologyProfileSettings TechnologyProfile,
    MonthlyCapacityFactorSettings[]? MonthlyCapacityFactors = null);

internal sealed record GenerationTechnologyProfileSettings(
    [property: JsonRequired] double HeatRateGjPerMwh,
    [property: JsonRequired] uint TechnicalLifeYears,
    [property: JsonRequired] double EmissionsIntensityTonnesPerMwh);

internal sealed record CostParametersSettings(
    decimal CapitalCostAudPerMw,
    decimal FixedOperatingCostAudPerMwYear,
    decimal VariableOperatingCostAudPerMwh,
    decimal FuelPriceAudPerGj);

internal sealed record StorageFleetSettings(
    string Technology,
    double InitialEnergyCapacityMwh,
    double InitialPowerCapacityMw,
    StorageCostParametersSettings CostParameters,
    StorageTechnologyProfileSettings TechnologyProfile);

internal sealed record StorageCostParametersSettings(
    decimal PowerCapitalCostAudPerMw,
    decimal EnergyCapitalCostAudPerMwh,
    decimal FixedOperatingCostAudPerMwYear);

internal sealed record StorageTechnologyProfileSettings(uint TechnicalLifeYears, double RoundTripEfficiency);

internal sealed record ScenarioInterconnectorSettings(
    [property: JsonRequired] string FromRegionId,
    [property: JsonRequired] string ToRegionId,
    [property: JsonRequired] double CapacityMw,
    [property: JsonRequired] double RouteLengthKm,
    [property: JsonRequired] decimal CapitalCostAudPerKmPerMw,
    [property: JsonRequired] decimal FixedOperatingCostAudPerKmPerMwYear,
    [property: JsonRequired] uint TechnicalLifeYears);

internal sealed record MonthlyCapacityFactorSettings(DateOnly Month, double CapacityFactor);