using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Infrastructure;

namespace NEMSweep.CLI.Application;

/// <summary>
/// Everything one command needs that is not its own arguments: the resolved workspace, the settings
/// they were resolved from, and where to write.
/// </summary>
/// <param name="Paths">Roots this invocation reads from and writes to.</param>
/// <param name="Settings">Settings loaded for this invocation.</param>
/// <param name="Output">Standard output.</param>
/// <param name="Error">Standard error, when the caller supplied one.</param>
internal sealed record CliContext(
    WorkspacePaths Paths,
    CliSettings Settings,
    TextWriter Output,
    TextWriter? Error = null)
{
    /// <summary>The settings this invocation's workspace was resolved from.</summary>
    public CliSettings LoadSettings() => Settings;
}
