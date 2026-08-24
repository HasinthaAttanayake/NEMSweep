using NEMSweep.Contracts;

namespace NEMSweep.Web.Services.Insights;

/// <summary>
/// The whole-system facts an analysis reads, without the interval series that carry them.
/// </summary>
/// <remarks>
/// The producer publishes the same system result twice: <c>results-overview.json</c> at about 19 KB
/// and <c>results.json</c> at about 2 MB, the difference being a dozen 8,760-element series. Every
/// integrated figure the comparison page states is in the smaller one, so the page reads that and
/// fetches the series only for the views that plot them. Both artifacts project onto this shape so
/// the analysis is written once rather than once per artifact.
/// </remarks>
public sealed record SystemFacts(
    string RunId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    TimeSpan Resolution,
    string[] RegionIds,
    IReadOnlyDictionary<string, DispatchSourcesDTO> DataSourcesByRegion,
    IReadOnlyDictionary<string, RegionDispatchSummaryDTO> RegionSummariesById,
    DispatchMetricsDTO Metrics,
    ReliabilityBasisDTO Reliability,
    StorageSizingOutcomeDTO StorageSizing,
    DispatchCostDTO Cost,
    DispatchTopologyDTO Topology)
{
    public static SystemFacts From(SystemDispatchOverviewDTO overview)
    {
        ArgumentNullException.ThrowIfNull(overview);
        return new SystemFacts(
            overview.RunId,
            overview.PeriodStart,
            overview.PeriodEnd,
            overview.Resolution,
            overview.RegionIds ?? [],
            overview.DataSourcesByRegion ?? [],
            overview.RegionSummariesById ?? [],
            overview.Metrics,
            overview.Reliability,
            overview.StorageSizing,
            overview.Cost,
            overview.Topology);
    }

    public static SystemFacts From(SystemDispatchResultsDTO result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new SystemFacts(
            result.RunId,
            result.PeriodStart,
            result.PeriodEnd,
            result.Resolution,
            result.RegionIds ?? [],
            result.DataSourcesByRegion ?? [],
            result.RegionSummariesById ?? [],
            result.Metrics,
            result.Reliability,
            result.StorageSizing,
            result.Cost,
            result.Topology);
    }
}
