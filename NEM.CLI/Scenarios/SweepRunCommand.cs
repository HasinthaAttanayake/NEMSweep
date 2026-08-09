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
                SweepArtifactExport.ExternalizeBaseDemand(resultPath, sweepDirectory);
                DispatchResultsDTO result = JsonSerializer.Deserialize<DispatchResultsDTO>(
                    File.ReadAllBytes(resultPath),
                    JsonFile.ReadOptions)
                    ?? throw new FormatException($"Sweep point '{point.PointId}' result is empty.");
                SweepPointScalarResultsDTO scalars = SweepArtifactExport.CreateScalars(result);
                JsonFile.Write(new SweepPointStatus(point.PointId, point.AxisValue, "succeeded", null), statusPath);
                indexPoints.Add(new SweepIndexPointDTO(
                    point.PointId,
                    point.Label,
                    point.AxisValue,
                    "succeeded",
                    $"points/{point.PointId}.json",
                    $"configs/{point.PointId}.json",
                    scalars,
                    null));
                context.Output.WriteLine($"Sweep point {point.PointId}: succeeded.");
            }
            catch (Exception exception)
            {
                File.Delete(resultPath);
                JsonFile.Write(
                    new SweepPointStatus(point.PointId, point.AxisValue, "failed", exception.Message),
                    statusPath);
                failedPointIds.Add(point.PointId);
                indexPoints.Add(new SweepIndexPointDTO(
                    point.PointId,
                    point.Label,
                    point.AxisValue,
                    "failed",
                    null,
                    $"configs/{point.PointId}.json",
                    null,
                    exception.Message));
                (context.Error ?? TextWriter.Null).WriteLine(
                    $"Sweep point {point.PointId}: failed: {exception.Message}");
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
                SweepArtifactExport.IndexSchemaVersion,
                definition.SweepId,
                definition.Name,
                new SweepAxisDTO(definition.Axis.Label, definition.Axis.Unit),
                provenance,
                indexPoints.ToArray()),
            Path.Combine(sweepDirectory, "index.json"));

        if (failedPointIds.Count == 0)
        {
            context.Output.WriteLine($"Sweep {definition.SweepId} completed.");
            return 0;
        }

        (context.Error ?? TextWriter.Null).WriteLine(
            $"Sweep {definition.SweepId} completed with failed points: {string.Join(", ", failedPointIds)}.");
        return 1;
    }

    private sealed record SweepPointStatus(
        string PointId,
        double AxisValue,
        string Status,
        string? Failure);
}