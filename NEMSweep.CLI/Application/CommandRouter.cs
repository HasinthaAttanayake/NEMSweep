using System.Reflection;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Demand;
using NEMSweep.CLI.Generation;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.CLI.Ingest;
using NEMSweep.CLI.QuickStart;
using NEMSweep.CLI.Scenarios;
using NEMSweep.CLI.Weather;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Application;

/// <summary>
/// Maps a command line onto one command handler. Every command is a flag literal followed by zero
/// to three positional arguments; there is no options parser, because the surface is small enough
/// that a pattern match over the argument array is easier to read than a framework. The workspace
/// overrides are stripped by <see cref="CliOptions"/> before that match, so they can appear anywhere
/// on the line without every command pattern having to account for them.
/// </summary>
/// <remarks>
/// Exit codes are the contract callers script against: <c>0</c> success, <c>1</c> a command that
/// ran and failed, <c>2</c> a command line this router could not read. Requesting help is a
/// success, so <c>--help</c> writes usage to standard output and returns <c>0</c>, while an
/// unrecognised command line, or an override missing its value, writes the same usage to standard
/// error and returns <c>2</c>.
/// </remarks>
internal sealed class CommandRouter
{
    private readonly string _settingsDirectory;
    private readonly string _workingRoot;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly TextReader _input;
    private readonly bool _inputRedirected;
    private readonly HttpClient? _quickStartClient;
    private readonly IReadOnlyList<StarterSample>? _samples;

    /// <summary>Creates a router for one process.</summary>
    /// <param name="settingsDirectory">Directory the settings file is loaded from.</param>
    /// <param name="workingRoot">Base for relative paths, normally the current directory.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <param name="input">Selection input, or the process console.</param>
    /// <param name="inputRedirected">Whether selection input is redirected.</param>
    /// <param name="quickStartClient">Optional injected downloader for offline tests.</param>
    /// <param name="samples">Optional injected catalogue for offline tests.</param>
    public CommandRouter(
        string settingsDirectory,
        string workingRoot,
        TextWriter output,
        TextWriter error,
        TextReader? input = null,
        bool? inputRedirected = null,
        HttpClient? quickStartClient = null,
        IReadOnlyList<StarterSample>? samples = null)
    {
        _settingsDirectory = settingsDirectory;
        _workingRoot = workingRoot;
        _output = output;
        _error = error;
        _input = input ?? Console.In;
        _inputRedirected = inputRedirected ?? Console.IsInputRedirected;
        _quickStartClient = quickStartClient;
        _samples = samples;
    }

