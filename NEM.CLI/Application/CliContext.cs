using NEM.CLI.Configuration;
using NEM.CLI.Infrastructure;

namespace NEM.CLI.Application;

internal sealed record CliContext(
    RepositoryPaths Paths,
    string SettingsDirectory,
    TextWriter Output)
{
    public CliSettings LoadSettings() => CliSettings.Load(SettingsDirectory);
}