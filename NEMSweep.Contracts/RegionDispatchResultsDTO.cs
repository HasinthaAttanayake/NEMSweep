using System.Text.Json.Serialization;

namespace NEMSweep.Contracts;

/// <summary>
/// Full dispatch evidence for one region. <see cref="RunId"/> must identify the parent system
/// run when this detail is published alongside a <see cref="SystemDispatchResultsDTO"/>; it is
/// retained here so a detached detail artifact cannot be mistaken for another run. Series values
/// are interval values in MW or MWh as named; integrated metrics use MWh, reliability values are
/// percentages of demand, and cost values use AUD or AUD/MWh as named.
/// </summary>
public sealed record RegionDispatchResultsDTO(
    int SchemaVersion,
    string RunId,
    string RegionId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    TimeSpan Resolution,
    DispatchSourcesDTO DataSources,
    DispatchPowerSystemDTO PowerSystem,
    DispatchSeriesDTO DataSeries,
    DispatchMetricsDTO Metrics,
    ReliabilityBasisDTO Reliability,
    StorageSizingOutcomeDTO StorageSizing,
    DispatchCostDTO Cost,
    [property: JsonRequired] DispatchEmissionsDTO Emissions);