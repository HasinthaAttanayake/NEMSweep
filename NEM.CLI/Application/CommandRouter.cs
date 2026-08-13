using NEM.CLI.Demand;
using NEM.CLI.Generation;
using NEM.CLI.Infrastructure;
using NEM.CLI.Ingest;
using NEM.CLI.Scenarios;
using NEM.CLI.Weather;

namespace NEM.CLI.Application;

internal sealed class CommandRouter
{
    private readonly CliContext _context;
    private readonly TextWriter _error;

    public CommandRouter(
        RepositoryPaths paths,
        string settingsDirectory,
        TextWriter output,
        TextWriter error)
    {
        _context = new CliContext(paths, settingsDirectory, output, error);
        _error = error;
    }

    public int Run(string[] args)
    {
        try
        {
            return args switch
            {
                ["--run-scenario"] => ScenarioCommand.Run(_context),
                ["--run-scenario", var scenarioConfigPath] => ScenarioCommand.Run(_context, scenarioConfigPath),
                ["--fan-out-sweep", var definitionPath] => SweepFanOutCommand.Run(_context, definitionPath),
                ["--run-sweep", var definitionPath] => SweepRunCommand.Run(_context, definitionPath),
                ["--describe-schema", var format] when format is "scenario" or "sweep" =>
                    SchemaDescriptionCommand.Run(_context, format),
                ["--validate-inputs"] => ValidateInputsCommand.Run(_context),
                ["--validate-inputs", var bundlePath] => ValidateInputsCommand.Run(_context, bundlePath),
                ["--ingest"] => IngestCommand.Run(_context),
                ["--ingest", var bundlePath] => IngestCommand.Run(_context, bundlePath),
                ["--generation-information", var path] =>
                    GenerationInformationCommand.Run(_context, path),
                ["--epw-report", var regionId, var solarPath] =>
                    EpwCommands.WriteReport(_context, regionId, solarPath),
                ["--epw-report", var regionId, var solarPath, var windPath] =>
                    EpwCommands.WriteReport(_context, regionId, solarPath, windPath),
                ["--epw-series", var path] => EpwCommands.PrintSeries(_context, path),
                ["--epw-validate", var path] => EpwCommands.Validate(_context, path),
                ["--epw-gaps", var path] => EpwCommands.PrintGaps(_context, path),
                ["--epw-rows", var path] => EpwCommands.PrintRows(_context, path),
                ["--epw-header", var path] => EpwCommands.PrintHeader(_context, path),
                [] => OperationalDemandCommand.Run(_context, _context.Paths.DemandDataPath),
                [var outputPath] when !outputPath.StartsWith('-') =>
                    OperationalDemandCommand.Run(_context, outputPath),
                _ => PrintUsage(),
            };
        }
        catch (Exception exception)
        {
            _error.WriteLine($"{OperationName(args)} failed: {exception.Message}");
            return 1;
        }
    }

    private int PrintUsage()
    {
        _error.WriteLine("Usage:");
        _error.WriteLine("  NEM.CLI --run-scenario [scenario-config.json]");
        _error.WriteLine("  NEM.CLI --fan-out-sweep <sweep-definition.json>");
        _error.WriteLine("  NEM.CLI --run-sweep <sweep-definition.json>");
        _error.WriteLine("  NEM.CLI --describe-schema <scenario|sweep>");
        _error.WriteLine("  NEM.CLI --validate-inputs [input-bundle]");
        _error.WriteLine("  NEM.CLI --ingest [input-bundle]");
        _error.WriteLine("  NEM.CLI --generation-information <workbook.xlsx>");
        _error.WriteLine("  NEM.CLI --epw-report <region> <solar.epw> [wind.epw]");
        _error.WriteLine("  NEM.CLI [demand-output.json]");
        return 2;
    }

    private static string OperationName(string[] args) => args.FirstOrDefault() switch
    {
        "--run-scenario" => "Scenario run",
        "--fan-out-sweep" => "Sweep fan-out",
        "--run-sweep" => "Sweep run",
        "--describe-schema" => "Schema description",
        "--validate-inputs" => "Input validation",
        "--ingest" => "Input ingest",
        "--generation-information" => "Generation-information import",
        "--epw-report" => "EPW report",
        "--epw-series" => "EPW series",
        "--epw-validate" => "EPW validation",
        "--epw-gaps" => "EPW gap analysis",
        "--epw-rows" => "EPW row report",
        "--epw-header" => "EPW header report",
        _ => "Operational-demand import",
    };
}