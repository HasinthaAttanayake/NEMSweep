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
/// <param name="Csv">Whether this run also writes the star schema CSV tables.</param>
/// <param name="Format">How a command reports its result.</param>
/// <param name="Points">
/// The sweep point IDs this run is restricted to, or <see langword="null"/> to run every point.
/// Empty is not a valid value: a selection naming nothing is a mistake rather than a request to
/// run nothing, and is rejected where it is parsed.
/// </param>
/// <param name="FailOnUnmet">Whether a missed reliability target should make this run exit non-zero.</param>
internal sealed record CliContext(
    WorkspacePaths Paths,
    CliSettings Settings,
    TextWriter Output,
    TextWriter? Error = null,
    bool Csv = false,
    OutputFormat Format = OutputFormat.Text,
    IReadOnlyList<string>? Points = null,
    bool FailOnUnmet = false)
{
    /// <summary>The settings this invocation's workspace was resolved from.</summary>
    public CliSettings LoadSettings() => Settings;
}
