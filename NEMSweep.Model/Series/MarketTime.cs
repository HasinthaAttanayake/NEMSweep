namespace NEMSweep.Model.Series;

/// <summary>
/// Market time: the one fixed UTC offset a modelled market runs on, with no daylight
/// saving, for every region and all year. The NEM runs on AEST (UTC+10) and that is the
/// default; a run may declare any fixed offset instead, but every series and period bound
/// within one run must share it. Never infer an offset from the machine locale.
/// </summary>
public static class MarketTime
{
    /// <summary>Inclusive lower bound on a market-time offset.</summary>
    public static readonly TimeSpan MinimumOffset = TimeSpan.FromHours(-12);

    /// <summary>Inclusive upper bound on a market-time offset.</summary>
    public static readonly TimeSpan MaximumOffset = TimeSpan.FromHours(14);

    private static readonly long GranularityTicks = TimeSpan.FromMinutes(15).Ticks;

    /// <summary>
    /// Whether <paramref name="offset"/> is a usable market-time offset: within
    /// [<see cref="MinimumOffset"/>, <see cref="MaximumOffset"/>] and a whole number of
    /// quarter-hours. Every real single-timezone market's offset qualifies; a machine locale that
    /// happens to sit on a quarter-hour still does, which is why cross-series consistency (every
    /// series in a run carrying the <em>same</em> offset) is the load-bearing check, enforced where
    /// series are combined and on <see cref="Scenarios.Scenario"/>'s period bounds.
    /// </summary>
    public static bool IsValidOffset(TimeSpan offset) =>
        offset >= MinimumOffset
        && offset <= MaximumOffset
        && offset.Ticks % GranularityTicks == 0;

    /// <summary>Requires <paramref name="instant"/>'s offset to satisfy <see cref="IsValidOffset"/>.</summary>
    public static void Require(DateTimeOffset instant, string paramName)
    {
        if (!IsValidOffset(instant.Offset))
        {
            throw new ArgumentException(
                $"Timestamps must carry a fixed market-time offset within [-12:00, +14:00] at "
                + $"quarter-hour granularity; got {instant.Offset}.",
                paramName);
        }
    }
}
