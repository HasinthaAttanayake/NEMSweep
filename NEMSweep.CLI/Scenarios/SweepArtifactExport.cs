using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NEMSweep.CLI.Application;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.Contracts;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Simulation;

namespace NEMSweep.CLI.Scenarios;

internal static class SweepArtifactExport
{
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
        JsonObject demand = result["dataSeries"]?["demand"]?.AsObject()
            ?? throw new FormatException($"Sweep point result '{pointResultPath}' has no demand series.");
        JsonArray baseDemand = demand["baseDemandMw"]?.AsArray()
            ?? throw new FormatException($"Sweep point result '{pointResultPath}' has no base demand series.");
        JsonObject metadata = result["scenario"]?.AsObject() ?? result;
        DateTimeOffset start = metadata["periodStart"]?.GetValue<DateTimeOffset>()
            ?? throw new FormatException($"Sweep point result '{pointResultPath}' has no period start.");
        string resolutionText = metadata["resolution"]?.GetValue<string>()
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
        var series = new RegularSeriesDTO(
            ArtifactSchemaVersions.RegularSeries,
            start,
            resolution,
            values);
        string seriesJson = JsonFile.Serialize(series);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seriesJson))).ToLowerInvariant();
        string relativePath = $"../series/base-demand-{hash}.json";
        string seriesPath = Path.Combine(sweepDirectory, "series", $"base-demand-{hash}.json");
        if (!File.Exists(seriesPath))
        {
            JsonFile.Write(series, seriesPath);
        }

        demand.Remove("baseDemandMw");
        demand["baseDemandSeriesPath"] = relativePath;
        JsonFile.Write(result, pointResultPath);
        return relativePath;
    }

    /// <summary>
    /// Deletes series files no point references. Series file names are content addressed, so a
    /// change to the series contract or to the demand input leaves the previous file behind and
    /// the published sweep would grow with every regeneration.
    /// </summary>
    public static void PruneUnreferencedSeries(
        string sweepDirectory,
        IEnumerable<string> referencedRelativePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sweepDirectory);
        ArgumentNullException.ThrowIfNull(referencedRelativePaths);
        string seriesDirectory = Path.Combine(sweepDirectory, "series");
        if (!Directory.Exists(seriesDirectory))
        {
            return;
        }

        var referenced = referencedRelativePaths
            .Select(relativePath => Path.GetFullPath(Path.Combine(
                sweepDirectory,
                "points",
                relativePath)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(seriesDirectory, "*.json"))
        {
            if (!referenced.Contains(Path.GetFullPath(path)))
            {
                File.Delete(path);
            }
        }
    }

    public static SweepPointScalarResultsDTO CreateScalars(DispatchResultsDTO result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return CreateScalars(
            result.Metrics,
            result.DataSeries,
            result.PowerSystem.StorageFleets.Sum(fleet => fleet.PowerCapacityMw),
            result.PowerSystem.StorageFleets.Sum(fleet => fleet.EnergyCapacityMwh),
            result.Cost);
    }

    public static SweepPointScalarResultsDTO CreateScalars(
        SystemDispatchResultsDTO result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return CreateScalars(
            result.Metrics,
            result.DataSeries,
            result.StorageSizing.FinalPowerMw,
            result.StorageSizing.FinalEnergyMwh,
            result.Cost);
    }

    public static SweepPointScalarResultsDTO CreateScalars(RegionDispatchResultsDTO result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return CreateScalars(
            result.Metrics,
            result.DataSeries,
            result.PowerSystem.StorageFleets.Sum(fleet => fleet.PowerCapacityMw),
            result.PowerSystem.StorageFleets.Sum(fleet => fleet.EnergyCapacityMwh),
            result.Cost);
    }

    private static SweepPointScalarResultsDTO CreateScalars(
        DispatchMetricsDTO metrics,
        DispatchSeriesDTO dataSeries,
        double storagePowerMw,
        double storageEnergyMwh,
        DispatchCostDTO cost)
    {
        RenewableShareMetrics renewableShare = RenewableShareMetrics.FromDeliveredEnergy(
            dataSeries.DeliveredGenerationByTechnologyMw.ToDictionary(
                entry => ParseTechnology(entry.Key),
                entry => entry.Value.Sum()),
            dataSeries.Demand.BaseDemandMw?.Sum()
                ?? throw new InvalidOperationException(
                    "Sweep scalars require the point's base-demand series."));
        return new SweepPointScalarResultsDTO(
            cost.SlcoeAudPerMwh,
            cost.GenerationSlcoeAudPerMwh,
            cost.StorageSlcoeAudPerMwh,
            metrics.DemandMwh,
            metrics.DemandMwh - metrics.UnservedEnergyMwh,
            metrics.DeliveredGenerationMwh,
            renewableShare.GridScaleShare,
            renewableShare.NativeShare,
            storagePowerMw,
            storageEnergyMwh,
            metrics.UnservedEnergyMwh,
            metrics.UnservedEnergyPercentageOfDemand,
            metrics.UnservedHours,
            metrics.HoursServedFraction,
            metrics.PeakUnservedPowerMw,
            metrics.CurtailedEnergyMwh,
            cost.TransmissionSlcotAudPerMwh,
            cost.TransmissionCostStatus,
            cost.NetImportedEnergyMwh);
    }

    private static GenerationTechnology ParseTechnology(string technology) =>
        Enum.TryParse(technology, ignoreCase: false, out GenerationTechnology parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : throw new FormatException($"Unknown generation technology '{technology}' in dispatch results.");

    /// <summary>
    /// The scope every succeeded point shares, or null when the points do not agree on one period
    /// and resolution. Regions accumulate, so a multi-region sweep states every region it covers.
    /// </summary>
    public static SweepScopeDTO? CreateScope(IReadOnlyCollection<SystemDispatchResultsDTO> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0)
        {
            return null;
        }

        SystemDispatchResultsDTO first = results.First();
        WeatherBasisDTO weatherBasis = first.DataSourcesByRegion.Values
            .OrderBy(source => source.DemandInput.FileName, StringComparer.Ordinal)
            .First().WeatherBasis;
        if (results.Any(result => result.PeriodStart != first.PeriodStart
            || result.PeriodEnd != first.PeriodEnd
            || result.Resolution != first.Resolution
            || result.DataSourcesByRegion.Values
                .OrderBy(source => source.DemandInput.FileName, StringComparer.Ordinal)
                .First().WeatherBasis != weatherBasis))
        {
            return null;
        }

        return new SweepScopeDTO(
            results.SelectMany(result => result.RegionIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(regionId => regionId, StringComparer.Ordinal)
                .ToArray(),
            first.PeriodStart,
            first.PeriodEnd,
            first.Resolution,
            weatherBasis);
    }

    /// <summary>
    /// Rewrites the manifest of published sweeps from what is on disk, so adding, renaming or
    /// deleting a sweep is reflected without a hand-maintained list.
    /// </summary>
    public static void WriteManifest(string sweepsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sweepsDirectory);
        var entries = new List<SweepManifestEntryDTO>();
        foreach (string indexPath in Directory
            .EnumerateFiles(sweepsDirectory, "index.json", SearchOption.AllDirectories)
            .Where(path => !string.Equals(
                Path.GetDirectoryName(path),
                sweepsDirectory,
                StringComparison.Ordinal)))
        {
            SweepIndexDTO? index;
            try
            {
                index = JsonSerializer.Deserialize<SweepIndexDTO>(
                    File.ReadAllBytes(indexPath),
                    JsonFile.ReadOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (index is null || string.IsNullOrWhiteSpace(index.SweepId))
            {
                continue;
            }

            entries.Add(new SweepManifestEntryDTO(
                index.SweepId,
                index.Name,
                Path.GetRelativePath(sweepsDirectory, indexPath).Replace('\\', '/')));
        }

        JsonFile.Write(
            new SweepManifestDTO(
                ArtifactSchemaVersions.SweepManifest,
                entries.OrderBy(entry => entry.SweepId, StringComparer.Ordinal).ToArray()),
            Path.Combine(sweepsDirectory, "index.json"));
    }

    /// <summary>
    /// Describes a failed point by stage and code so failures can be grouped without matching on
    /// message text. Failures the runner did not attribute are reported as unknown rather than
    /// guessed at.
    /// </summary>
    public static SweepPointFailureDTO CreateFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is ScenarioRunException failure
            ? new SweepPointFailureDTO(failure.Stage, failure.Code, failure.Message)
            : new SweepPointFailureDTO(
                SweepFailureStage.Unknown,
                "unhandled",
                exception.Message);
    }

    public static SweepProvenanceDTO CreateProvenance(
        CliContext context,
        SweepDefinition definition,
        string definitionPath,
        IEnumerable<string> configPaths,
        SweepRunMetadata runMetadata)
    {
        // Genuine inputs only. The emitted per-point configs are outputs of the fan-out and are
        // reachable from each point's configPath, so listing them here would grow the provenance
        // block with the point count without adding a fact.
        var inputs = new Dictionary<string, SweepInputFileDTO>(StringComparer.OrdinalIgnoreCase);
        AddInput(inputs, context.Paths.SolutionRoot, definitionPath, "sweep-definition");
        AddInput(
            inputs,
            context.Paths.SolutionRoot,
            definition.BaselineConfigFullPath(context.Paths),
            "baseline-scenario-config");
        string outputRoot = ScenarioRunner.ResolveOutputRoot(context.Paths);
        foreach (string configPath in configPaths)
        {
            ScenarioSettings? settings;
            try
            {
                settings = ScenarioConfig.Load(configPath);
            }
            catch (FormatException)
            {
                continue;
            }

            foreach (ScenarioRegionSettings region in settings.Regions)
            {
                AddInput(
                    inputs,
                    context.Paths.SolutionRoot,
                    ScenarioRunner.ResolveScenarioInputPath(context.Paths, outputRoot, region.DemandFile),
                    "demand-data");
                AddInput(
                    inputs,
                    context.Paths.SolutionRoot,
                    ScenarioRunner.ResolveScenarioInputPath(context.Paths, outputRoot, region.WeatherFile),
                    "weather-data");
            }
        }

        string resolvedDefinition = JsonFile.SerializeExact(definition);
        return new SweepProvenanceDTO(
            runMetadata.GitCommitSha,
            runMetadata.WorkingTreeDirty,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resolvedDefinition))).ToLowerInvariant(),
            inputs.Values.OrderBy(input => input.Path, StringComparer.Ordinal).ToArray(),
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["dispatchResults"] = ArtifactSchemaVersions.DispatchResults,
                ["operationalDemand"] = ArtifactSchemaVersions.OperationalDemand,
                ["regularSeries"] = ArtifactSchemaVersions.RegularSeries,
                ["sweepDefinition"] = definition.SchemaVersion,
                ["sweepIndex"] = ArtifactSchemaVersions.SweepIndex,
                ["weather"] = ArtifactSchemaVersions.Weather,
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