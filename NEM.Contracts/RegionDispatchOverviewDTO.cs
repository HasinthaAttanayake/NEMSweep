namespace NEM.Contracts;

/// <summary>
/// Compact per-region dispatch facts for overview consumers. Unlike
/// <see cref="RegionDispatchResultsDTO"/>, this artifact deliberately contains no interval series.
/// </summary>
public sealed record RegionDispatchOverviewDTO(
    int SchemaVersion,
    string RunId,
    string RegionId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    TimeSpan Resolution,
    DispatchSourcesDTO DataSources,
    DispatchPowerSystemDTO PowerSystem,
    DispatchMetricsDTO Metrics,
    ReliabilityBasisDTO Reliability,
    StorageSizingOutcomeDTO StorageSizing,
    DispatchCostDTO Cost,
    Dictionary<string, double> DeliveredGenerationByTechnologyMwh,
    double TransmissionLossesMwh);
