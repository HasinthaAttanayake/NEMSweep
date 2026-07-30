using NEM.CLI.Demand;
using NEM.CLI.Generation;
using NEM.CLI.Infrastructure;
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
        _context = new CliContext(paths, settingsDirectory, output);
        _error = error;
    }

    public int Run(string[] args)
    {
        try
        {
            return args switch
            {
                ["--run-scenario"] => ScenarioCommand.Run(_context),
                ["--generation-information", var path] =>
                    GenerationInformationCommand.Run(_context, path),
                ["--epw-report", var path] => EpwCommands.WriteReport(_context, path),
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
        _error.WriteLine("  NEM.CLI --run-scenario");
        _error.WriteLine("  NEM.CLI --generation-information <workbook.xlsx>");
        _error.WriteLine("  NEM.CLI --epw-report|--epw-series|--epw-validate|--epw-gaps|--epw-rows|--epw-header <file.epw>");
        _error.WriteLine("  NEM.CLI [demand-output.json]");
        return 2;
    }

    private static string OperationName(string[] args) => args.FirstOrDefault() switch
    {
        "--run-scenario" => "Scenario run",
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