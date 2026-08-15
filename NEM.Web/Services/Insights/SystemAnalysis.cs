using NEM.Contracts;
using NEM.Web.Components.Viz;

namespace NEM.Web.Services.Insights;

/// <summary>
/// One region's share of a system run, in the form the site compares regions in. Everything here
/// is copied or integrated from the artifact; nothing is modelled.
/// </summary>
public sealed record RegionProfile(
    string RegionId,
    DispatchMetricsDTO Metrics,
    ReliabilityBasisDTO Reliability,
    StorageSizingOutcomeDTO StorageSizing,
    DispatchCostDTO Cost,
    string? DetailPath,
    EnergyMix Mix)
{
    public string Name => RegionNames.Full(RegionId);

    public string State => RegionNames.State(RegionId);

    public double ServedEnergyMwh => Metrics.DemandMwh - Metrics.UnservedEnergyMwh;

    /// <summary>Positive net imported energy denotes imports, matching the contract.</summary>
    public bool IsNetImporter => Cost.NetImportedEnergyMwh > 0;

    public double NetTradeMwh => Math.Abs(Cost.NetImportedEnergyMwh);

    /// <summary>
    /// Available renewable energy the region could not use, as a share of everything its fleet
    /// could have delivered. A megawatt-hour figure alone does not say whether that is a rounding
    /// error or a sixth of the region's output.
    /// </summary>
    public double CurtailedShareOfAvailable
    {
        get
        {
            double available = Metrics.DeliveredGenerationMwh + Metrics.CurtailedEnergyMwh;
            return available <= 0 ? 0 : Metrics.CurtailedEnergyMwh / available;
        }
    }

    /// <summary>How much of the reliability allowance the region spent, as a fraction of its target.</summary>
    public double ReliabilityAllowanceUsed => Reliability.TargetUsePercentageOfDemand <= 0
        ? 0
        : Reliability.AchievedUsePercentageOfDemand / Reliability.TargetUsePercentageOfDemand;

    public bool WasResized => StorageSizing.Outcome == StorageSizingOutcome.Resized;

    public double StorageGrowthMwh => StorageSizing.FinalEnergyMwh - StorageSizing.InitialEnergyMwh;
}

/// <summary>Energy carried over one interconnector across a dispatch period.</summary>
public sealed record LinkFlow(
    string FromRegionId,
    string ToRegionId,
    double CapacityMw,
    double EnergyMwh,
    double LossesMwh,
    double PeakFlowMw,
    int FlowingIntervals,
    int TotalIntervals,
    double IntervalHours)
{
    public string Label => $"{FromRegionId} to {ToRegionId}";

    public double FlowingShare => TotalIntervals <= 0 ? 0 : (double)FlowingIntervals / TotalIntervals;

    /// <summary>Energy carried as a share of what the link could have carried had it run flat out.</summary>
    /// <remarks>
    /// Both sides of the ratio are energy. The denominator has to include the interval length as
    /// the numerator does, or a two-hour run reports twice the utilisation it achieved and a
    /// half-hourly one reports half.
    /// </remarks>
    public double CapacityFactor => CapacityMw <= 0 || TotalIntervals <= 0 || IntervalHours <= 0
        ? 0
        : EnergyMwh / (CapacityMw * TotalIntervals * IntervalHours);

    public double LossShare => EnergyMwh <= 0 ? 0 : LossesMwh / EnergyMwh;
}

