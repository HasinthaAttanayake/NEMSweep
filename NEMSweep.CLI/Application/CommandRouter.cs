using System.Reflection;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Demand;
using NEMSweep.CLI.Generation;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.CLI.Ingest;
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
/// ran and failed, <c>2</c> a command line this router could not route. Requesting help is a
/// success, so <c>--help</c> writes usage to standard output and returns <c>0</c>, while an
/// unrecognised command line writes the same usage to standard error and returns <c>2</c>.
/// </remarks>
internal sealed class CommandRouter
{
    private readonly string _settingsDirectory;
    private readonly string _workingRoot;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    /// <summary>Creates a router for one process.</summary>
    /// <param name="settingsDirectory">Directory the settings file is loaded from.</param>
    /// <param name="workingRoot">Base for relative paths, normally the current directory.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    public CommandRouter(
        string settingsDirectory,
        string workingRoot,
        TextWriter output,
        TextWriter error)
    {
        _settingsDirectory = settingsDirectory;
        _workingRoot = workingRoot;
        _output = output;
        _error = error;
    }

    /// <summary>Routes one command line and returns the process exit code.</summary>
    public int Run(string[] args)
    {
        try
        {
            CliOptions options = CliOptions.Parse(args, out string[] commandArgs);

            // Answered without a workspace, so asking how to use the tool, or for a schema, never
            // fails on a settings file the caller has not written yet.
            switch (commandArgs)
            {
                case ["--help"] or ["-h"] or ["--usage"]:
                    return PrintUsage(_output, 0);
                case ["--version"]:
                    return PrintVersion();
                case ["--describe-schema", var schemaFormat] when schemaFormat is "scenario" or "sweep":
                    return SchemaDescriptionCommand.Run(_output, schemaFormat);
            }

            // Checked here rather than inside the handler so a typo is rejected without first
            // reading settings the run was never going to reach.
            if (commandArgs is ["--epw-report", var epwRegionId, ..])
            {
                RequireKnownRegion(epwRegionId);
            }

            // Matched to a handler before the workspace is built, so an unroutable command line is
            // rejected without reading settings it was never going to use.
            Func<CliContext, int>? handler = commandArgs switch
            {
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
                    context => EpwCommands.WriteReport(context, RequireKnownRegion(regionId), solarPath),
                ["--epw-report", var regionId, var solarPath, var windPath] =>
                    context => EpwCommands.WriteReport(
                        context,
                        RequireKnownRegion(regionId),
                        solarPath,
                        windPath),
                _ => null,
            };

            return handler is null ? PrintUsage(_error, 2) : handler(CreateContext(options));
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
        return new CliContext(paths, settings, _output, _error, options.Csv);
    }

    /// <summary>
    /// Rejects a region argument that is not one of the five NEM regions. Region identity is a bare
    /// string throughout the pipeline, so a typo here would otherwise publish a
    /// <c>weather-{typo}.json</c> artifact that nothing ever reads.
    /// </summary>
    private static string RequireKnownRegion(string regionId) =>
        NemRegions.IsKnown(regionId)
            ? regionId
            : throw new ArgumentException(
                $"Region '{regionId}' is not a NEM region. Expected one of: "
                + $"{string.Join(", ", NemRegions.All.Order(StringComparer.Ordinal))}.");

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
        writer.WriteLine();
        writer.WriteLine("  Workspace overrides, accepted alongside any command below:");
        writer.WriteLine("  --data-root <dir>   where inputs are read from  (env NEMSWEEP_DATA_ROOT)");
        writer.WriteLine("  --output <dir>      where results are written   (env NEMSWEEP_OUTPUT)");
        writer.WriteLine("  --csv               also write the star schema CSV tables");
        writer.WriteLine();
        writer.WriteLine("  Scenario and sweep runs:");
        writer.WriteLine("  nemsweep --run-scenario [scenario-config.json]");
        writer.WriteLine("  nemsweep --fan-out-sweep <sweep-definition.json>");
        writer.WriteLine("  nemsweep --run-sweep <sweep-definition.json>");
        writer.WriteLine("  nemsweep --describe-schema <scenario|sweep>");
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
        "--run-scenario" => "Scenario run",
        "--fan-out-sweep" => "Sweep fan-out",
        "--run-sweep" => "Sweep run",
        "--describe-schema" => "Schema description",
        "--validate-inputs" => "Input validation",
        "--ingest" => "Input ingest",
        "--import-demand" => "Operational-demand import",
        "--generation-information" => "Generation-information import",
        "--epw-report" => "EPW report",
        _ => "Command",
    };
}
