namespace NEMSweep.CLI.Application;

/// <summary>
/// The workspace overrides a command line may carry, stripped out before the command itself is
/// matched. Keeping them out of the command patterns is what lets every command keep the shape the
/// CLI reference documents while still accepting a root override anywhere on the line.
/// </summary>
/// <param name="DataRoot">Value of <c>--data-root</c>, or <see langword="null"/> when absent.</param>
/// <param name="OutputRoot">Value of <c>--output</c>, or <see langword="null"/> when absent.</param>
internal sealed record CliOptions(string? DataRoot, string? OutputRoot)
{
    private const string DataRootFlag = "--data-root";
    private const string OutputFlag = "--output";

    /// <summary>Splits a command line into workspace overrides and the command's own arguments.</summary>
    /// <param name="args">The raw command line.</param>
    /// <param name="remaining">The arguments left once overrides are removed.</param>
    public static CliOptions Parse(string[] args, out string[] remaining)
    {
        string? dataRoot = null;
        string? outputRoot = null;
        var rest = new List<string>(args.Length);

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument is not (DataRootFlag or OutputFlag))
            {
                rest.Add(argument);
                continue;
            }

            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"{argument} requires a directory.");
            }

            if (argument is DataRootFlag)
            {
                dataRoot = args[++index];
            }
            else
            {
                outputRoot = args[++index];
            }
        }

        remaining = [.. rest];
        return new CliOptions(dataRoot, outputRoot);
    }
}
