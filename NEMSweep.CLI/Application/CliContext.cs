using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Infrastructure;

namespace NEMSweep.CLI.Application;

internal sealed record CliContext(
    RepositoryPaths Paths,
    string SettingsDirectory,
    TextWriter Output,
    TextWriter? Error = null)
{
    public CliSettings LoadSettings() => CliSettings.Load(SettingsDirectory);
}