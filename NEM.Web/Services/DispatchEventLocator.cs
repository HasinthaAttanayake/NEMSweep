using NEM.Contracts;

namespace NEM.Web.Services;

/// <summary>
/// An interval of the dispatch period worth navigating to, named by what makes it notable.
/// </summary>
/// <param name="Key">Stable identifier, used for element ids and test assertions.</param>
/// <param name="Label">What the reader is being offered.</param>
/// <param name="Index">Interval offset from the start of the period.</param>
/// <param name="Instant">The instant that interval begins.</param>
public sealed record DispatchEvent(string Key, string Label, int Index, DateTimeOffset Instant);

/// <summary>
/// Turns the artifact's interval pointers into navigable dates.
/// <para>
/// The model publishes these indices, so nothing is searched for here. A null pointer means the
/// run never experienced that event — a run with no unserved energy has no peak unserved interval
/// — and the corresponding jump is simply not offered.
/// </para>
/// </summary>
public static class DispatchEventLocator
{
    public static IReadOnlyList<DispatchEvent> Locate(DispatchResultsDTO result)
    {
        ArgumentNullException.ThrowIfNull(result);

        IntervalPointersDTO? pointers = result.Metrics.IntervalPointers;
        if (pointers is null)
        {
            return [];
        }

        int intervalCount = result.DataSeries.Demand.TotalDemandMw?.Length ?? 0;
        var events = new List<DispatchEvent>();
        Add(events, "peak-unserved", "Peak unserved demand", pointers.PeakUnservedIntervalIndex, result, intervalCount);
        Add(events, "peak-curtailment", "Peak curtailment", pointers.PeakCurtailmentIntervalIndex, result, intervalCount);
        Add(events, "lowest-storage", "Lowest state of charge", pointers.MinimumStateOfChargeIntervalIndex, result, intervalCount);
        return events;
    }

    private static void Add(
        List<DispatchEvent> events,
        string key,
        string label,
        int? index,
        DispatchResultsDTO result,
        int intervalCount)
    {
        // An index outside the period would send the date filter somewhere the run does not cover.
        if (index is null || index < 0 || (intervalCount > 0 && index >= intervalCount))
        {
            return;
        }

        events.Add(new DispatchEvent(key, label, index.Value, InstantAt(result, index.Value)));
    }

    private static DateTimeOffset InstantAt(DispatchResultsDTO result, int index) =>
        result.Scenario.PeriodStart.AddTicks(result.Scenario.Resolution.Ticks * index);
}
