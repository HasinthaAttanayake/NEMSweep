using System.Globalization;
using System.Text.Json;
using NEM.CLI.Application;
using NEM.CLI.Configuration;
using NEM.CLI.Infrastructure;
using NEM.Contracts;

namespace NEM.CLI.Scenarios;

internal static class SweepRunCommand
{
    public static int Run(CliContext context, string definitionPath)
    {
        SweepRunMetadata runMetadata = SweepArtifactExport.CaptureRunMetadata(context.Paths.SolutionRoot);
        SweepDefinition definition = SweepFanOutCommand.WriteConfigs(
            context,
            definitionPath,
            validateGeneratedConfigs: false);
        string sweepDirectory = context.Paths.WebDataPath(Path.Combine("sweeps", definition.SweepId));
        string pointsDirectory = Path.Combine(sweepDirectory, "points");
        var failedPointIds = new List<string>();
        var indexPoints = new List<SweepIndexPointDTO>();
        var configPaths = new List<string>();
        var succeededResults = new List<DispatchResultsDTO>();
        var referencedSeriesPaths = new List<string>();

        foreach (SweepPoint point in definition.Points)
        {
            string axisValue = point.AxisValue.ToString("G17", CultureInfo.InvariantCulture);
            string configPath = Path.Combine(
                context.Paths.SolutionRoot,
                "sweeps",
                definition.SweepId,
                "configs",
                $"{point.PointId}.json");
            configPaths.Add(configPath);
            string publishedConfigPath = Path.Combine(
                sweepDirectory,
                "configs",
                $"{point.PointId}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(publishedConfigPath)!);
            File.Copy(configPath, publishedConfigPath, overwrite: true);
            string resultPath = Path.Combine(pointsDirectory, $"{point.PointId}.json");
            string statusPath = Path.Combine(pointsDirectory, $"{point.PointId}.status.json");

            context.Output.WriteLine(
                $"Running sweep point {point.PointId} ({definition.Axis.Label}={axisValue} {definition.Axis.Unit}).");
            try
            {
                ScenarioCommand.Run(context, configPath, resultPath);
                (DispatchResultsDTO result, string seriesPath) = ExternalizeBaseDemand(
                    point.PointId,
                    resultPath,
                    sweepDirectory);
                referencedSeriesPaths.Add(seriesPath);
                succeededResults.Add(result);
                JsonFile.Write(
                    new SweepPointStatusFile(
                        point.PointId,
                        point.AxisValue,
                        SweepPointStatus.Succeeded,
                        null),
                    statusPath);
                indexPoints.Add(new SweepIndexPointDTO(
                    point.PointId,
                    point.Label,
                    point.AxisValue,
                    SweepPointStatus.Succeeded,
                    $"points/{point.PointId}.json",
                    $"configs/{point.PointId}.json",
                    SweepArtifactExport.CreateScalars(result),
                    result.Reliability,
                    result.StorageSizing,
                    result.Metrics.IntervalPointers,
                    null));
                context.Output.WriteLine($"Sweep point {point.PointId}: succeeded.");
            }
            catch (Exception exception)
            {
                File.Delete(resultPath);
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
                    failure));
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
            runMetadata);
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
        SweepArtifactExport.WriteManifest(context.Paths.WebDataPath("sweeps"));
        // Only once the new index is on disk: a series file is still referenced by the previously
        // published index until that index is replaced.
        SweepArtifactExport.PruneUnreferencedSeries(sweepDirectory, referencedSeriesPaths);

        if (failedPointIds.Count == 0)
        {
            context.Output.WriteLine($"Sweep {definition.SweepId} completed.");
            return 0;
        }

        (context.Error ?? TextWriter.Null).WriteLine(
            $"Sweep {definition.SweepId} completed with failed points: {string.Join(", ", failedPointIds)}.");
        return 1;
    }

    /// <summary>
    /// Moves the point's base-demand series into the shared series directory and reads the point
    /// back. Failures here are export failures: the point itself ran.
    /// </summary>
    private static (DispatchResultsDTO Result, string SeriesPath) ExternalizeBaseDemand(
        string pointId,
        string resultPath,
        string sweepDirectory)
    {
        try
        {
            string seriesPath = SweepArtifactExport.ExternalizeBaseDemand(resultPath, sweepDirectory);
            DispatchResultsDTO result = JsonSerializer.Deserialize<DispatchResultsDTO>(
                File.ReadAllBytes(resultPath),
                JsonFile.ReadOptions)
                ?? throw new FormatException($"Sweep point '{pointId}' result is empty.");
            return (result, seriesPath);
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
    private sealed record SweepPointStatusFile(
        string PointId,
        double AxisValue,
        SweepPointStatus Status,
        SweepPointFailureDTO? Failure);
}