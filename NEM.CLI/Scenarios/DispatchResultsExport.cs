using NEM.Contracts;
using NEM.CLI.Demand;
using NEM.CLI.Infrastructure;
using NEM.CLI.Weather;
using NEM.Model.Economics;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.StorageSizing;
using DomainScenario = NEM.Model.Scenarios.Scenario;

namespace NEM.CLI.Scenarios;

/// <summary>Everything a dispatch-results artifact is written from.</summary>
internal sealed record DispatchExportRequest(
    OperationalDemandData DemandData,
    DispatchInputArtifactDTO DemandInput,
    DispatchInputArtifactDTO WeatherInput,
    WeatherBasisDTO WeatherBasis,
    DomainScenario Scenario,
    StorageSizingRunResult SizingResult,
    StorageSizingOptions SizingOptions,
    string? ReliabilityStandardName,
    PowerSystemCostBreakdown CostBreakdown,
    string? RegionId = null);

internal sealed record DispatchPublicationRequest(
    ScenarioDispatchResult Dispatch,
    StorageSizingOptions SizingOptions,
    string? ReliabilityStandardName,
    string? RegionFileNamePrefix = null);

internal sealed record DispatchPublication(
    SystemDispatchResultsDTO System,
    IReadOnlyDictionary<string, RegionDispatchResultsDTO> Regions);

internal static class DispatchResultsExport
{
    public static void WritePublication(
        DispatchPublicationRequest request,
        string resultsPath,
        Action<string, string>? writeText = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsPath);
        writeText ??= File.WriteAllText;

        DispatchPublication publication = CreatePublication(request);
        string finalResultsPath = Path.GetFullPath(resultsPath);
        string outputDirectory = Path.GetDirectoryName(finalResultsPath)
            ?? throw new InvalidOperationException("Results path has no directory.");
        string stagingDirectory = Path.Combine(
            outputDirectory,
            $".dispatch-results-{Guid.NewGuid():N}");
        string backupDirectory = Path.Combine(stagingDirectory, "previous");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            string systemPath = Path.Combine(stagingDirectory, "results.json");
            WriteText(publication.System, systemPath, writeText);
            foreach ((string fileName, RegionDispatchResultsDTO result) in publication.Regions)
            {
                WriteText(result, Path.Combine(stagingDirectory, fileName), writeText);
            }