    /// <summary>Routes one command line and returns the process exit code.</summary>
    public int Run(string[] args)
    {
        try
        {
            if (args.Contains("--quick-start", StringComparer.Ordinal))
            {
                if (args.FirstOrDefault() != "--quick-start"
                    || args.Skip(1).Any(arg => arg.StartsWith("--", StringComparison.Ordinal)))
                {
                    throw new UsageException("Quick-start does not accept workspace/output/format overrides or other flags.");
                }
                if (_quickStartClient is not null)
                {
                    return QuickStartCommand.Run(args, _workingRoot, _input, _inputRedirected,
                        _output, _error, _quickStartClient, _samples ?? StarterSample.LoadCatalogue());
                }
                using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                return QuickStartCommand.Run(args, _workingRoot, _input, _inputRedirected,
                    _output, _error, client, _samples ?? StarterSample.LoadCatalogue());
            }
            CliOptions options = CliOptions.Parse(args, out string[] commandArgs);
            RejectUnsupportedFlags(options, commandArgs);

            // Answered without a workspace, so asking how to use the tool, or for a schema, never
            // fails on a settings file the caller has not written yet.
            switch (commandArgs)
            {
                case ["--help"] or ["-h"] or ["--usage"]:
                    return PrintUsage(_output, 0);
                case ["--version"]:
                    return PrintVersion();
                case ["--describe-schema", var schemaFormat] when schemaFormat is
                    "scenario" or "sweep" or "manifest" or "legacy-manifest" or "demand" or "weather":
                    return SchemaDescriptionCommand.Run(_output, schemaFormat);
                case ["--new-scenario"]:
                    return ScenarioScaffoldCommand.Run(_output);
                case ["--agent-guide"]:
                    return AgentGuideCommand.Run(_output);
            }

            // Checked here rather than inside the handler so a typo is rejected without first
            // reading settings the run was never going to reach.
            if (commandArgs is ["--epw-report", var epwRegionId, ..])
            {
                RequireRegionId(epwRegionId);
            }

            // Matched to a handler before the workspace is built, so an unroutable command line is
            // rejected without reading settings it was never going to use.
            Func<CliContext, int>? handler = commandArgs switch
            {
                ["--validate-scenario"] => ValidateScenarioCommand.Run,
                ["--validate-scenario", var validatePath] =>
                    context => ValidateScenarioCommand.Run(context, validatePath),
                ["--run-scenario"] => ScenarioCommand.Run,
                ["--run-scenario", var scenarioConfigPath] =>
                    context => ScenarioCommand.Run(context, scenarioConfigPath),
                ["--fan-out-sweep", var definitionPath] =>
                    context => SweepFanOutCommand.Run(context, definitionPath),
                ["--run-sweep", var definitionPath] =>
                    context => SweepRunCommand.Run(context, definitionPath),
                ["--validate-inputs"] => context => ValidateInputsCommand.Run(context),
                ["--validate-inputs", var bundlePath] =>
                    context => ValidateInputsCommand.Run(context, bundlePath),
                ["--ingest"] => context => IngestCommand.Run(context),
                ["--ingest", var bundlePath] => context => IngestCommand.Run(context, bundlePath),
                ["--import-demand"] =>
                    context => OperationalDemandCommand.Run(context, string.Empty),
                ["--import-demand", var outputDirectory] =>
                    context => OperationalDemandCommand.Run(context, outputDirectory),
                ["--generation-information", var path] =>
                    context => GenerationInformationCommand.Run(context, path),
                ["--epw-report", var regionId, var solarPath] =>
                    context => EpwCommands.WriteReport(context, RequireRegionId(regionId), solarPath),
                ["--epw-report", var regionId, var solarPath, var windPath] =>
                    context => EpwCommands.WriteReport(
                        context,
                        RequireRegionId(regionId),
                        solarPath,
                        windPath),
                _ => null,
            };

            if (handler is null)
            {
                return PrintUsage(_error, 2);
            }

            return handler(CreateContext(options));
        }
        catch (UsageException exception)
        {
            _error.WriteLine(exception.Message);
            return PrintUsage(_error, 2);
        }
        catch (Exception exception)
        {
            _error.WriteLine($"{OperationName(args)} failed: {exception.Message}");
            return 1;
        }
    }

    /// <summary>Loads settings and resolves the workspace the command will read and write through.</summary>
    private CliContext CreateContext(CliOptions options)
    {
        CliSettings settings = CliSettings.Load(_settingsDirectory);
        WorkspacePaths paths = WorkspacePaths.Create(
            settings,
            _workingRoot,
            options.DataRoot,
            options.OutputRoot);
        return new CliContext(
            paths,
            settings,
            _output,
            _error,
            options.Csv,
            options.Format,
            ParsePoints(options.Points),
            options.FailOnUnmet);
    }

    /// <summary>
    /// Rejects a flag the matched command does not act on. Silently ignoring one is worse than
    /// failing: a caller who asked for a subset of points, or for a missed target to fail the run,
    /// and got neither, has no way to tell from the exit code that the flag went nowhere.
    /// </summary>
    private static void RejectUnsupportedFlags(CliOptions options, string[] commandArgs)
    {
        string? command = commandArgs.FirstOrDefault();
        if (options.Points is not null && command is not "--run-sweep")
        {
            throw new UsageException(
                $"{CliOptions.PointsFlag} applies to --run-sweep only.");
        }

        if (options.FailOnUnmet && command is not ("--run-scenario" or "--run-sweep"))
        {
            throw new UsageException(
                $"{CliOptions.FailOnUnmetFlag} applies to --run-scenario and --run-sweep only.");
        }
    }