/// <summary>
/// A system dispatch result read as a comparison between its regions. The system artifact already
/// carries every regional summary, so this needs no extra fetch; passing the regional detail
/// artifacts as well adds each region's generation mix.
/// </summary>
public sealed record SystemAnalysis(
    SystemDispatchResultsDTO Result,
    IReadOnlyList<RegionProfile> Regions,
    IReadOnlyList<LinkFlow> Links,
    EnergyMix SystemMix,
    IReadOnlyList<Finding> Findings)
{
    public double ServedEnergyMwh => Result.Metrics.DemandMwh - Result.Metrics.UnservedEnergyMwh;

    /// <summary>
    /// The unweighted mean of the regional levelised costs. Shown only alongside the system figure,
    /// to make the point that the system figure is not this number.
    /// </summary>
    public decimal MeanRegionalSlcoe => Regions.Count == 0
        ? 0
        : Regions.Sum(region => region.Cost.SlcoeAudPerMwh) / Regions.Count;

    public RegionProfile? Cheapest => Regions.Count == 0
        ? null
        : Regions.MinBy(region => region.Cost.SlcoeAudPerMwh);

    public RegionProfile? Dearest => Regions.Count == 0
        ? null
        : Regions.MaxBy(region => region.Cost.SlcoeAudPerMwh);

    public static SystemAnalysis Build(
        SystemDispatchResultsDTO result,
        IReadOnlyDictionary<string, RegionDispatchResultsDTO>? regionDetails = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var regions = new List<RegionProfile>();
        foreach (string regionId in result.RegionIds ?? [])
        {
            if (result.RegionSummariesById?.GetValueOrDefault(regionId) is not { } summary)
            {
                continue;
            }

            RegionDispatchResultsDTO? detail = regionDetails?.GetValueOrDefault(regionId);
            regions.Add(new RegionProfile(
                regionId,
                summary.Metrics,
                summary.Reliability,
                summary.StorageSizing,
                summary.Cost,
                summary.DetailPath,
                EnergyMix.From(detail?.DataSeries, result.Resolution)));
        }

        IReadOnlyList<LinkFlow> links = BuildLinks(result);
        EnergyMix systemMix = EnergyMix.From(result.DataSeries, result.Resolution);
        var analysis = new SystemAnalysis(result, regions, links, systemMix, []);
        return analysis with { Findings = Derive(analysis) };
    }

    private static IReadOnlyList<LinkFlow> BuildLinks(SystemDispatchResultsDTO result)
    {
        double hours = result.Resolution.TotalHours;
        var links = new List<LinkFlow>();
        foreach (DispatchInterconnectorDTO link in result.Interconnectors ?? [])
        {
            double[] flow = link.FlowMw ?? [];
            double[] losses = link.LossesMw ?? [];
            links.Add(new LinkFlow(
                link.FromRegionId,
                link.ToRegionId,
                link.CapacityMw,
                flow.Sum() * hours,
                losses.Sum() * hours,
                flow.Length == 0 ? 0 : flow.Max(),
                flow.Count(value => value > 0),
                flow.Length,
                hours));
        }

        return links;
    }

    /// <summary>
    /// Each finding is guarded by the condition that makes it true, so a run where regions agree,
    /// nothing trades and nothing was resized produces a short list rather than a padded one.
    /// </summary>
    private static IReadOnlyList<Finding> Derive(SystemAnalysis analysis)
    {
        var findings = new List<Finding>();
        AddCostSpread(analysis, findings);
        AddWeightedAverage(analysis, findings);
        AddReliabilityConcentration(analysis, findings);
        AddCurtailmentWithShortfall(analysis, findings);
        AddTrade(analysis, findings);
        AddStorageDivergence(analysis, findings);
        // Highest priority first, so a page showing only the first few shows the ones that matter.
        return [.. findings.OrderByDescending(finding => finding.Priority)];
    }

    private static void AddCostSpread(SystemAnalysis analysis, List<Finding> findings)
    {
        if (analysis.Regions.Count < 2
            || analysis.Cheapest is not { } cheapest
            || analysis.Dearest is not { } dearest
            || cheapest.RegionId == dearest.RegionId)
        {
            return;
        }

        decimal spread = dearest.Cost.SlcoeAudPerMwh - cheapest.Cost.SlcoeAudPerMwh;
        if (spread <= 0)
        {
            return;
        }

        decimal percentage = cheapest.Cost.SlcoeAudPerMwh <= 0
            ? 0
            : 100 * spread / cheapest.Cost.SlcoeAudPerMwh;
        findings.Add(new Finding(
            $"{dearest.State} energy costs {PlotFormat.Money(spread)}/MWh more than {cheapest.State}",
            $"{dearest.Name} levelises at {PlotFormat.Money(dearest.Cost.SlcoeAudPerMwh)}/MWh against "
            + $"{PlotFormat.Money(cheapest.Cost.SlcoeAudPerMwh)}/MWh in {cheapest.Name} — a spread of "
            + $"{percentage:N1}% between two regions of the same run.",
            FindingTone.Neutral,
            PlotFormat.Money(spread),
            "AUD/MWh spread",
            Priority: 95));
    }

    private static void AddWeightedAverage(SystemAnalysis analysis, List<Finding> findings)
    {
        if (analysis.Regions.Count < 2)
        {
            return;
        }

        decimal mean = analysis.MeanRegionalSlcoe;
        decimal system = analysis.Result.Cost.SlcoeAudPerMwh;
        if (Math.Abs(system - mean) < 0.5m)
        {
            return;
        }

        RegionProfile largest = analysis.Regions.MaxBy(region => region.ServedEnergyMwh)!;
        findings.Add(new Finding(
            "The system figure is demand-weighted, not an average of the regions",
            $"System levelised cost is {PlotFormat.Money(system)}/MWh while the plain average of the "
            + $"regional figures is {PlotFormat.Money(mean)}/MWh. {largest.State} serves "
            + $"{PlotFormat.Share(largest.ServedEnergyMwh / Math.Max(1, analysis.ServedEnergyMwh))} of "
            + "system energy, so its cost dominates the system number.",
            FindingTone.Neutral,
            Priority: 60));
    }

    private static void AddReliabilityConcentration(SystemAnalysis analysis, List<Finding> findings)
    {
        double systemUnserved = analysis.Result.Metrics.UnservedEnergyMwh;
        if (systemUnserved <= 0)
        {
            findings.Add(new Finding(
                "Every hour of demand was served",
                $"No region recorded unserved energy against a "
                + $"{analysis.Result.Reliability.TargetUsePercentageOfDemand:G3}% of demand target"
                + ReliabilityStandard(analysis.Result.Reliability) + ".",
                FindingTone.Favourable,
                Priority: 40));
            return;
        }

        RegionProfile[] shortRegions = [.. analysis.Regions
            .Where(region => region.Metrics.UnservedEnergyMwh > 0)
            .OrderByDescending(region => region.Metrics.UnservedEnergyMwh)];
        if (shortRegions.Length == 0)
        {
            return;
        }

        RegionProfile worst = shortRegions[0];
        double share = worst.Metrics.UnservedEnergyMwh / systemUnserved;
        string concentration = shortRegions.Length == 1
            ? $"All of it fell in {worst.Name}"
            : $"{PlotFormat.Share(share)} of it fell in {worst.Name}";
        findings.Add(new Finding(
            $"Unserved energy is concentrated in {worst.State}",
            $"The system left {PlotFormat.Compact(systemUnserved)} MWh unserved across "
            + $"{analysis.Result.Metrics.UnservedHours} "
            + (analysis.Result.Metrics.UnservedHours == 1 ? "hour" : "hours")
            + $". {concentration}, which spent "
            + $"{PlotFormat.Share(worst.ReliabilityAllowanceUsed, 0)} of its reliability allowance "
            + $"against the system's {PlotFormat.Share(SystemAllowanceUsed(analysis), 0)}.",
            worst.Reliability.WithinTarget ? FindingTone.Caution : FindingTone.Constraint,
            PlotFormat.Compact(worst.Metrics.PeakUnservedPowerMw),
            "MW peak shortfall",
            Priority: worst.Reliability.WithinTarget ? 90 : 100));
    }

    private static void AddCurtailmentWithShortfall(SystemAnalysis analysis, List<Finding> findings)
    {
        RegionProfile? spiller = analysis.Regions
            .Where(region => region.Metrics.CurtailedEnergyMwh > 0)
            .MaxBy(region => region.CurtailedShareOfAvailable);
        if (spiller is null)
        {
            return;
        }

        bool alsoShort = spiller.Metrics.UnservedEnergyMwh > 0;
        findings.Add(new Finding(
            alsoShort
                ? $"{spiller.State} spills energy and still runs short"
                : $"{spiller.State} spills the largest share of its available energy",
            $"{PlotFormat.Compact(spiller.Metrics.CurtailedEnergyMwh)} MWh was curtailed in "
            + $"{spiller.Name}, {PlotFormat.Share(spiller.CurtailedShareOfAvailable)} of everything its "
            + "fleet could have delivered"
            + (alsoShort
                ? $", in the same year it left {PlotFormat.Compact(spiller.Metrics.UnservedEnergyMwh)} MWh "
                    + "of demand unserved. Both at once is a timing result, not a capacity one: the energy "
                    + "existed, but not in the hours it was needed."
                : ". Curtailed energy is available generation the fleet had no room for."),
            alsoShort ? FindingTone.Caution : FindingTone.Neutral,
            Priority: alsoShort ? 85 : 55));
    }

    private static void AddTrade(SystemAnalysis analysis, List<Finding> findings)
    {
        LinkFlow? busiest = analysis.Links
            .Where(link => link.EnergyMwh > 0)
            .MaxBy(link => link.EnergyMwh);
        if (busiest is null)
        {
            return;
        }

        findings.Add(new Finding(
            $"{RegionNames.State(busiest.FromRegionId)} underwrites {RegionNames.State(busiest.ToRegionId)}",
            $"{PlotFormat.Compact(busiest.EnergyMwh)} MWh flowed from {busiest.FromRegionId} to "
            + $"{busiest.ToRegionId} over {busiest.FlowingIntervals:N0} intervals — "
            + $"{PlotFormat.Share(busiest.FlowingShare, 0)} of the period — reaching "
            + $"{PlotFormat.Compact(busiest.PeakFlowMw)} MW against a "
            + $"{PlotFormat.Compact(busiest.CapacityMw)} MW limit. "
            + $"{PlotFormat.Compact(busiest.LossesMwh)} MWh was lost in transmission.",
            FindingTone.Neutral,
            PlotFormat.Share(busiest.CapacityFactor, 1),
            "of link capacity used",
            Priority: 50));
    }

    private static void AddStorageDivergence(SystemAnalysis analysis, List<Finding> findings)
    {
        RegionProfile[] resized = [.. analysis.Regions.Where(region => region.WasResized)];
        if (resized.Length == 0 || resized.Length == analysis.Regions.Count)
        {
            return;
        }

        RegionProfile grown = resized.MaxBy(region => region.StorageGrowthMwh)!;
        string held = Join(analysis.Regions.Where(region => !region.WasResized).Select(region => region.State));
        double addedMwh = resized.Sum(region => region.StorageGrowthMwh);

        // With one region resized the finding names it; with several it has to name all of them, or
        // it reports the others as having held when they did not.
        string headline = resized.Length == 1
            ? $"Only {grown.State} needed more storage"
            : $"{resized.Length} of {analysis.Regions.Count} regions needed more storage";
        string detail = resized.Length == 1
            ? $"The sizing loop grew {grown.Name} storage from "
                + $"{PlotFormat.Compact(grown.StorageSizing.InitialEnergyMwh)} MWh to "
                + $"{PlotFormat.Compact(grown.StorageSizing.FinalEnergyMwh)} MWh to reach the "
                + $"reliability target, while {held} met the target with the fleet already installed."
            : $"The sizing loop grew storage in {Join(resized.Select(region => region.State))} to reach "
                + $"the reliability target, adding {PlotFormat.Compact(addedMwh)} MWh in total and most "
                + $"of it in {grown.State}. {held} met the target with the fleet already installed.";

        findings.Add(new Finding(
            headline,
            detail,
            FindingTone.Caution,
            PlotFormat.Compact(addedMwh),
            "MWh of storage added",
            Priority: 70));
    }

    /// <summary>Names in a readable list, so three regions read as "A, B and C" rather than "A, B, C".</summary>
    private static string Join(IEnumerable<string> names)
    {
        string[] values = [.. names];
        return values.Length switch
        {
            0 => string.Empty,
            1 => values[0],
            _ => $"{string.Join(", ", values[..^1])} and {values[^1]}",
        };
    }

    private static double SystemAllowanceUsed(SystemAnalysis analysis) =>
        analysis.Result.Reliability.TargetUsePercentageOfDemand <= 0
            ? 0
            : analysis.Result.Reliability.AchievedUsePercentageOfDemand
                / analysis.Result.Reliability.TargetUsePercentageOfDemand;

    private static string ReliabilityStandard(ReliabilityBasisDTO reliability) =>
        reliability.StandardName is { Length: > 0 } name ? $" ({name})" : string.Empty;
}