            var targets = new List<(string Staged, string Final)>
            {
                (systemPath, finalResultsPath),
            };
            targets.AddRange(publication.Regions.Select(region =>
                (Path.Combine(stagingDirectory, region.Key), Path.Combine(outputDirectory, region.Key))));
            Directory.CreateDirectory(backupDirectory);
            var backups = new List<(string Backup, string Final)>();
            try
            {
                foreach ((_, string finalPath) in targets)
                {
                    if (File.Exists(finalPath))
                    {
                        string backupPath = Path.Combine(backupDirectory, Path.GetFileName(finalPath));
                        File.Move(finalPath, backupPath);
                        backups.Add((backupPath, finalPath));
                    }
                }

                foreach ((string stagedPath, string finalPath) in targets)
                {
                    File.Move(stagedPath, finalPath);
                }
            }
            catch
            {
                foreach ((_, string finalPath) in targets)
                {
                    if (File.Exists(finalPath))
                    {
                        File.Delete(finalPath);
                    }
                }

                foreach ((string backupPath, string finalPath) in backups)
                {
                    if (File.Exists(backupPath))
                    {
                        File.Move(backupPath, finalPath);
                    }
                }

                throw;
            }
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private static DispatchPublication CreatePublication(DispatchPublicationRequest request)
    {
        ScenarioDispatchResult dispatch = request.Dispatch;
        StorageSizingRunResult sizingResult = dispatch.SizingResult;
        SystemDispatchOutcome systemOutcome = SystemDispatchOutcome.Create(
            sizingResult.PowerSystem,
            sizingResult.Regions.Select(region => region.DispatchOutcome).ToArray());
        SystemReliabilityAssessment systemReliability = SystemReliabilityAssessment.Create(
            systemOutcome,
            request.SizingOptions.TargetUsePercentage);
        string runId = Guid.NewGuid().ToString("N");
        var regions = new Dictionary<string, RegionDispatchResultsDTO>(StringComparer.OrdinalIgnoreCase);
        var summaries = new Dictionary<string, RegionDispatchSummaryDTO>(StringComparer.OrdinalIgnoreCase);
        var sourcesByRegion = new Dictionary<string, DispatchSourcesDTO>(StringComparer.OrdinalIgnoreCase);

        foreach (Region region in dispatch.PowerSystem.Regions)
        {
            string regionId = region.RegionId;
            RegionalSizingResult regionalSizing = sizingResult.Regions.Single(
                result => string.Equals(
                    result.DispatchOutcome.RegionId,
                    regionId,
                    StringComparison.OrdinalIgnoreCase));
            RegionCostBreakdown regionalCost = dispatch.CostBreakdown.Regions.Single(
                cost => string.Equals(cost.RegionId, regionId, StringComparison.OrdinalIgnoreCase));
            DispatchInputArtifactDTO demandInput = dispatch.DemandInputs[regionId].Artifact;
            DispatchInputArtifactDTO weatherInput = dispatch.WeatherInputs[regionId].Artifact;
            OperationalDemandData demandData = dispatch.DemandInputs[regionId].Value;
            DispatchSourcesDTO sources = new(
                demandInput,
                weatherInput,
                WeatherBasis.Create(dispatch.WeatherInputs[regionId].Value),
                demandData.SourceArchives.ToArray());
            DispatchResultsDTO regionalLegacy = Create(new DispatchExportRequest(
                demandData,
                demandInput,
                weatherInput,
                sources.WeatherBasis,
                dispatch.Scenario,
                sizingResult,
                request.SizingOptions,
                request.ReliabilityStandardName,
                dispatch.CostBreakdown,
                regionId));
            DispatchCostDTO cost = CreateCost(regionalCost);
            RegionDispatchResultsDTO detail = new(
                ArtifactSchemaVersions.RegionDispatchResults,
                runId,
                regionId,
                regionalLegacy.Scenario.PeriodStart,
                regionalLegacy.Scenario.PeriodEnd,
                regionalLegacy.Scenario.Resolution,
                sources,
                regionalLegacy.PowerSystem,
                regionalLegacy.DataSeries,
                regionalLegacy.Metrics,
                regionalLegacy.Reliability,
                regionalLegacy.StorageSizing,
                cost);
            string detailPath = $"{request.RegionFileNamePrefix ?? "results-"}{regionId.ToLowerInvariant()}.json";
            regions.Add(detailPath, detail);
            sourcesByRegion.Add(regionId, sources);
            summaries.Add(regionId, new RegionDispatchSummaryDTO(
                regionalLegacy.Metrics,
                regionalLegacy.Reliability,
                regionalLegacy.StorageSizing,
                cost,
                detailPath));
        }

        DispatchCostDTO systemCost = CreateCost(dispatch.CostBreakdown);
        SystemDispatchResultsDTO system = new(
            ArtifactSchemaVersions.SystemDispatchResults,
            runId,
            systemOutcome.Start,
            systemOutcome.Start.AddTicks(systemOutcome.Resolution.Ticks * systemOutcome.Length),
            systemOutcome.Resolution,
            dispatch.PowerSystem.Regions.Select(region => region.RegionId).ToArray(),
            sourcesByRegion,
            summaries,
            CreateSystemSeries(systemOutcome, dispatch.PowerSystem),
            CreateMetrics(systemOutcome),
            new ReliabilityBasisDTO(
                systemReliability.TargetUsePercentage,
                systemReliability.AchievedUsePercentage,
                systemReliability.WithinTarget,
                request.ReliabilityStandardName),
            CreateSystemStorageSizingOutcome(request, sizingResult),
            systemCost);
        return new DispatchPublication(system, regions);
    }

    private static void WriteText<T>(T value, string path, Action<string, string> writeText) =>
        writeText(path, JsonFile.Serialize(value));

