using NEM.CLI.Application;
using NEM.Contracts;

namespace NEM.CLI.Scenarios;

internal static class ScenarioCommand
{
    public static int Run(CliContext context)
    {
        var settings = context.LoadSettings().Scenario;
        DispatchResultsDTO result = ScenarioRunner.Run(settings, context.Paths.SolutionRoot);
        DispatchResultsExport.WriteJson(result, context.Paths.DispatchResultsPath);
        context.Output.WriteLine(
            $"Dispatched {result.DataSeries.DemandMw.Length} hourly intervals for "
            + $"{result.Scenario.Region}.");
        context.Output.WriteLine(
            $"Wrote scenario results to: {Path.GetFullPath(context.Paths.DispatchResultsPath)}");
        return 0;
    }
}