    /// <summary>
    /// Splits the <c>--points</c> value into point IDs. Blank entries are dropped so a trailing
    /// comma is not an error, but a value naming nothing at all is: it reads as a request to run a
    /// subset, and running every point instead would be the opposite of what was asked.
    /// </summary>
    private static IReadOnlyList<string>? ParsePoints(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string[] ids = value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length == 0)
        {
            throw new UsageException($"{CliOptions.PointsFlag} named no point IDs.");
        }

        string[] duplicates = ids
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        return duplicates.Length == 0
            ? ids
            : throw new UsageException(
                $"{CliOptions.PointsFlag} named {string.Join(", ", duplicates)} more than once.");
    }

    /// <summary>
    /// Region identities are free-form strings, but the EPW command needs a nonblank identity that
    /// can be represented in a portable artifact filename.
    /// </summary>
    private static string RequireRegionId(string regionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        RegionFileNames.For(regionId);
        return regionId;
    }

    private int PrintVersion()
    {
        Assembly assembly = typeof(CommandRouter).Assembly;
        string version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        _output.WriteLine($"NEMSweep.CLI {version}");
        return 0;
    }

    private static int PrintUsage(TextWriter writer, int exitCode)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  nemsweep --help");
        writer.WriteLine("  nemsweep --version");
        writer.WriteLine("  nemsweep --quick-start [simple|advanced] [new-study-directory]");
        writer.WriteLine("    Explain, download and ingest a Simple or Advanced starter; never run simulations.");
        writer.WriteLine("    No workspace overrides apply. The destination must not already exist.");
        writer.WriteLine();
        writer.WriteLine("  Workspace overrides, accepted alongside any command below:");
        writer.WriteLine("  --data-root <dir>   where inputs are read from  (env NEMSWEEP_DATA_ROOT)");
        writer.WriteLine("  --output <dir>      where results are written   (env NEMSWEEP_OUTPUT)");
        writer.WriteLine("  --csv               also write the star schema CSV tables");
        writer.WriteLine("  --format <text|json>  report as JSON (--validate-scenario)");
        writer.WriteLine("  --fail-on-unmet     exit 1 when the reliability target is not met");
        writer.WriteLine("                      (--run-scenario, --run-sweep)");
        writer.WriteLine();
        writer.WriteLine("  Scenario and sweep runs:");
        writer.WriteLine("  nemsweep --validate-scenario [scenario-config.json]");
        writer.WriteLine("  nemsweep --run-scenario [scenario-config.json]");
        writer.WriteLine("  nemsweep --fan-out-sweep <sweep-definition.json>");
        writer.WriteLine("  nemsweep --run-sweep <sweep-definition.json>");
        writer.WriteLine("    --points <id,id,...>  run only these sweep points");
        writer.WriteLine("  nemsweep --describe-schema <scenario|sweep|manifest|legacy-manifest|demand|weather>");
        writer.WriteLine("  nemsweep --new-scenario");
        writer.WriteLine("  nemsweep --agent-guide");
        writer.WriteLine("    Print the agent guide embedded in this build.");
        writer.WriteLine();
        writer.WriteLine("  Input bundles:");
        writer.WriteLine("  nemsweep --validate-inputs [input-bundle]");
        writer.WriteLine("  nemsweep --ingest [input-bundle]");
        writer.WriteLine();
        writer.WriteLine("  Single-source imports (all covered by --ingest):");
        writer.WriteLine("  nemsweep --import-demand [output-directory]");
        writer.WriteLine("  nemsweep --generation-information <workbook.xlsx>");
        writer.WriteLine("  nemsweep --epw-report <region> <solar.epw> [wind.epw]");
        return exitCode;
    }

    private static string OperationName(string[] args) => args.FirstOrDefault() switch
    {
        "--validate-scenario" => "Scenario validation",
        "--run-scenario" => "Scenario run",
        "--fan-out-sweep" => "Sweep fan-out",
        "--run-sweep" => "Sweep run",
        "--describe-schema" => "Schema description",
        "--new-scenario" => "Scenario scaffold",
        "--agent-guide" => "Agent guide",
        "--quick-start" => "Quick-start",
        "--validate-inputs" => "Input validation",
        "--ingest" => "Input ingest",
        "--import-demand" => "Operational-demand import",
        "--generation-information" => "Generation-information import",
        "--epw-report" => "EPW report",
        _ => "Command",
    };
}