    private static DispatchSeriesDTO CreateSystemSeries(
        SystemDispatchOutcome outcome,
        PowerSystem powerSystem)
    {
        int length = outcome.Length;
        var baseDemand = new double[length];
        var additive = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        foreach (Region region in powerSystem.Regions)
        {
            AddValues(baseDemand, ValuesOf(region.Demand.BaseDemand));
            foreach (DemandComponent component in region.Demand.AdditiveComponents)
            {
                if (!additive.TryGetValue(component.Name, out double[]? values))
                {
                    values = new double[length];
                    additive.Add(component.Name, values);
                }

                AddValues(values, ValuesOf(component.Demand));
            }
        }

        return new DispatchSeriesDTO(
            new DispatchDemandDTO(
                baseDemand,
                additive,
                ValuesOf(outcome.Demand)),
            outcome.PerFleetDelivered.ToDictionary(
                entry => entry.Key.ToString(),
                entry => ValuesOf(entry.Value)),
            ValuesOf(SumFlows(outcome.PerFleetCurtailment.Values, outcome.Demand)),
            ValuesOf(outcome.Unserved),
            ValuesOf(outcome.Charge),
            ValuesOf(outcome.Discharge),
            outcome.StateOfChargeByTechnology.ToDictionary(
                entry => entry.Key.ToString(),
                entry => ValuesOf(entry.Value)));
    }

    private static DispatchMetricsDTO CreateMetrics(SystemDispatchOutcome outcome)
    {
        ReliabilityMetrics reliability = outcome.Reliability;
        double[] unserved = ValuesOf(outcome.Unserved);
        FlowSeries curtailmentSeries = SumFlows(outcome.PerFleetCurtailment.Values, outcome.Demand);
        double[] curtailment = ValuesOf(curtailmentSeries);
        return new DispatchMetricsDTO(
            outcome.Demand.Integrate().MegawattHours,
            outcome.PerFleetDelivered.Values.Sum(series => series.Integrate().MegawattHours),
            curtailmentSeries.Integrate().MegawattHours,
            reliability.UnservedEnergy.MegawattHours,
            reliability.UnservedEnergyPercentageOfDemand,
            reliability.UnservedHours,
            reliability.HoursServedFraction,
            reliability.PeakUnservedPower.Megawatts,
            new IntervalPointersDTO(
                IndexOfPeak(unserved),
                IndexOfPeak(curtailment),
                IndexOfMinimumStateOfCharge(outcome.StateOfChargeByTechnology, outcome.Length)));
    }

    private static StorageSizingOutcomeDTO CreateSystemStorageSizingOutcome(
        DispatchPublicationRequest request,
        StorageSizingRunResult sizingResult)
    {
        InstalledBatteryAssessment[] installed = sizingResult.InstalledBatteryAssessments.ToArray();
        return new StorageSizingOutcomeDTO(
            OutcomeFor(sizingResult.Status, sizingResult.Regions.Any(region => region.BatterySizing.WasChanged)),
            installed.Sum(assessment => assessment.BatteryCapacity.EnergyCapacity.MegawattHours),
            installed.Sum(assessment => assessment.BatteryCapacity.PowerCapacity.Megawatts),
            sizingResult.Regions.Sum(region => region.BatterySizing.EnergyCapacity.MegawattHours),
            sizingResult.Regions.Sum(region => region.BatterySizing.PowerCapacity.Megawatts),
            request.SizingOptions.MaximumEnergy.MegawattHours,
            request.SizingOptions.MaximumPower.Megawatts,
            sizingResult.DispatchPassCount,
            EvidenceFor(sizingResult.EnergyLimitedAssessment));
    }

