using NEMSweep.Contracts;
using NEMSweep.Web.Components;
using NEMSweep.Web.Components.Viz;

namespace NEMSweep.Web.Services.Insights;

/// <summary>
/// Something about a run that can be put on an axis. Wraps the contract's own scalar catalogue and
/// adds the two quantities a reader keeps asking for that the artifact does not publish directly:
/// the sweep's own input value, and the whole annual cost behind a levelised one.
/// </summary>
public sealed record SweepMeasure(
    string Key,
    string Label,
    string Unit,
    string? Prefix,
    Func<SweepRun, double?> Select,
    bool IsDerived = false)
{
    /// <summary>
    /// The contract states shares as fractions. A reader does not read 0.38 as thirty-eight
    /// percent, so a fraction is scaled and relabelled for display while the underlying scalar is
    /// left exactly as the artifact publishes it.
    /// </summary>
    public bool IsFraction => string.Equals(Unit, "fraction", StringComparison.OrdinalIgnoreCase);

    public string DisplayUnit => IsFraction ? "%" : Unit;

    public string AxisTitle =>
        string.IsNullOrWhiteSpace(DisplayUnit) ? Label : $"{Label} ({DisplayUnit})";

    /// <summary>Reads the measure off a run in the units this measure is displayed in.</summary>
    public double? SelectForDisplay(SweepRun run)
    {
        double? value = Select(run);
        return IsFraction && value is { } fraction ? 100 * fraction : value;
    }

    /// <summary>Formats a displayed value the way the site states it elsewhere.</summary>
    public string Format(double value) => IsFraction
        ? $"{value:N1}%"
        : Prefix switch
        {
            "$" => PlotFormat.Money((decimal)value),
            _ => PlotFormat.Compact(value, 2),
        };
}

/// <summary>
/// The measures a sweep can be plotted against. Built per sweep because the input axis label and
/// unit come from the sweep's own index rather than from the contract.
/// </summary>
public static class SweepMeasures
{
    public const string TotalCostKey = "totalAnnualCostAud";
    public const string LevelisedCostKey = "slcoeAudPerMwh";
    public const string RenewableShareKey = "achievedRenewableShareGridScale";
    public const string AxisKey = "sweepAxis";

    /// <summary>
    /// The measures for a sweep at a given scope. The region is part of the signature because the
    /// derived annual cost is the selected scope's cost, and labelling a region's figure as the
    /// system's would overstate it by the size of every other region.
    /// </summary>
    public static IReadOnlyList<SweepMeasure> For(SweepIndexDTO index, string? regionId = null)
    {
        ArgumentNullException.ThrowIfNull(index);

        var measures = new List<SweepMeasure>
        {
            new(
                AxisKey,
                index.Axis.Label,
                index.Axis.Unit,
                null,
                run => run.AxisValue),
            new(
                TotalCostKey,
                regionId is null ? "Annual system cost" : $"Annual cost, {regionId}",
                "AUD",
                "$",
                run => run.TotalAnnualCostAud,
                IsDerived: true),
        };

        foreach (SweepChartYAxis axis in SweepSeriesCatalogue.All)
        {
            measures.Add(new SweepMeasure(
                axis.Key,
                axis.Label,
                axis.Unit,
                axis.ValuePrefix,
                run => axis.SelectValue(run.Scalars)));
        }

        return measures;
    }

    public static SweepMeasure Resolve(IReadOnlyList<SweepMeasure> measures, string? key) =>
        measures.FirstOrDefault(measure => string.Equals(measure.Key, key, StringComparison.Ordinal))
            ?? measures[0];

    /// <summary>
    /// Whether a measure actually moves across the sweep. A trade-off drawn against something
    /// constant is a vertical line, so the default axes are chosen from measures that vary.
    /// </summary>
    public static bool Varies(SweepMeasure measure, IReadOnlyList<SweepRun> runs)
    {
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentNullException.ThrowIfNull(runs);

        double[] values = [.. runs.Select(measure.Select).Where(value => value.HasValue).Select(value => value!.Value)];
        return values.Length > 1 && values.Distinct().Count() > 1;
    }

    /// <summary>
    /// The measure a trade-off should open on for the horizontal axis: renewable share when the
    /// sweep moved it, and the sweep's own input otherwise.
    /// </summary>
    public static SweepMeasure DefaultX(IReadOnlyList<SweepMeasure> measures, IReadOnlyList<SweepRun> runs)
    {
        SweepMeasure renewable = Resolve(measures, RenewableShareKey);
        return Varies(renewable, runs) ? renewable : Resolve(measures, AxisKey);
    }
}
