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
        return Run(context, CliSettings.LoadScenario(path), resultsPath);
    }

    private static int Run(CliContext context, ScenarioSettings settings, string resultsPath)
    {
        DispatchResultsDTO result = ScenarioRunner.Run(settings, context.Paths.SolutionRoot);
        DispatchResultsExport.WriteJson(result, resultsPath);
        context.Output.WriteLine(
            $"Dispatched {result.DataSeries.Demand.TotalDemandMw.Length} hourly intervals for "
            + $"{result.Scenario.Region}.");
        context.Output.WriteLine(
            $"Wrote scenario results to: {Path.GetFullPath(resultsPath)}");
        return 0;
    }
}