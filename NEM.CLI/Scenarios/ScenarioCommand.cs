using System.Text.Json;
using NEM.CLI.Application;
using NEM.CLI.Configuration;
using NEM.Contracts;

namespace NEM.CLI.Scenarios;

internal static class ScenarioCommand
{
    public static int Run(CliContext context)
    {
        string path = context.Paths.ResolveConfiguredPath(context.LoadSettings().DefaultScenarioPath);
        return RunPublication(context, path);
    }

    public static int Run(CliContext context, string scenarioConfigPath)
    {
        string path = context.Paths.ResolveConfiguredPath(scenarioConfigPath);
        return RunPublication(context, path);
    }

    public static int Run(CliContext context, string scenarioConfigPath, string resultsPath)
    {
        string path = context.Paths.ResolveConfiguredPath(scenarioConfigPath);
        return Run(context, LoadScenario(path), resultsPath);
    }

    public static int Run(
        CliContext context,
        string scenarioConfigPath,
        string resultsPath,
        string regionFileNamePrefix)
    {
        string path = context.Paths.ResolveConfiguredPath(scenarioConfigPath);
        return RunPublication(context, LoadScenario(path), resultsPath, regionFileNamePrefix);
    }

    /// <summary>Reads a scenario config, attributing any failure to the input stage.</summary>
    private static ScenarioSettings LoadScenario(string path)
    {
        try
        {
            return ScenarioConfig.Load(path);
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

    private static int RunPublication(CliContext context, string scenarioConfigPath)
    {
        ScenarioSettings settings = LoadScenario(scenarioConfigPath);
        ScenarioDispatchResult dispatch = ScenarioRunner.RunForPublication(
            settings,
            context.Paths.SolutionRoot);
        StorageSizingSettings sizing = settings.StorageSizing;
        var sizingOptions = new NEM.Model.StorageSizing.StorageSizingOptions(
            NEM.Model.Units.Power.FromMegawatts(sizing.MaximumPowerMw),
            NEM.Model.Units.Energy.FromMegawattHours(sizing.MaximumEnergyMwh),
            sizing.TargetUsePercentage,
            sizing.MaximumPasses);
        DispatchPublication publication;
        try
        {
            publication = DispatchResultsExport.WritePublication(
                new DispatchPublicationRequest(
                    dispatch,
                    sizingOptions,
                    sizing.ReliabilityStandardName),
                context.Paths.DispatchResultsPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ScenarioRunException(
                SweepFailureStage.Export,
                "resultsUnwritable",
                exception.Message,
                exception);
        }

        context.Output.WriteLine(
            $"Dispatched {dispatch.SizingResult.Regions[0].DispatchOutcome.Demand.Length} hourly intervals for "
            + $"{string.Join(", ", dispatch.PowerSystem.Regions.Select(region => region.RegionId))}.");
        context.Output.WriteLine(
            $"Wrote scenario results to: {Path.GetFullPath(context.Paths.DispatchResultsPath)}");
        if (!publication.System.Reliability.WithinTarget)
        {
            context.Output.WriteLine(
                "WARNING: reliability target not met "
                + $"(achieved {publication.System.Reliability.AchievedUsePercentageOfDemand:F4}% unserved energy, "
                + $"target {publication.System.Reliability.TargetUsePercentageOfDemand:F4}%).");
        }

        return 0;
    }

    private static int RunPublication(
        CliContext context,
        ScenarioSettings settings,
        string resultsPath,
        string regionFileNamePrefix)
    {
        ScenarioDispatchResult dispatch = ScenarioRunner.RunForPublication(
            settings,
            context.Paths.SolutionRoot);
        StorageSizingSettings sizing = settings.StorageSizing;
        var sizingOptions = new NEM.Model.StorageSizing.StorageSizingOptions(
            NEM.Model.Units.Power.FromMegawatts(sizing.MaximumPowerMw),
            NEM.Model.Units.Energy.FromMegawattHours(sizing.MaximumEnergyMwh),
            sizing.TargetUsePercentage,
            sizing.MaximumPasses);
        DispatchPublication publication;
        try
        {
            publication = DispatchResultsExport.WritePublication(
                new DispatchPublicationRequest(
                    dispatch,
                    sizingOptions,
                    sizing.ReliabilityStandardName,
                    regionFileNamePrefix),
                resultsPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ScenarioRunException(
                SweepFailureStage.Export,
                "resultsUnwritable",
                exception.Message,
                exception);
        }

        if (!publication.System.Reliability.WithinTarget)
        {
            context.Output.WriteLine(
                "WARNING: reliability target not met "
                + $"(achieved {publication.System.Reliability.AchievedUsePercentageOfDemand:F4}% unserved energy, "
                + $"target {publication.System.Reliability.TargetUsePercentageOfDemand:F4}%).");
        }

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