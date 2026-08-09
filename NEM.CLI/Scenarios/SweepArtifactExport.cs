using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using NEM.CLI.Application;
using NEM.CLI.Configuration;
using NEM.CLI.Infrastructure;
using NEM.Contracts;

namespace NEM.CLI.Scenarios;

internal static class SweepArtifactExport
{
    internal const int IndexSchemaVersion = 1;

    public static SweepRunMetadata CaptureRunMetadata(string solutionRoot)
    {
        string? commitSha = TryRunGit(solutionRoot, "rev-parse", "HEAD");
        string? status = TryRunGit(solutionRoot, "status", "--porcelain");
        return new SweepRunMetadata(commitSha ?? "unavailable", !string.IsNullOrWhiteSpace(status));
    }

    public static string ExternalizeBaseDemand(string pointResultPath, string sweepDirectory)
    {
        JsonObject result = JsonNode.Parse(File.ReadAllBytes(pointResultPath))?.AsObject()
            ?? throw new FormatException($"Sweep point result '{pointResultPath}' is empty.");
        JsonObject scenario = result["scenario"]?.AsObject()
            ?? throw new FormatException($"Sweep point result '{pointResultPath}' has no scenario.");
        JsonObject demand = result["dataSeries"]?["demand"]?.AsObject()
            ?? throw new FormatException($"Sweep point result '{pointResultPath}' has no demand series.");
        JsonArray baseDemand = demand["baseDemandMw"]?.AsArray()
            ?? throw new FormatException($"Sweep point result '{pointResultPath}' has no base demand series.");
        DateTimeOffset start = scenario["periodStart"]?.GetValue<DateTimeOffset>()
            ?? throw new FormatException($"Sweep point result '{pointResultPath}' has no period start.");
        string resolutionText = scenario["resolution"]?.GetValue<string>()
            ?? throw new FormatException($"Sweep point result '{pointResultPath}' has no resolution.");
        if (!TimeSpan.TryParseExact(
                resolutionText,
                "c",
                CultureInfo.InvariantCulture,
                out TimeSpan resolution))
        {
            throw new FormatException($"Sweep point result '{pointResultPath}' has an invalid resolution.");
        }
        double[] values = baseDemand.Select(value => value?.GetValue<double>()
            ?? throw new FormatException($"Sweep point result '{pointResultPath}' has a null base demand value."))
            .ToArray();
        string seriesJson = JsonFile.Serialize(new RegularSeriesDTO(start, resolution, values));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seriesJson))).ToLowerInvariant();
        string relativePath = $"../series/base-demand-{hash}.json";
        string seriesPath = Path.Combine(sweepDirectory, "series", $"base-demand-{hash}.json");
        if (!File.Exists(seriesPath))
        {
            JsonFile.Write(new RegularSeriesDTO(start, resolution, values), seriesPath);
        }

        demand.Remove("baseDemandMw");
        demand["baseDemandSeriesPath"] = relativePath;
        JsonFile.Write(result, pointResultPath);
        return relativePath;
    }

    public static SweepPointScalarResultsDTO CreateScalars(DispatchResultsDTO result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new SweepPointScalarResultsDTO(
            result.Cost.SlcoeAudPerMwh,
            result.Cost.GenerationSlcoeAudPerMwh,
            result.Cost.StorageSlcoeAudPerMwh,
            result.Metrics.DemandMwh - result.Metrics.UnservedEnergyMwh,
            null,
            null,
            result.PowerSystem.StorageFleets.Sum(fleet => fleet.PowerCapacityMw),
            result.PowerSystem.StorageFleets.Sum(fleet => fleet.EnergyCapacityMwh),
            result.Metrics.UnservedEnergyMwh,
            result.Metrics.UnservedEnergyPercentageOfDemand,
            result.Metrics.CurtailedEnergyMwh);
    }

    public static SweepProvenanceDTO CreateProvenance(
        CliContext context,
        SweepDefinition definition,
        string definitionPath,
        IEnumerable<string> configPaths,
        SweepRunMetadata runMetadata)
    {
        var inputs = new Dictionary<string, SweepInputFileDTO>(StringComparer.OrdinalIgnoreCase);
        AddInput(inputs, context.Paths.SolutionRoot, definitionPath, "sweep-definition");
        AddInput(
            inputs,
            context.Paths.SolutionRoot,
            definition.BaselineConfigFullPath(context.Paths),
            "baseline-scenario-config");
        foreach (string configPath in configPaths)
        {
            AddInput(inputs, context.Paths.SolutionRoot, configPath, "emitted-scenario-config");
            ScenarioSettings? settings;
            try
            {
                settings = CliSettings.LoadScenario(configPath);
            }
            catch (FormatException)
            {
                continue;
            }

            AddInput(
                inputs,
                context.Paths.SolutionRoot,
                context.Paths.ResolveConfiguredPath(settings.DemandFile),
                "demand-data");
            AddInput(
                inputs,
                context.Paths.SolutionRoot,
                context.Paths.ResolveConfiguredPath(settings.WeatherFile),
                "weather-data");
        }

        string resolvedDefinition = JsonFile.SerializeExact(definition);
        return new SweepProvenanceDTO(
            runMetadata.GitCommitSha,
            runMetadata.WorkingTreeDirty,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resolvedDefinition))).ToLowerInvariant(),
            inputs.Values.OrderBy(input => input.Path, StringComparer.Ordinal).ToArray(),
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["dispatchResults"] = 4,
                ["operationalDemand"] = 2,
                ["sweepDefinition"] = definition.SchemaVersion,
                ["sweepIndex"] = IndexSchemaVersion,
                ["weather"] = 5,
            });
    }

    private static void AddInput(
        IDictionary<string, SweepInputFileDTO> inputs,
        string solutionRoot,
        string path,
        string purpose)
    {
        byte[] contents = File.ReadAllBytes(path);
        string relativePath = Path.GetRelativePath(solutionRoot, path).Replace('\\', '/');
        string key = $"{purpose}:{relativePath}";
        inputs[key] = new SweepInputFileDTO(
            relativePath,
            purpose,
            Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant());
    }

    private static string? TryRunGit(string solutionRoot, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = solutionRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start git.");
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException)
        {
            return null;
        }
    }
}

internal sealed record SweepRunMetadata(string GitCommitSha, bool WorkingTreeDirty);