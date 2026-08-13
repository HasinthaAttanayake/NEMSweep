using System.Text.Json;
using NEM.CLI.Infrastructure;

namespace NEM.CLI.Configuration;

internal sealed record CliSettings(
    string InputBundleRoot,
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
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(DefaultScenarioPath);
    }
}
