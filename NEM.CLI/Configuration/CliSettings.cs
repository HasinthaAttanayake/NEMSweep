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
        ArgumentNullException.ThrowIfNull(Scenario.CostBasis);
        ArgumentNullException.ThrowIfNull(Scenario.GeneratingFleets);
        if (Scenario.GeneratingFleets.Any(fleet => fleet is null || fleet.CostParameters is null))
        {
            throw new FormatException("scenario.generatingFleets must each define costParameters.");
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
    GeneratingFleetSettings[] GeneratingFleets,
    StorageSizingSettings StorageSizing);

internal sealed record CostBasisSettings(
    int Year,
    double RealDiscountRate);

internal sealed record StorageSizingSettings(
    double MaximumPowerMw,
    double MaximumEnergyMwh,
    double TargetUsePercentage = 0.002,
    int MaximumPasses = 256);

internal sealed record GeneratingFleetSettings(
    string Technology,
    double NameplateCapacityMw,
    CostParametersSettings CostParameters,
    MonthlyCapacityFactorSettings[]? MonthlyCapacityFactors = null);

internal sealed record CostParametersSettings(
    decimal CapitalCostAudPerMw,
    decimal EnergyCapitalCostAudPerMwh,
    decimal FixedOperatingCostAudPerMwYear,
    decimal VariableOperatingCostAudPerMwh,
    decimal FuelPriceAudPerGj);

internal sealed record MonthlyCapacityFactorSettings(
    DateOnly Month,
    double CapacityFactor);