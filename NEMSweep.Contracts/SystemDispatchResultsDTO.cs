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
    [property: JsonRequired] DispatchEmissionsDTO Emissions,
    [property: JsonRequired] DispatchTopologyDTO Topology,
    [property: JsonRequired] DispatchInterconnectorDTO[] Interconnectors,
    DispatchCostBasisDTO? CostBasis = null,
    DispatchModelProvenanceDTO? Provenance = null);

/// <summary>
/// The model build that produced a result. <see cref="DispatchSourcesDTO"/> already pins the input
/// bytes a run consumed, but without the commit a result published by hand cannot say which version
/// of the model read them. Absent when the binary was built outside a checkout and so carries no
/// commit to report.
/// </summary>
/// <param name="GitCommitSha">Commit the model was built at, stamped into the binary by its build.</param>
/// <param name="WorkingTreeDirty">
/// True when the run was made from a checkout standing on <see cref="GitCommitSha"/> that had
/// uncommitted changes. False when the run was made from a binary built elsewhere, where the source
/// tree is not present to inspect.
/// </param>
public sealed record DispatchModelProvenanceDTO(
    [property: JsonRequired] string GitCommitSha,
    [property: JsonRequired] bool WorkingTreeDirty);

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
    [property: JsonRequired] DispatchEmissionsDTO Emissions,
    [property: JsonRequired] Dictionary<string, double> DeliveredGenerationByTechnologyMwh,
    [property: JsonRequired] string DetailPath,
    [property: JsonRequired] string OverviewPath);