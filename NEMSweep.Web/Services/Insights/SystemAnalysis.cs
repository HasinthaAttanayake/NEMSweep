using NEMSweep.Contracts;
using NEMSweep.Web.Components.Viz;

namespace NEMSweep.Web.Services.Insights;

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

/// <summary>
/// One directed link of the declared system network, and what ran over it if the interval evidence
/// is in hand.
/// </summary>
/// <remarks>
/// The link exists because the run's topology declares it, not because a flow was observed on it,
/// so a link that never ran is still drawn rather than missing. <see cref="Flow"/> is null until
/// the artifact carrying the interconnector series is read, which keeps "no flow evidence yet"
/// distinguishable from "carried nothing".
/// </remarks>
public sealed record LinkFlow(
    string Id,
    string FromRegionId,
    string ToRegionId,
    double CapacityMw,
    LinkFlowEvidence? Flow = null)
{
    public string Label => $"{FromRegionId} to {ToRegionId}";
}

/// <summary>What one link carried over a dispatch period, integrated from its interval series.</summary>
public sealed record LinkFlowEvidence(
    double EnergyMwh,
    double LossesMwh,
    double PeakFlowMw,
    int FlowingIntervals,
    int TotalIntervals,
    double IntervalHours,
    double CapacityMw)
{
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
/// A system dispatch result read as a comparison between its regions. Everything here comes from
/// the compact overview artifact, which carries each region's summary, cost decomposition and
/// generation mix; the interval series are needed only to say what ran over each link, and are
/// folded in with <see cref="WithLinkEvidence"/> when they arrive.
/// </summary>
public sealed record SystemAnalysis(
    SystemFacts Result,
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

    /// <summary>Every link the run declares that carried energy, busiest first.</summary>
    public IReadOnlyList<LinkFlow> FlowingLinks =>
        [.. Links.Where(link => link.Flow is { EnergyMwh: > 0 })
            .OrderByDescending(link => link.Flow!.EnergyMwh)];

    /// <summary>True once the interconnector series have been read, whatever they turned out to say.</summary>
    public bool HasLinkEvidence => Links.Any(link => link.Flow is not null);

    public static SystemAnalysis Build(SystemFacts result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var regions = new List<RegionProfile>();
        foreach (string regionId in result.RegionIds ?? [])
        {
            if (result.RegionSummariesById?.GetValueOrDefault(regionId) is not { } summary)
            {
                continue;
            }

            regions.Add(new RegionProfile(
                regionId,
                summary.Metrics,
                summary.Reliability,
                summary.StorageSizing,
                summary.Cost,
                summary.DetailPath,
                EnergyMix.FromTotals(summary.DeliveredGenerationByTechnologyMwh)));
        }

        // The system mix is the sum of the regional ones by construction: every generator sits in
        // exactly one region, so summing them is the system total rather than an approximation of it.
        EnergyMix systemMix = EnergyMix.Combine(regions.Select(region => region.Mix));
        var analysis = new SystemAnalysis(result, regions, BuildLinks(result.Topology), systemMix, []);
        return analysis with { Findings = Derive(analysis) };
    }

    /// <summary>
    /// The same analysis with what actually ran over each link, once the artifact carrying the
    /// interconnector series has been read. Findings are re-derived because trade is one of them.
    /// </summary>
    public SystemAnalysis WithLinkEvidence(
        IReadOnlyList<DispatchInterconnectorDTO>? interconnectors,
        TimeSpan resolution)
    {
        double hours = resolution.TotalHours;
        var byId = new Dictionary<string, DispatchInterconnectorDTO>(StringComparer.OrdinalIgnoreCase);
        foreach (DispatchInterconnectorDTO link in interconnectors ?? [])
        {
            byId[link.Id] = link;
        }

        // A link the evidence does not mention keeps a null flow rather than a zero: the run
        // declared it, and nothing was published about what it did.
        var analysis = this with
        {
            Links = [.. Links.Select(link => link with
            {
                Flow = byId.TryGetValue(link.Id, out DispatchInterconnectorDTO? evidence)
                    ? Integrate(evidence, link.CapacityMw, hours)
                    : null,
            })],
        };
        return analysis with { Findings = Derive(analysis) };
    }

    /// <summary>
    /// Integrates one link's interval series into energy.
    /// </summary>
    /// <remarks>
    /// The capacity is the topology's rather than the evidence's own copy of it, so the utilisation
    /// a row states is a share of the capacity that same row displays. The two agree in a healthy
    /// artifact — the validator checks link evidence against topology — and using one of them makes
    /// a disagreement visible in validation rather than as a percentage that does not divide.
    /// </remarks>
    private static LinkFlowEvidence Integrate(
        DispatchInterconnectorDTO link,
        double capacityMw,
        double hours)
    {
        double[] flow = link.FlowMw ?? [];
        double[] losses = link.LossesMw ?? [];
        return new LinkFlowEvidence(
            flow.Sum() * hours,
            losses.Sum() * hours,
            flow.Length == 0 ? 0 : flow.Max(),
            flow.Count(value => value > 0),
            flow.Length,
            hours,
            capacityMw);
    }

    /// <summary>
    /// The declared network, whether or not anything ran over it. Reading the graph from the links
    /// that happen to appear in the flow evidence would make an idle link vanish from the picture.
    /// </summary>
    private static IReadOnlyList<LinkFlow> BuildLinks(DispatchTopologyDTO? topology) =>
        [.. (topology?.Links ?? []).Select(link => new LinkFlow(
            link.Id,
            link.FromRegionId,
            link.ToRegionId,
            link.CapacityMw))];

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
        AddCostDriver(analysis, findings);
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
        if (analysis.FlowingLinks.FirstOrDefault() is not { Flow: { } flow } busiest)
        {
            return;
        }

        findings.Add(new Finding(
            $"{RegionNames.State(busiest.FromRegionId)} underwrites {RegionNames.State(busiest.ToRegionId)}",
            $"{PlotFormat.Compact(flow.EnergyMwh)} MWh flowed from {busiest.FromRegionId} to "
            + $"{busiest.ToRegionId} over {flow.FlowingIntervals:N0} intervals — "
            + $"{PlotFormat.Share(flow.FlowingShare, 0)} of the period — reaching "
            + $"{PlotFormat.Compact(flow.PeakFlowMw)} MW against a "
            + $"{PlotFormat.Compact(busiest.CapacityMw)} MW limit. "
            + $"{PlotFormat.Compact(flow.LossesMwh)} MWh was lost in transmission, and is reported "
            + $"against {RegionNames.State(busiest.ToRegionId)} as the receiving region.",
            FindingTone.Neutral,
            PlotFormat.Share(flow.CapacityFactor, 1),
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
        string held = RegionNames.Readable(
            analysis.Regions.Where(region => !region.WasResized).Select(region => region.State));
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
            : $"The sizing loop grew storage in {RegionNames.Readable(resized.Select(region => region.State))} "
                + "to reach "
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

    /// <summary>
    /// What is actually driving the levelised cost. The three-way generation, storage and
    /// transmission split answers which bucket the money fell in; this answers which fleet, which
    /// is the decomposition a reader of a renewable-target result is after.
    /// </summary>
    private static void AddCostDriver(SystemAnalysis analysis, List<Finding> findings)
    {
        CostByTechnology cost = CostByTechnology.From(analysis.Result.Cost, analysis.SystemMix);
        if (cost.Entries.Count < 2 || cost.Entries[0] is not { } dearest)
        {
            return;
        }

        // Cost share against energy share is what makes the figure mean something: a fleet costing
        // a third of the bill for a third of the energy is unremarkable, and the gap is the story.
        string energyClause = dearest.EnergyShare > 0
            ? $"{PlotFormat.Share(dearest.CostShare)} of the generation bill for "
                + $"{PlotFormat.Share(dearest.EnergyShare)} of the delivered energy"
            : $"{PlotFormat.Share(dearest.CostShare)} of the generation bill";

        CostEntry? dearestPerMwh = cost.Entries
            .Where(entry => entry.EnergyMwh > 0)
            .MaxBy(entry => entry.AudPerOwnMwh);
        string comparison = dearestPerMwh is not null && dearestPerMwh.Technology != dearest.Technology
            ? $" {dearestPerMwh.Technology} is the dearest per megawatt-hour it delivers, at "
                + $"{PlotFormat.Money(dearestPerMwh.AudPerOwnMwh)}/MWh against "
                + $"{PlotFormat.Money(dearest.AudPerOwnMwh)}/MWh for {dearest.Technology.ToLowerInvariant()}."
            : string.Empty;

        findings.Add(new Finding(
            $"{dearest.Technology} is the largest single cost in the system",
            $"{dearest.Technology} carries {PlotFormat.MoneyTotal(dearest.AnnualisedCostAud)} a year — "
            + $"{energyClause} — and contributes "
            + $"{PlotFormat.Money(dearest.LevelisedContributionAudPerMwh)}/MWh of the "
            + $"{PlotFormat.Money(analysis.Result.Cost.GenerationSlcoeAudPerMwh)}/MWh generation "
            + $"levelised cost.{comparison}",
            FindingTone.Neutral,
            PlotFormat.Money(dearest.LevelisedContributionAudPerMwh),
            $"AUD/MWh from {dearest.Technology.ToLowerInvariant()}",
            Priority: 80));
    }

    private static double SystemAllowanceUsed(SystemAnalysis analysis) =>
        analysis.Result.Reliability.TargetUsePercentageOfDemand <= 0
            ? 0
            : analysis.Result.Reliability.AchievedUsePercentageOfDemand
                / analysis.Result.Reliability.TargetUsePercentageOfDemand;

    private static string ReliabilityStandard(ReliabilityBasisDTO reliability) =>
        reliability.StandardName is { Length: > 0 } name ? $" ({name})" : string.Empty;
}
