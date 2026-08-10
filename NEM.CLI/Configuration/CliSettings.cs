using System.Text.Json;
using NEM.CLI.Infrastructure;

namespace NEM.CLI.Configuration;

internal sealed record CliSettings(
    OperationalDemandSettings OperationalDemand,
    ScenarioSettings Scenario)
{
    public static CliSettings Load(string settingsDirectory)
    {
        string localPath = Path.Combine(settingsDirectory, "appsettings.local.json");
        string path = File.Exists(localPath)
            ? localPath
            : Path.Combine(settingsDirectory, "appsettings.example.json");
        CliSettings settings = JsonSerializer.Deserialize<CliSettings>(
            File.ReadAllBytes(path),
            JsonFile.ReadOptions)
            ?? throw new FormatException("CLI settings are empty.");
        settings.Validate();
        return settings;
    }

    internal static ScenarioSettings LoadScenario(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Scenario config was not found.", path);
        }

        ScenarioSettings scenario = JsonSerializer.Deserialize<ScenarioSettings>(
            File.ReadAllBytes(path),
            JsonFile.ReadOptions)
            ?? throw new FormatException("Scenario config is empty.");
        new CliSettings(
            new OperationalDemandSettings("standalone", "standalone", default),
            scenario).Validate();
        return scenario;
    }

    private void Validate()
    {
        ArgumentNullException.ThrowIfNull(OperationalDemand);
        ArgumentNullException.ThrowIfNull(Scenario);
        ArgumentException.ThrowIfNullOrWhiteSpace(OperationalDemand.ArchiveDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(OperationalDemand.Region);
        ArgumentException.ThrowIfNullOrWhiteSpace(Scenario.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Scenario.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(Scenario.DemandFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(Scenario.WeatherFile);
        if (double.IsNaN(Scenario.DataCentreNameplateMw)
            || double.IsInfinity(Scenario.DataCentreNameplateMw)
            || Scenario.DataCentreNameplateMw < 0)
        {
            throw new FormatException("scenario.dataCentreNameplateMw must be a finite, non-negative number.");
        }

        ArgumentNullException.ThrowIfNull(Scenario.CostBasis);
        ArgumentNullException.ThrowIfNull(Scenario.Regions);
        if (Scenario.Regions.Length == 0
            || Scenario.Regions.Any(region =>
                region is null
                || string.IsNullOrWhiteSpace(region.RegionId)
                || region.GeneratingFleets is null
                || region.GeneratingFleets.Length == 0
                || region.StorageFleets is null
                || region.StorageFleets.Length == 0
                || region.StorageFleets.Any(fleet =>
                    fleet is null
                    || string.IsNullOrWhiteSpace(fleet.Technology)
                    || fleet.CostParameters is null
                    || fleet.TechnologyProfile is null)
                || region.GeneratingFleets.Any(fleet =>
                    fleet is null
                    || fleet.CostParameters is null
                    || fleet.TechnologyProfile is null)))
        {
            throw new FormatException(
                "scenario.regions must each define a region ID, generating fleets, and storage fleets with cost parameters and technology profiles.");
        }

        ArgumentNullException.ThrowIfNull(Scenario.StorageSizing);
    }
}

internal sealed record OperationalDemandSettings(
    string ArchiveDirectory,
    string Region,
    DateTimeOffset PeriodStart);

internal sealed record ScenarioSettings(
    string Id,
    string Name,
    string DemandFile,
    string WeatherFile,
    CostBasisSettings CostBasis,
    ScenarioRegionSettings[] Regions,
    StorageSizingSettings StorageSizing,
    double DataCentreNameplateMw = 0);

internal sealed record ScenarioRegionSettings(
    string RegionId,
    GeneratingFleetSettings[] GeneratingFleets,
    StorageFleetSettings[] StorageFleets);

internal sealed record CostBasisSettings(
    int Year,
    decimal RealDiscountRate);

internal sealed record StorageSizingSettings(
    double MaximumPowerMw,
    double MaximumEnergyMwh,
    double TargetUsePercentage = 0.002,
    int MaximumPasses = 256,
    // Names the standard the target represents, so an artifact can say what the number is
    // without a client asserting it. Null when the target is not a published standard.
    string? ReliabilityStandardName = null);

internal sealed record GeneratingFleetSettings(
    string Technology,
    double NameplateCapacityMw,
    CostParametersSettings CostParameters,
    GenerationTechnologyProfileSettings TechnologyProfile,
    MonthlyCapacityFactorSettings[]? MonthlyCapacityFactors = null);

internal sealed record GenerationTechnologyProfileSettings(
    double HeatRateGjPerMwh,
    uint TechnicalLifeYears);

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

internal sealed record StorageTechnologyProfileSettings(
    uint TechnicalLifeYears,
    double RoundTripEfficiency);

internal sealed record MonthlyCapacityFactorSettings(
    DateOnly Month,
    double CapacityFactor);