    private static StorageSizingOutcome OutcomeFor(StorageSizingStatus status, bool wasChanged) =>
        status switch
        {
            StorageSizingStatus.TargetMet => wasChanged
                ? StorageSizingOutcome.Resized
                : StorageSizingOutcome.NotRequired,
            StorageSizingStatus.EnergyLimited => StorageSizingOutcome.EnergyLimited,
            StorageSizingStatus.StorageNoLongerImprovesReliability =>
                StorageSizingOutcome.StorageNoLongerImprovesReliability,
            StorageSizingStatus.BatteryCapacityLimitReached =>
                StorageSizingOutcome.BatteryCapacityLimitReached,
            StorageSizingStatus.PassLimitReached => StorageSizingOutcome.PassLimitReached,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    private static DispatchCostDTO CreateCost(PowerSystemCostBreakdown cost) =>
        new(
            "calculated",
            cost.TotalAnnualisedGenerationCost.Aud,
            cost.TotalAnnualisedStorageCost.Aud,
            cost.TotalAnnualisedCost.Aud,
            cost.SystemLevelisedCostOfGeneration.AudPerMwhDelivered,
            cost.SystemLevelisedCostOfStorage.AudPerMwhDelivered,
            cost.SystemLevelisedCostOfElectricity.AudPerMwhDelivered);

    private static DispatchCostDTO CreateCost(RegionCostBreakdown cost) =>
        new(
            "calculated",
            cost.AnnualisedGenerationCost.Aud,
            cost.AnnualisedStorageCost.Aud,
            cost.TotalAnnualisedCost.Aud,
            cost.LevelisedCostOfGeneration.AudPerMwhDelivered,
            cost.LevelisedCostOfStorage.AudPerMwhDelivered,
            cost.LevelisedCostOfElectricity.AudPerMwhDelivered);

    private static void AddValues(double[] target, double[] source)
    {
        for (int index = 0; index < target.Length; index++)
        {
            target[index] += source[index];
        }
    }

    private static FlowSeries SumFlows(IEnumerable<FlowSeries> flows, FlowSeries timeline)
    {
        double[] values = new double[timeline.Length];
        foreach (FlowSeries flow in flows)
        {
            for (int index = 0; index < values.Length; index++)
            {
                values[index] += flow[index].Megawatts;
            }
        }

        return new FlowSeries(timeline.Start, timeline.Resolution, values);
    }

    private static int? IndexOfMinimumStateOfCharge(
        IReadOnlyDictionary<StorageTechnology, StockSeries> stateOfCharge,
        int length)
    {
        if (stateOfCharge.Count == 0 || length == 0)
        {
            return null;
        }

        double[] total = new double[length];
        foreach (StockSeries series in stateOfCharge.Values)
        {
            for (int index = 0; index < length; index++)
            {
                total[index] += series[index].MegawattHours;
            }
        }

        int minimumIndex = 0;
        for (int index = 1; index < total.Length; index++)
        {
            if (total[index] < total[minimumIndex])
            {
                minimumIndex = index;
            }
        }

        return minimumIndex;
    }

    public static DispatchResultsDTO Create(DispatchExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        StorageSizingRunResult sizingResult = request.SizingResult;
        PowerSystem powerSystem = sizingResult.PowerSystem;
        RegionalSizingResult regionalSizing = request.RegionId is null
            ? sizingResult.Regions.Single()
            : sizingResult.Regions.Single(result => string.Equals(
                result.DispatchOutcome.RegionId,
                request.RegionId,
                StringComparison.OrdinalIgnoreCase));
        DispatchOutcome outcome = regionalSizing.DispatchOutcome;
        var deliveredGenerationByTechnology = new Dictionary<string, FlowSeries>();
        foreach ((GenerationTechnology technology, FlowSeries availableGeneration) in
                 outcome.PerFleetGeneration.OrderBy(entry => entry.Key))
        {
            /// <summary>
            /// PerFleetDelivered is canonical delivered-to-load; generation minus curtailment also
            /// includes generation booked to storage charging.
            /// </summary>
            FlowSeries deliveredGeneration = outcome.PerFleetDelivered[technology];
            deliveredGenerationByTechnology.Add(technology.ToString(), deliveredGeneration);
        }

        ReliabilityMetrics reliability = outcome.Reliability;
        double deliveredGenerationMwh = deliveredGenerationByTechnology.Values
            .Sum(series => series.Integrate().MegawattHours);
        Region region = powerSystem.Regions.Single(region => region.RegionId == outcome.RegionId);

        return new DispatchResultsDTO(
            ArtifactSchemaVersions.DispatchResults,
            new DispatchScenarioDTO(
                request.Scenario.Id.Value,
                request.Scenario.Name,
                request.DemandData.Region,
                outcome.Demand.Start,
                outcome.Demand.Start.AddTicks(outcome.Demand.Resolution.Ticks * outcome.Demand.Length),
                outcome.Demand.Resolution),
            DateTimeOffset.UtcNow,
            new DispatchSourcesDTO(
                request.DemandInput,
                request.WeatherInput,
                request.WeatherBasis,
                request.DemandData.SourceArchives.ToArray()),
            new DispatchPowerSystemDTO(
                powerSystem.Id.Value,
                region.GeneratingFleets.Select(fleet => new DispatchFleetDTO(
                    fleet.GenerationTechnology.ToString(),
                    fleet.NameplateCapacity.Megawatts)).ToArray(),
                region.StorageFleets.Select(fleet => new DispatchStorageFleetDTO(
                    fleet.StorageTechnology.ToString(),
                    fleet.StorageCapacity.MegawattHours,
                    fleet.PowerCapacity.Megawatts)).ToArray()),
            new DispatchSeriesDTO(
                new DispatchDemandDTO(
                    ValuesOf(region.Demand.BaseDemand),
                    region.Demand.AdditiveComponents.ToDictionary(
                        component => component.Name,
                        component => ValuesOf(component.Demand),
                        StringComparer.OrdinalIgnoreCase),
                    ValuesOf(region.Demand.TotalDemand)),
                deliveredGenerationByTechnology.ToDictionary(
                    entry => entry.Key,
                    entry => ValuesOf(entry.Value)),
                ValuesOf(outcome.Curtailment),
                ValuesOf(outcome.Unserved),
                ValuesOf(outcome.Charge),
                ValuesOf(outcome.Discharge),
                outcome.StateOfChargeByTechnology.ToDictionary(
                    entry => entry.Key.ToString(),
                    entry => ValuesOf(entry.Value))),
            new DispatchMetricsDTO(
                outcome.Demand.Integrate().MegawattHours,
                deliveredGenerationMwh,
                outcome.Curtailment.Integrate().MegawattHours,
                reliability.UnservedEnergy.MegawattHours,
                reliability.UnservedEnergyPercentageOfDemand,
                reliability.UnservedHours,
                reliability.HoursServedFraction,
                reliability.PeakUnservedPower.Megawatts,
                CreateIntervalPointers(outcome)),
            new ReliabilityBasisDTO(
                request.SizingOptions.TargetUsePercentage,
                reliability.UnservedEnergyPercentageOfDemand,
                regionalSizing.MeetsTarget,
                request.ReliabilityStandardName),
            CreateStorageSizingOutcome(request, regionalSizing),
            new DispatchCostDTO(
                "calculated",
                request.CostBreakdown.TotalAnnualisedGenerationCost.Aud,
                request.CostBreakdown.TotalAnnualisedStorageCost.Aud,
                request.CostBreakdown.TotalAnnualisedCost.Aud,
                request.CostBreakdown.SystemLevelisedCostOfGeneration.AudPerMwhDelivered,
                request.CostBreakdown.SystemLevelisedCostOfStorage.AudPerMwhDelivered,
                request.CostBreakdown.SystemLevelisedCostOfElectricity.AudPerMwhDelivered));
    }

    public static void WriteJson(DispatchResultsDTO result, string path)
        => JsonFile.Write(result, path);

    private static StorageSizingOutcomeDTO CreateStorageSizingOutcome(
        DispatchExportRequest request,
        RegionalSizingResult regionalSizing)
    {
        InstalledBatteryAssessment installed = request.SizingResult.InstalledBatteryAssessments
            .Single(assessment => string.Equals(
                assessment.BatteryCapacity.RegionId,
                regionalSizing.BatterySizing.RegionId,
                StringComparison.OrdinalIgnoreCase));
        return new StorageSizingOutcomeDTO(
            OutcomeFor(regionalSizing),
            installed.BatteryCapacity.EnergyCapacity.MegawattHours,
            installed.BatteryCapacity.PowerCapacity.Megawatts,
            regionalSizing.BatterySizing.EnergyCapacity.MegawattHours,
            regionalSizing.BatterySizing.PowerCapacity.Megawatts,
            request.SizingOptions.MaximumEnergy.MegawattHours,
            request.SizingOptions.MaximumPower.Megawatts,
            request.SizingResult.DispatchPassCount,
            EvidenceFor(request.SizingResult.EnergyLimitedAssessment));
    }

    private static StorageSizingOutcome OutcomeFor(RegionalSizingResult regionalSizing) =>
        regionalSizing.Status switch
        {
            StorageSizingStatus.TargetMet => regionalSizing.BatterySizing.WasChanged
                ? StorageSizingOutcome.Resized
                : StorageSizingOutcome.NotRequired,
            StorageSizingStatus.EnergyLimited => StorageSizingOutcome.EnergyLimited,
            StorageSizingStatus.StorageNoLongerImprovesReliability =>
                StorageSizingOutcome.StorageNoLongerImprovesReliability,
            StorageSizingStatus.BatteryCapacityLimitReached =>
                StorageSizingOutcome.BatteryCapacityLimitReached,
            StorageSizingStatus.PassLimitReached => StorageSizingOutcome.PassLimitReached,
            _ => throw new ArgumentOutOfRangeException(nameof(regionalSizing)),
        };

    private static EnergyLimitedEvidenceDTO? EvidenceFor(
        EnergyLimitedAssessment? assessment) =>
        assessment is null
            ? null
            : new EnergyLimitedEvidenceDTO(
                assessment.AvailableEnergy.MegawattHours / 1_000,
                assessment.DemandEnergy.MegawattHours / 1_000,
                assessment.ShortfallEnergy.MegawattHours / 1_000,
                assessment.BindingIntervalIndices.ToArray());

    private static IntervalPointersDTO CreateIntervalPointers(DispatchOutcome outcome) =>
        new(
            IndexOfPeak(ValuesOf(outcome.Unserved)),
            IndexOfPeak(ValuesOf(outcome.Curtailment)),
            IndexOfMinimumStateOfCharge(outcome));

    /// <summary>Index of the largest value in a series, or null when the series never rises above zero.</summary>
    private static int? IndexOfPeak(double[] values)
    {
        int peakIndex = -1;
        double peak = 0;
        for (int index = 0; index < values.Length; index++)
        {
            if (values[index] > peak)
            {
                peak = values[index];
                peakIndex = index;
            }
        }

        return peakIndex < 0 ? null : peakIndex;
    }

    /// <summary>
    /// Index of the lowest total state of charge across every storage technology, or null when the
    /// region has no storage.
    /// </summary>
    private static int? IndexOfMinimumStateOfCharge(DispatchOutcome outcome)
    {
        if (outcome.StateOfChargeByTechnology.Count == 0)
        {
            return null;
        }

        if (outcome.Demand.Length == 0)
        {
            return null;
        }

        double[] total = new double[outcome.Demand.Length];
        foreach (StockSeries series in outcome.StateOfChargeByTechnology.Values)
        {
            for (int index = 0; index < total.Length; index++)
            {
                total[index] += series[index].MegawattHours;
            }
        }

        int minimumIndex = 0;
        for (int index = 1; index < total.Length; index++)
        {
            if (total[index] < total[minimumIndex])
            {
                minimumIndex = index;
            }
        }

        return minimumIndex;
    }

    private static double[] ValuesOf(FlowSeries series)
    {
        var values = new double[series.Length];
        for (int index = 0; index < series.Length; index++)
        {
            values[index] = series[index].Megawatts;
        }

        return values;
    }

    private static double[] ValuesOf(StockSeries series)
    {
        var values = new double[series.Length];
        for (int index = 0; index < series.Length; index++)
        {
            values[index] = series[index].MegawattHours;
        }

        return values;
    }
}
