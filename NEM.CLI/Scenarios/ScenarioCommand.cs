using System.Text.Json;
using NEM.CLI.Application;
using NEM.CLI.Configuration;
using NEM.Contracts;

namespace NEM.CLI.Scenarios;

internal static class ScenarioCommand
{
    public static int Run(CliContext context)
    {
        var settings = context.LoadSettings().Scenario;
        return Run(context, settings, context.Paths.DispatchResultsPath);
    }

    public static int Run(CliContext context, string scenarioConfigPath)
    {
        string path = context.Paths.ResolveConfiguredPath(scenarioConfigPath);
        return Run(context, path, context.Paths.DispatchResultsPath);
    }

    public static int Run(CliContext context, string scenarioConfigPath, string resultsPath)
    {
        string path = context.Paths.ResolveConfiguredPath(scenarioConfigPath);
        return Run(context, LoadScenario(path), resultsPath);
    }

    /// <summary>Reads a scenario config, attributing any failure to the input stage.</summary>
    private static ScenarioSettings LoadScenario(string path)
    {
        try
        {
            return CliSettings.LoadScenario(path);
        }
        catch (Exception exception) when (exception
            is FormatException or IOException or JsonException or ArgumentException)
        {
            throw new ScenarioRunException(
                SweepFailureStage.Input,
                exception is IOException ? "configUnreadable" : "invalidConfig",
                exception.Message,
                exception);
        }
    }

    private static int Run(CliContext context, ScenarioSettings settings, string resultsPath)
    {
        DispatchResultsDTO result = ScenarioRunner.Run(settings, context.Paths.SolutionRoot);
        WriteResults(result, resultsPath);
        context.Output.WriteLine(
            $"Dispatched {result.DataSeries.Demand.TotalDemandMw.Length} hourly intervals for "
            + $"{result.Scenario.Region}.");
        context.Output.WriteLine(
            $"Wrote scenario results to: {Path.GetFullPath(resultsPath)}");
        return 0;
    }

    /// <summary>Writes the results artifact, attributing any failure to the export stage.</summary>
    private static void WriteResults(DispatchResultsDTO result, string resultsPath)
    {
        try
        {
            DispatchResultsExport.WriteJson(result, resultsPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ScenarioRunException(
                SweepFailureStage.Export,
                "resultsUnwritable",
                exception.Message,
                exception);
        }
    }
}