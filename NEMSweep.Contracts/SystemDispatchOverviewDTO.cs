using System.Text.Json.Serialization;

namespace NEMSweep.Contracts;

/// <summary>
/// Compact whole-system dispatch facts for overview consumers. Unlike
/// <see cref="SystemDispatchResultsDTO"/>, this artifact deliberately contains no interval series.
/// </summary>
public sealed record SystemDispatchOverviewDTO(
    int SchemaVersion,
    string RunId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    TimeSpan Resolution,
    string[] RegionIds,
    Dictionary<string, DispatchSourcesDTO> DataSourcesByRegion,
    Dictionary<string, RegionDispatchSummaryDTO> RegionSummariesById,
    DispatchMetricsDTO Metrics,
    ReliabilityBasisDTO Reliability,
    StorageSizingOutcomeDTO StorageSizing,
    DispatchCostDTO Cost,
    [property: JsonRequired] DispatchTopologyDTO Topology);