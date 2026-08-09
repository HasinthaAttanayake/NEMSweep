using NEM.Model.Series;
using NEM.Model.Units;

namespace NEM.Model.Grid;

/// <summary>Expands data-centre nameplate into a flat demand flow.</summary>
public static class DataCentreDemand
{
    /// <summary>
    /// Data centre load added to a region, modelled as a flat draw across the whole
    /// scenario period.
    ///
    /// Two assumptions are folded into this expansion and must be read together:
    /// Energy - annual energy is nameplate x LoadFactor x hours in period.
    /// Shape - that energy is spread evenly across every interval, not
    /// concentrated into a duty cycle.
    ///
    /// LoadFactor is 1.0: data centre load is modelled at full nameplate in every
    /// hour. Real fleets sit nearer 0.6-0.8, so storage and cost results are an
    /// upper bound with respect to this parameter. Stated in nemsim-assumptions.md;
    /// see the limitations page (NEM-048).
    /// </summary>
    public const double LoadFactor = 1.0;

    public static FlowSeries Expand(
        Power nameplate,
        DateTimeOffset start,
        TimeSpan resolution,
        int intervalCount)
    {
        if (nameplate < Power.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nameplate),
                nameplate.Megawatts,
                "Data-centre nameplate cannot be negative.");
        }

        NemTime.Require(start, nameof(start));
        if (intervalCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalCount),
                intervalCount,
                "Data-centre demand must contain at least one interval.");
        }

        return new FlowSeries(
            start,
            resolution,
            Enumerable.Repeat(nameplate.Megawatts * LoadFactor, intervalCount).ToArray());
    }
}