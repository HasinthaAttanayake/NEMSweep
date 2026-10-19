using System.Text.Json;
using NEMSweep.CLI.Application;
using NEMSweep.CLI.Configuration;
using NEMSweep.Contracts;
using NEMSweep.Model.StorageSizing;
using NEMSweep.Model.Units;

namespace NEMSweep.CLI.Scenarios;

internal static class ScenarioCommand
{
    public static int Run(CliContext context)
    {
        string path = context.Paths.ResolveConfiguredPath(context.LoadSettings().DefaultScenarioPath);
        return RunPublication(context, LoadScenario(path), provenance: CaptureProvenance(context));
    }

    public static int Run(CliContext context, string scenarioConfigPath)
    {
        string path = context.Paths.ResolveConfiguredPath(scenarioConfigPath);
        return RunPublication(context, LoadScenario(path), provenance: CaptureProvenance(context));
    }

    /// <summary>
    /// Runs one sweep point. The caller passes the provenance it captured for the whole sweep, so a
    /// fan-out shells out to git once rather than twice per point.
    /// </summary>
    /// <param name="context">The invocation's workspace and settings.</param>
    /// <param name="scenarioConfigPath">Materialised config for this point.</param>
    /// <param name="resultsPath">Where this point's result is written.</param>
    /// <param name="regionFileNamePrefix">Prefix that keeps point region files distinct.</param>
    /// <param name="provenance">Model build captured once for the sweep.</param>
    /// <param name="csvDirectory">Where this point's star schema tables go, when asked for.</param>
    /// <param name="pointId">Identifier stamped on every CSV fact row for this point.</param>
    /// <param name="dimensionDirectory">Where the study's shared dimensions go, on the point that writes them.</param>
    public static int Run(
        CliContext context,
        string scenarioConfigPath,
        string resultsPath,
        string regionFileNamePrefix,
        DispatchModelProvenanceDTO? provenance = null,
        string? csvDirectory = null,
        string? pointId = null,
        string? dimensionDirectory = null)
    {
        string path = context.Paths.ResolveConfiguredPath(scenarioConfigPath);
        return RunPublication(
            context,
            LoadScenario(path),
            resultsPath,
            regionFileNamePrefix,
            provenance,
            csvDirectory,
            pointId,
            dimensionDirectory);
    }

    /// <summary>Reads the model build this run was made from, reporting absence rather than
    /// inventing a value when the binary carries no build commit.</summary>
    private static DispatchModelProvenanceDTO? CaptureProvenance(CliContext context) =>
        ToProvenance(SweepArtifactExport.CaptureRunMetadata(
            context.Paths.WorkingRoot,
            context.Paths.OutputRoot));

    /// <summary>Maps captured run metadata onto the published provenance shape.</summary>
    /// <param name="metadata">Metadata captured for a scenario or sweep run.</param>
    internal static DispatchModelProvenanceDTO? ToProvenance(SweepRunMetadata metadata) =>
        metadata.GitCommitSha == SweepArtifactExport.UnknownCommit
            ? null
            : new DispatchModelProvenanceDTO(metadata.GitCommitSha, metadata.WorkingTreeDirty);

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

    /// <summary>
    /// Runs a scenario and publishes it. The default run writes to the workspace's results path and
    /// reports where it wrote; a sweep point supplies its own path and prefix and stays quiet,
    /// because a fan-out would otherwise print a line per point.
    /// </summary>
    private static int RunPublication(
        CliContext context,
        ScenarioSettings settings,
        string? resultsPath = null,
        string? regionFileNamePrefix = null,
        DispatchModelProvenanceDTO? provenance = null,
        string? csvDirectory = null,
        string? pointId = null,
        string? dimensionDirectory = null)
    {
        ScenarioDispatchResult dispatch = ScenarioRunner.RunDispatch(settings, context.Paths);
        string finalResultsPath = resultsPath ?? context.Paths.DispatchResultsPath;
        DispatchPublication publication;
        try
        {
            publication = DispatchResultsExport.WritePublication(
                new DispatchPublicationRequest(
                    dispatch,
                    settings.StorageSizing is { } sizing
                        ? ScenarioConfig.CreateSizingOptions(sizing, dispatch.Reliability.TargetUsePercentage)
                        : null,
                    dispatch.Reliability.StandardName,
                    regionFileNamePrefix,
                    provenance),
                finalResultsPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ScenarioRunException(
                SweepFailureStage.Export,
                "resultsUnwritable",
                exception.Message,
                exception);
        }

        if (context.Csv)
        {
            // A sweep names its own fact directory and writes the shared dimensions itself, once for
            // the study. A standalone run has no such parent, so it writes both side by side.
            string factDirectory = csvDirectory ?? context.Paths.OutputPath("csv");
            StarSchemaExport.WriteFacts(publication, factDirectory, pointId ?? settings.Id);
            if (csvDirectory is null)
            {
                StarSchemaExport.WriteDimensions(publication, dispatch.PowerSystem, factDirectory);
            }
            else if (dimensionDirectory is not null)
            {
                StarSchemaExport.WriteDimensions(publication, dispatch.PowerSystem, dimensionDirectory);
            }
        }

        if (resultsPath is null)
        {
            context.Output.WriteLine(
                $"Dispatched {dispatch.Dispatch.RegionalOutcomes[0].Demand.Length} hourly intervals for "
                + $"{string.Join(", ", dispatch.PowerSystem.Regions.Select(region => region.RegionId))}.");
            context.Output.WriteLine(
                $"Wrote scenario results to: {Path.GetFullPath(finalResultsPath)}");
        }

        ReportReliability(context, publication);
        // The target is a property of the run, not of whether it completed, so a miss is reported
        // and the run still succeeds unless the caller asked otherwise. Callers that script against
        // the exit code and do not pass the flag keep the behaviour they already have.
        return context.FailOnUnmet && !publication.System.Reliability.WithinTarget ? 1 : 0;
    }

    /// <summary>
    /// States the reliability outcome of every run, met or not. Previously only a miss said
    /// anything, so a run that printed no warning was indistinguishable from one whose reliability
    /// nobody had thought to check: the absence of a line was carrying the result.
    /// </summary>
    /// <remarks>
    /// The percentages are percentages of demand energy, which is the unit the model works in; the
    /// line says so, because a bare <c>0.0241%</c> has been read as a fraction and multiplied by a
    /// hundred more than once.
    /// </remarks>
    private static void ReportReliability(CliContext context, DispatchPublication publication)
    {
        ReliabilityBasisDTO reliability = publication.System.Reliability;
        context.Output.WriteLine(
            $"reliability: achieved {reliability.AchievedUsePercentageOfDemand:F4}% vs target "
            + $"{reliability.TargetUsePercentageOfDemand:F4}% of demand: "
            + (reliability.WithinTarget ? "met" : "NOT met"));
        if (!reliability.WithinTarget)
        {
            // Kept verbatim alongside the line above: callers already grep for it.
            context.Output.WriteLine(
                "WARNING: reliability target not met "
                + $"(achieved {reliability.AchievedUsePercentageOfDemand:F4}% unserved energy, "
                + $"target {reliability.TargetUsePercentageOfDemand:F4}%).");
        }
    }

}