using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CsvHelper;
using NEMSweep.CLI.Application;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Scenarios;

internal static class SweepRunCommand
{
    public static int Run(CliContext context, string definitionPath)
    {
        var runStopwatch = Stopwatch.StartNew();
        SweepRunMetadata runMetadata = SweepArtifactExport.CaptureRunMetadata(
            context.Paths.WorkingRoot,
            context.Paths.OutputRoot);
        (SweepDefinition definition, JsonNode baseline) =
            SweepFanOutCommand.LoadDefinitionAndBaseline(context, definitionPath);
        IReadOnlyList<SweepPoint> selectedPoints = SelectPoints(definition, context.Points);
        bool partialRun = selectedPoints.Count != definition.Points.Length;
        string configOutputDirectory = SweepFanOutCommand.ConfigOutputDirectory(context, definition);
        string sweepDirectory = context.Paths.OutputPath(Path.Combine("sweeps", definition.SweepId));
        string pointsDirectory = Path.Combine(sweepDirectory, "points");
        // Both created before the first point runs: the per-point failure handler cleans up inside
        // them, so a point that fails before anything has written to either would otherwise take
        // the whole run down from inside the handler meant to contain it, which is the abort that
        // recording per-point failures exists to prevent. Nothing else creates them until a point gets far
        // enough to dispatch, so the first point failing on a sweep id that has never been
        // published is enough to hit this.
        Directory.CreateDirectory(pointsDirectory);
        Directory.CreateDirectory(Path.Combine(sweepDirectory, "configs"));
        bool csvDimensionsWritten = false;
        var failedPointIds = new List<string>();
        var indexPoints = new List<SweepIndexPointDTO>();
        var configPaths = new List<string>();
        var succeededResults = new List<SystemDispatchResultsDTO>();
        var referencedSeriesPaths = new List<string>();

        foreach (SweepPoint point in selectedPoints)
        {
            string axisValue = point.AxisValue.ToString("G17", CultureInfo.InvariantCulture);
            string publishedConfigPath = Path.Combine(
                sweepDirectory,
                "configs",
                $"{point.PointId}.json");
            string configPath = Path.Combine(configOutputDirectory, $"{point.PointId}.json");
            string resultPath = Path.Combine(pointsDirectory, $"{point.PointId}.json");
            string statusPath = Path.Combine(pointsDirectory, $"{point.PointId}.status.json");

            context.Output.WriteLine(
                $"Running sweep point {point.PointId} ({definition.Axis.Label}={axisValue} {definition.Axis.Unit}).");
            int referencedSeriesPathCount = referencedSeriesPaths.Count;
            var pointStopwatch = Stopwatch.StartNew();
            try
            {
                // Generated here, inside the per-point try, so a malformed override for one point
                // is recorded as that point's failure rather than aborting every other point in an
                // unattended run. The generated config is not validated here: ScenarioCommand.Run
                // loads it through the same validation below and already attributes a schema failure
                // to the input stage, so validating twice would only cost a second parse, and would
                // reject the config before it is published, leaving the index citing a file that was
                // never written.
                WritePointConfig(definition, baseline, configOutputDirectory, point);
                configPaths.Add(configPath);
                // Published before dispatch, so a point that fails while running is still published
                // beside the exact config it failed on.
                File.Copy(configPath, publishedConfigPath, overwrite: true);

                ScenarioCommand.Run(
                    context,
                    configPath,
                    resultPath,
                    $"{point.PointId}-",
                    ScenarioCommand.ToProvenance(runMetadata),
                    // Per point, because a sweep's combined generation fact would run to millions of
                    // rows and a spreadsheet truncates past ~1,048,576 silently. A folder of
                    // identically shaped tables recombines in any tool that wants the whole thing.
                    Path.Combine(sweepDirectory, "csv", "points", point.PointId),
                    point.PointId,
                    // Dimensions are shared across the study, so only the first point to get this far
                    // writes them; repeating the calendar per point would be megabytes of identical rows.
                    csvDimensionsWritten ? null : Path.Combine(sweepDirectory, "csv"));
                csvDimensionsWritten = context.Csv;
                pointStopwatch.Stop();
                SystemDispatchResultsDTO systemResult = JsonSerializer.Deserialize<SystemDispatchResultsDTO>(
                    File.ReadAllBytes(resultPath),
                    JsonFile.ReadOptions)
                    ?? throw new FormatException($"Sweep point '{point.PointId}' result is empty.");
                var regionScalars = new List<SweepPointRegionScalarsDTO>();
                var regionDetails = new List<SweepPointRegionDetailDTO>();
                foreach (string regionId in systemResult.RegionIds.Order(StringComparer.Ordinal))
                {
                    RegionDispatchSummaryDTO summary = systemResult.RegionSummariesById.GetValueOrDefault(regionId)
                        ?? throw new FormatException(
                            $"Sweep point '{point.PointId}' has no summary for region '{regionId}'.");
                    string detailPath = summary.DetailPath
                        ?? throw new FormatException(
                            $"Sweep point '{point.PointId}' has no detail path for region '{regionId}'.");
                    string overviewPath = summary.OverviewPath
                        ?? throw new FormatException(
                            $"Sweep point '{point.PointId}' has no overview path for region '{regionId}'.");
                    string regionalResultPath = Path.Combine(pointsDirectory, detailPath);
                    RegionDispatchResultsDTO regionalResult = JsonSerializer.Deserialize<RegionDispatchResultsDTO>(
                        File.ReadAllBytes(regionalResultPath),
                        JsonFile.ReadOptions)
                        ?? throw new FormatException(
                            $"Sweep point '{point.PointId}' regional result for '{regionId}' is empty.");
                    SweepPointScalarResultsDTO regionalScalars =
                        SweepArtifactExport.CreateScalars(regionalResult, summary.StorageCapacity);
                    string seriesPath = ExternalizeBaseDemand(
                        point.PointId,
                        regionalResultPath,
                        sweepDirectory);
                    if (!string.Equals(regionalResult.RegionId, regionId, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new FormatException(
                            $"Sweep point '{point.PointId}' regional result '{detailPath}' identifies region "
                            + $"'{regionalResult.RegionId}' instead of '{regionId}'.");
                    }

                    referencedSeriesPaths.Add(seriesPath);
                    regionScalars.Add(new SweepPointRegionScalarsDTO(
                        regionId,
                        regionalScalars));
                    regionDetails.Add(new SweepPointRegionDetailDTO(
                        regionId,
                        $"points/{detailPath}",
                        $"points/{overviewPath}"));
                }

                referencedSeriesPaths.Add(ExternalizeBaseDemand(point.PointId, resultPath, sweepDirectory));
                succeededResults.Add(systemResult);
                JsonFile.Write(
                    new SweepPointStatusFile(
                        point.PointId,
                        point.AxisValue,
                        SweepPointStatus.Succeeded,
                        null,
                        systemResult.Reliability.WithinTarget),
                    statusPath);
                indexPoints.Add(new SweepIndexPointDTO(
                    point.PointId,
                    point.Label,
                    point.AxisValue,
                    SweepPointStatus.Succeeded,
                    $"points/{point.PointId}.json",
                    $"configs/{point.PointId}.json",
                    SweepArtifactExport.CreateScalars(systemResult),
                    systemResult.Reliability,
                    systemResult.StorageSizing,
                    systemResult.Metrics.IntervalPointers,
                    null,
                    regionScalars.ToArray(),
                    regionDetails.ToArray(),
                    $"points/{point.PointId}-overview.json",
                    pointStopwatch.Elapsed.TotalMilliseconds,
                    systemResult.ExecutionMode));
                context.Output.WriteLine($"Sweep point {point.PointId}: succeeded.");
            }
            catch (Exception exception)
            {
                pointStopwatch.Stop();
                referencedSeriesPaths.RemoveRange(
                    referencedSeriesPathCount,
                    referencedSeriesPaths.Count - referencedSeriesPathCount);
                File.Delete(resultPath);
                foreach (string regionalPath in Directory.GetFiles(pointsDirectory, $"{point.PointId}-*.json"))
                {
                    File.Delete(regionalPath);
                }

                // A point whose overrides could not be merged never generated a config, so anything
                // still sitting at the published path belongs to an earlier run. Leaving it there
                // would have the index cite a config this point never ran against.
                if (!File.Exists(configPath))
                {
                    File.Delete(publishedConfigPath);
                }
                SweepPointFailureDTO failure = SweepArtifactExport.CreateFailure(exception);
                JsonFile.Write(
                    new SweepPointStatusFile(
                        point.PointId,
                        point.AxisValue,
                        SweepPointStatus.Failed,
                        failure),
                    statusPath);
                failedPointIds.Add(point.PointId);
                indexPoints.Add(new SweepIndexPointDTO(
                    point.PointId,
                    point.Label,
                    point.AxisValue,
                    SweepPointStatus.Failed,
                    null,
                    $"configs/{point.PointId}.json",
                    null,
                    null,
                    null,
                    null,
                    failure,
                    DurationMs: pointStopwatch.Elapsed.TotalMilliseconds));
                (context.Error ?? TextWriter.Null).WriteLine(
                    $"Sweep point {point.PointId}: failed: {failure.Message} "
                    + $"(stage {failure.Stage}, code {failure.Code})");
            }
        }

        string resolvedDefinitionPath = context.Paths.ResolveConfiguredPath(definitionPath);
        SweepProvenanceDTO provenance = SweepArtifactExport.CreateProvenance(
            context,
            definition,
            resolvedDefinitionPath,
            configPaths,
            runMetadata) with
        {
            TotalDurationMs = runStopwatch.Elapsed.TotalMilliseconds,
        };
        // A partial run publishes the point artifacts it produced and nothing that speaks for the
        // sweep as a whole. Rewriting the index from a subset would drop every point this run did
        // not touch, and the results site reads that index; leaving it stale and saying so is the
        // lesser of the two wrongs, and the full rerun that restores it is one command.
        if (partialRun)
        {
            if (context.Csv)
            {
                string csvDirectory = Path.Combine(sweepDirectory, "csv");
                foreach (string pointId in failedPointIds)
                {
                    string factsDirectory = Path.Combine(csvDirectory, "points", pointId);
                    if (Directory.Exists(factsDirectory))
                    {
                        Directory.Delete(factsDirectory, recursive: true);
                    }
                }

                WritePartialPointDimension(indexPoints, csvDirectory);
            }

            context.Output.WriteLine(
                $"Partial run of {selectedPoints.Count} of {definition.Points.Length} points. "
                + "index.json, the sweep manifest and the summary files were left as they were; "
                + "rerun without --points to republish them.");
            SweepSummary.WriteTable(context.Output, definition, indexPoints);
            return ExitCode(context, definition, failedPointIds, indexPoints);
        }

        JsonFile.Write(
            new SweepIndexDTO(
                ArtifactSchemaVersions.SweepIndex,
                definition.SweepId,
                definition.Name,
                new SweepAxisDTO(definition.Axis.Label, definition.Axis.Unit),
                SweepArtifactExport.CreateScope(succeededResults),
                provenance,
                indexPoints.ToArray()),
            Path.Combine(sweepDirectory, "index.json"));
        SweepArtifactExport.WriteManifest(context.Paths.OutputPath("sweeps"));
        if (context.Csv)
        {
            string csvDirectory = Path.Combine(sweepDirectory, "csv");
            // Not gated on any point succeeding: a rerun where every point now fails is exactly the
            // case where last run's facts are still on disk and would be read as this run's.
            StarSchemaExport.PruneStalePointFacts(
                indexPoints
                    .Where(point => point.Status == SweepPointStatus.Succeeded)
                    .Select(point => point.PointId),
                csvDirectory);
            if (csvDimensionsWritten)
            {
                // Written last, because it describes the points as they finished: a point that
                // failed is in the index with its status, and a consumer joining facts to it sees
                // the same story.
                StarSchemaExport.WritePointDimension(indexPoints, csvDirectory);
            }
        }
        // Only once the new index is on disk: a series file is still referenced by the previously
        // published index until that index is replaced.
        SweepArtifactExport.PruneUnreferencedSeries(sweepDirectory, referencedSeriesPaths);
        SweepSummary.Write(sweepDirectory, definition, indexPoints);
        SweepSummary.WriteTable(context.Output, definition, indexPoints);

        if (failedPointIds.Count > 0)
        {
            (context.Error ?? TextWriter.Null).WriteLine(
                $"Sweep {definition.SweepId} completed with failed points: "
                + $"{string.Join(", ", failedPointIds)}.");
        }
        else
        {
            context.Output.WriteLine($"Sweep {definition.SweepId} completed.");
        }

        return ExitCode(context, definition, failedPointIds, indexPoints);
    }

    private static void WritePartialPointDimension(
        IReadOnlyCollection<SweepIndexPointDTO> points,
        string csvDirectory)
    {
        string path = Path.Combine(csvDirectory, "dim_point.csv");
        var selected = points.Select(point => point.PointId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retained = new System.Text.StringBuilder();
        if (File.Exists(path))
        {
            using var reader = new StreamReader(path);
            using var parser = new CsvParser(reader, CultureInfo.InvariantCulture);
            parser.Read(); // Skip the header; the exporter supplies it.
            while (parser.Read())
            {
                if (!selected.Contains(parser.Record![0]))
                {
                    retained.Append(parser.RawRecord);
                }
            }
        }

        StarSchemaExport.WritePointDimension(points, csvDirectory, (file, text) =>
        {
            int headerEnd = text.IndexOf('\n') + 1;
            File.WriteAllText(file, text[..headerEnd] + retained + text[headerEnd..]);
        });
    }

    /// <summary>
    /// A failed point is a failed run and has always exited 1. A point that ran and missed its
    /// reliability target is a result, not a failure, so it only changes the exit code when the
    /// caller opted in with <c>--fail-on-unmet</c>.
    /// </summary>
    private static int ExitCode(
        CliContext context,
        SweepDefinition definition,
        List<string> failedPointIds,
        List<SweepIndexPointDTO> indexPoints)
    {
        if (failedPointIds.Count > 0)
        {
            return 1;
        }

        if (!context.FailOnUnmet)
        {
            return 0;
        }

        string[] unmet = indexPoints
            .Where(point => point.Reliability is { WithinTarget: false })
            .Select(point => point.PointId)
            .ToArray();
        if (unmet.Length == 0)
        {
            return 0;
        }

        (context.Error ?? TextWriter.Null).WriteLine(
            $"Sweep {definition.SweepId}: reliability target not met at points: "
            + $"{string.Join(", ", unmet)}.");
        return 1;
    }

    /// <summary>
    /// Narrows a definition's points to the ones <c>--points</c> named, in the definition's own
    /// order so a selection cannot reorder the axis. An ID that is not in the definition is an
    /// error: running the points that did match and staying quiet about a typo would publish a
    /// different set of results than the caller asked for.
    /// </summary>
    private static IReadOnlyList<SweepPoint> SelectPoints(
        SweepDefinition definition,
        IReadOnlyList<string>? requestedPointIds)
    {
        if (requestedPointIds is null)
        {
            return definition.Points;
        }

        var requested = new HashSet<string>(requestedPointIds, StringComparer.Ordinal);
        SweepPoint[] selected = definition.Points
            .Where(point => requested.Contains(point.PointId))
            .ToArray();
        string[] unknown = requestedPointIds
            .Where(id => !definition.Points.Any(point =>
                string.Equals(point.PointId, id, StringComparison.Ordinal)))
            .ToArray();
        return unknown.Length == 0
            ? selected
            : throw new UsageException(
                $"Sweep '{definition.SweepId}' has no point {string.Join(", ", unknown)}. "
                + $"Its points are: {string.Join(", ", definition.Points.Select(point => point.PointId))}.");
    }

    /// <summary>Generates one point's scenario config, attributing a merge-patch failure to the
    /// input stage so it surfaces as that point's failure rather than an unhandled crash.</summary>
    private static void WritePointConfig(
        SweepDefinition definition,
        JsonNode baseline,
        string outputDirectory,
        SweepPoint point)
    {
        try
        {
            _ = SweepFanOutCommand.WritePointConfig(
                definition,
                baseline,
                outputDirectory,
                point,
                validate: false);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            throw new ScenarioRunException(
                SweepFailureStage.Input,
                "invalidConfig",
                exception.Message,
                exception);
        }
    }

    /// <summary>
    /// Moves the point's base-demand series into the shared series directory and reads the point
    /// back. Failures here are export failures: the point itself ran.
    /// </summary>
    private static string ExternalizeBaseDemand(
        string pointId,
        string resultPath,
        string sweepDirectory)
    {
        try
        {
            string seriesPath = SweepArtifactExport.ExternalizeBaseDemand(resultPath, sweepDirectory);
            return seriesPath;
        }
        catch (Exception exception) when (exception
            is FormatException or IOException or JsonException)
        {
            throw new ScenarioRunException(
                SweepFailureStage.Export,
                "pointArtifactUnusable",
                exception.Message,
                exception);
        }
    }

    /// <summary>Per-point status sidecar, written next to each point's detail artifact.</summary>
    /// <param name="PointId">The point this status describes.</param>
    /// <param name="AxisValue">Where that point sits on the sweep's axis.</param>
    /// <param name="Status">Whether the run itself completed.</param>
    /// <param name="Failure">Why it did not, when it did not.</param>
    /// <param name="ReliabilityTargetMet">
    /// Whether the point met its reliability target, or <see langword="null"/> when it never got
    /// far enough to have one. <see cref="Status"/> answers a different question: a point that
    /// dispatched a full period and missed its target is <c>succeeded</c>, which has been read as
    /// the target having been met.
    /// </param>
    private sealed record SweepPointStatusFile(
        string PointId,
        double AxisValue,
        SweepPointStatus Status,
        SweepPointFailureDTO? Failure,
        bool? ReliabilityTargetMet = null);
}