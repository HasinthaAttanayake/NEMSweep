namespace NEMSweep.CLI.Application;

/// <summary>
/// The workspace overrides a command line may carry, stripped out before the command itself is
/// matched. Keeping them out of the command patterns is what lets every command keep the shape the
/// CLI reference documents while still accepting a root override anywhere on the line.
/// </summary>
/// <param name="DataRoot">Value of <c>--data-root</c>, or <see langword="null"/> when absent.</param>
/// <param name="OutputRoot">Value of <c>--output</c>, or <see langword="null"/> when absent.</param>
/// <param name="Csv">Whether <c>--csv</c> was given, asking for the star schema alongside the JSON.</param>
/// <param name="Format">How a command reports its result.</param>
/// <param name="Points">
/// Value of <c>--points</c>, a comma-separated list of sweep point IDs, or <see langword="null"/>
/// when absent. Which IDs are valid depends on the sweep definition, so the list is carried as
/// written and resolved by the command that reads the definition.
/// </param>
/// <param name="FailOnUnmet">
/// Whether <c>--fail-on-unmet</c> was given, asking for a missed reliability target to be a
/// non-zero exit. Opt-in because a run that misses its target has still run, and the existing
/// contract is that such a run exits 0.
/// </param>
internal sealed record CliOptions(
    string? DataRoot,
    string? OutputRoot,
    bool Csv = false,
    OutputFormat Format = OutputFormat.Text,
    string? Points = null,
    bool FailOnUnmet = false)
{
    public const string PointsFlag = "--points";
    public const string FailOnUnmetFlag = "--fail-on-unmet";
    private const string DataRootFlag = "--data-root";
    private const string OutputFlag = "--output";
    private const string CsvFlag = "--csv";
    private const string FormatFlag = "--format";

    /// <summary>Splits a command line into workspace overrides and the command's own arguments.</summary>
    /// <param name="args">The raw command line.</param>
    /// <param name="remaining">The arguments left once overrides are removed.</param>
    public static CliOptions Parse(string[] args, out string[] remaining)
    {
        string? dataRoot = null;
        string? outputRoot = null;
        bool csv = false;
        bool failOnUnmet = false;
        string? points = null;
        OutputFormat format = OutputFormat.Text;
        var rest = new List<string>(args.Length);

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument is CsvFlag)
            {
                csv = true;
                continue;
            }

            if (argument is FailOnUnmetFlag)
            {
                failOnUnmet = true;
                continue;
            }

            if (argument is not (DataRootFlag or OutputFlag or FormatFlag or PointsFlag))
            {
                rest.Add(argument);
                continue;
            }

            if (index + 1 >= args.Length)
            {
                throw new UsageException(argument switch
                {
                    FormatFlag => $"{argument} requires 'text' or 'json'.",
                    PointsFlag => $"{argument} requires a comma-separated list of point IDs.",
                    _ => $"{argument} requires a directory.",
                });
            }

            string value = args[++index];
            if (argument is DataRootFlag)
            {
                dataRoot = value;
            }
            else if (argument is OutputFlag)
            {
                outputRoot = value;
            }
            else if (argument is PointsFlag)
            {
                points = value;
            }
            else
            {
                format = value switch
                {
                    "json" => OutputFormat.Json,
                    "text" => OutputFormat.Text,
                    _ => throw new UsageException($"{FormatFlag} must be 'text' or 'json'."),
                };
            }
        }

        remaining = [.. rest];
        return new CliOptions(dataRoot, outputRoot, csv, format, points, failOnUnmet);
    }
}
