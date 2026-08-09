using NEM.CLI.Configuration;
using NEM.CLI.Infrastructure;

namespace NEM.CLI.Application;

internal sealed record CliContext(
    RepositoryPaths Paths,
    string SettingsDirectory,
    TextWriter Output,
    TextWriter? Error = null)
{
    public CliSettings LoadSettings() => CliSettings.Load(SettingsDirectory);
}