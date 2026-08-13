using NEM.Contracts;
using NEM.CLI.Demand;
using NEM.CLI.Infrastructure;
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
    PowerSystemCostBreakdown CostBreakdown);

internal static class DispatchResultsExport
{
    public static DispatchResultsDTO Create(DispatchExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        StorageSizingRunResult sizingResult = request.SizingResult;
        PowerSystem powerSystem = sizingResult.PowerSystem;
        RegionalSizingResult regionalSizing = sizingResult.Regions.Single();
        DispatchOutcome outcome = regionalSizing.DispatchOutcome;
        var deliveredGenerationByTechnology = new Dictionary<string, FlowSeries>();
        foreach ((GenerationTechnology technology, FlowSeries availableGeneration) in
                 outcome.PerFleetGeneration.OrderBy(entry => entry.Key))
        {
            FlowSeries deliveredGeneration = availableGeneration.Subtract(
                outcome.PerFleetCurtailment[technology]);
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
