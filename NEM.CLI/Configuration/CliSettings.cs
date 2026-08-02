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
        ArgumentNullException.ThrowIfNull(Scenario.GeneratingFleets);
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
    GeneratingFleetSettings[] GeneratingFleets,
    StorageSizingSettings StorageSizing);

internal sealed record StorageSizingSettings(
    double MaximumPowerMw,
    double MaximumEnergyMwh,
    double TargetUsePercentage = 0.002,
    int MaximumPasses = 256);

internal sealed record GeneratingFleetSettings(
    string Technology,
    double NameplateCapacityMw,
    MonthlyCapacityFactorSettings[]? MonthlyCapacityFactors = null);

internal sealed record MonthlyCapacityFactorSettings(
    DateOnly Month,
    double CapacityFactor);