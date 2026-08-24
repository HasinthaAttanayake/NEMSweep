using System.Text.Json.Serialization;

namespace NEMSweep.Contracts;

/// <summary>
/// Whole-system dispatch evidence. Series values are interval values in MW or MWh as named;
/// integrated metrics use MWh, reliability values are percentages of demand, and cost values use
/// AUD or AUD/MWh as named. <see cref="RegionIds"/> defines the regions represented by every
/// region-keyed member and preserves the system's deterministic region order.
/// </summary>
public sealed record SystemDispatchResultsDTO(
    int SchemaVersion,
    string RunId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    TimeSpan Resolution,
    string[] RegionIds,
    Dictionary<string, DispatchSourcesDTO> DataSourcesByRegion,
    Dictionary<string, RegionDispatchSummaryDTO> RegionSummariesById,
    DispatchSeriesDTO DataSeries,
    DispatchMetricsDTO Metrics,
    ReliabilityBasisDTO Reliability,
    StorageSizingOutcomeDTO StorageSizing,
    DispatchCostDTO Cost,
    [property: JsonRequired] DispatchTopologyDTO Topology,
    [property: JsonRequired] DispatchInterconnectorDTO[] Interconnectors,
    DispatchCostBasisDTO? CostBasis = null);

/// <summary>Declared directed network topology for a whole-system dispatch result.</summary>
public sealed record DispatchTopologyDTO(
    [property: JsonRequired] string[] RegionIds,
    [property: JsonRequired] DispatchTopologyLinkDTO[] Links);

/// <summary>A stable directed link in the declared system network.</summary>
public sealed record DispatchTopologyLinkDTO(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string FromRegionId,
    [property: JsonRequired] string ToRegionId,
    [property: JsonRequired] double CapacityMw);

/// <summary>Summary evidence for one region within a system dispatch run.</summary>
public sealed record RegionDispatchSummaryDTO(
    DispatchMetricsDTO Metrics,
    ReliabilityBasisDTO Reliability,
    StorageSizingOutcomeDTO StorageSizing,
    DispatchCostDTO Cost,
    [property: JsonRequired] Dictionary<string, double> DeliveredGenerationByTechnologyMwh,
    [property: JsonRequired] string DetailPath,
    [property: JsonRequired] string OverviewPath);