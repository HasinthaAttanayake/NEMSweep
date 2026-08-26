using System.Text.Json;
using NEMSweep.CLI.Infrastructure;

namespace NEMSweep.CLI.Configuration;

internal sealed record CliSettings(
    string InputBundleRoot,
    string DataRoot,
    string OutputRoot,
    string DefaultScenarioPath)
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
        ArgumentException.ThrowIfNullOrWhiteSpace(InputBundleRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(DataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(DefaultScenarioPath);
    }
}
