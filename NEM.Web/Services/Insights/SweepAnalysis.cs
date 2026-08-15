using NEM.Contracts;
using NEM.Web.Components;
using NEM.Web.Components.Viz;

namespace NEM.Web.Services.Insights;

/// <summary>One run of a sweep that produced results, with the scalars for the selected scope.</summary>
public sealed record SweepRun(SweepIndexPointDTO Point, SweepPointScalarResultsDTO Scalars)
{
    public string Label => Point.Label;

    public double AxisValue => Point.AxisValue;

    /// <summary>
    /// The whole annual cost of whatever scope these scalars describe, reconstructed from the two
    /// figures the index carries. The levelised cost is published to the cent, so this is accurate
    /// to well under a tenth of a percent — but it is a product of published scalars rather than a
    /// published total, and the site says so wherever it is shown.
    /// </summary>
    /// <remarks>
    /// When the analysis is built for a region, these are that region's scalars and this is that
    /// region's cost, not the system's. Callers must label it with
    /// <see cref="SweepAnalysis.AnnualCostLabel"/> rather than assuming system scope.
    /// </remarks>
    public double TotalAnnualCostAud => (double)Scalars.SlcoeAudPerMwh * Scalars.EnergyServedMwh;

    /// <summary>Delivered energy from renewable technologies, where the run carries a share.</summary>
    public double? RenewableEnergyMwh => Scalars.AchievedRenewableShareGridScale is { } share
        ? share * Scalars.DeliveredGenerationMwh
        : null;

    /// <summary>Storage's share of the levelised cost, which is what turns a falling curve around.</summary>
    public double StorageCostShare => Scalars.SlcoeAudPerMwh <= 0
        ? 0
        : (double)(Scalars.StorageSlcoeAudPerMwh / Scalars.SlcoeAudPerMwh);
}

/// <summary>An interior extremum in a series: the run the curve turns at, and how far it turned.</summary>
public sealed record SweepTurningPoint(int Index, SweepRun Run, bool IsMinimum, double ReboundPercentage);

/// <summary>
/// A sweep read as a set of trade-offs rather than as a list of runs. Everything is derived from
/// the sweep index alone, so a page can state what the sweep found before any detail artifact is
/// fetched.
/// </summary>
public sealed record SweepAnalysis(
    SweepIndexDTO Index,
    string? RegionId,
    IReadOnlyList<SweepRun> Runs,
    IReadOnlyList<SweepIndexPointDTO> ConstrainedPoints,
    SweepTurningPoint? UnitCostTurningPoint,
    IReadOnlyList<Finding> Findings)
{
    /// <summary>The run with the lowest levelised cost, which is not always the last or the first.</summary>
    public SweepRun? CheapestUnitCost => Runs.Count == 0
        ? null
        : Runs.MinBy(run => run.Scalars.SlcoeAudPerMwh);

    public SweepRun? First => Runs.Count == 0 ? null : Runs[0];

    public SweepRun? Last => Runs.Count == 0 ? null : Runs[^1];

    public string AxisUnit => Index.Axis.Unit;

    public string AxisLabel => Index.Axis.Label;

    /// <summary>
    /// What these figures describe. Selecting a region rebuilds the analysis from that region's
    /// scalars, so every label drawn from it has to move with the selection: a regional view that
    /// still says "system" states a single region's result as the whole market's.
    /// </summary>
    public string ScopeName => RegionId is null ? "the whole system" : RegionNames.Full(RegionId);

    /// <summary>Column and axis label for the derived annual cost at the selected scope.</summary>
    public string AnnualCostLabel => RegionId is null
        ? "Annual system cost"
        : $"Annual cost, {RegionId}";

    public static SweepAnalysis Build(SweepIndexDTO index, string? regionId = null)
    {
        ArgumentNullException.ThrowIfNull(index);

        var runs = new List<SweepRun>();
        var constrained = new List<SweepIndexPointDTO>();
        foreach (SweepIndexPointDTO point in index.Points ?? [])
        {
            if (point.Status == SweepPointStatus.Failed)
            {
                constrained.Add(point);
                continue;
            }

            if (SweepChartData.SelectScalars(point, regionId) is { } scalars)
            {
                runs.Add(new SweepRun(point, scalars));
            }
        }

        SweepTurningPoint? turningPoint = FindTurningPoint(runs);
        var analysis = new SweepAnalysis(index, regionId, runs, constrained, turningPoint, []);
        return analysis with { Findings = Derive(analysis) };
    }

    /// <summary>
    /// Finds an interior minimum or maximum in the levelised cost, ignoring turns smaller than half
    /// a percent so a flat curve with rounding wobble does not read as a reversal.
    /// </summary>
    private static SweepTurningPoint? FindTurningPoint(IReadOnlyList<SweepRun> runs)
    {
        if (runs.Count < 3)
        {
            return null;
        }

        double[] values = [.. runs.Select(run => (double)run.Scalars.SlcoeAudPerMwh)];
        int minimumIndex = IndexOfExtreme(values, minimum: true);
        int maximumIndex = IndexOfExtreme(values, minimum: false);

        SweepTurningPoint? candidate = Consider(runs, values, minimumIndex, isMinimum: true)
            ?? Consider(runs, values, maximumIndex, isMinimum: false);
        return candidate;
    }

    private static SweepTurningPoint? Consider(
        IReadOnlyList<SweepRun> runs,
        double[] values,
        int index,
        bool isMinimum)
    {
        if (index <= 0 || index >= values.Length - 1 || values[index] == 0)
        {
            return null;
        }

        double approach = Math.Abs(values[0] - values[index]) / Math.Abs(values[index]);
        double rebound = Math.Abs(values[^1] - values[index]) / Math.Abs(values[index]);
        return approach < 0.005 || rebound < 0.005
            ? null
            : new SweepTurningPoint(index, runs[index], isMinimum, 100 * rebound);
    }

    private static int IndexOfExtreme(double[] values, bool minimum)
    {
        int best = 0;
        for (int index = 1; index < values.Length; index++)
        {
            if (minimum ? values[index] < values[best] : values[index] > values[best])
            {
                best = index;
            }
        }

        return best;
    }

    private static IReadOnlyList<Finding> Derive(SweepAnalysis analysis)
    {
        var findings = new List<Finding>();
        AddUnitAgainstTotalCost(analysis, findings);
        AddTurningPoint(analysis, findings);
        AddCostComposition(analysis, findings);
        AddEnergyBalance(analysis, findings);
        AddRenewableShare(analysis, findings);
        AddConstraints(analysis, findings);
        AddReliabilityBinding(analysis, findings);
        // Highest priority first, so a page showing only the first few shows the ones that matter.
        return [.. findings.OrderByDescending(finding => finding.Priority)];
    }

    /// <summary>
    /// The finding this whole page exists for: a levelised cost and a total cost can move in
    /// opposite directions, and reporting only the first makes a scenario that costs more money
    /// look like a saving.
    /// </summary>
    private static void AddUnitAgainstTotalCost(SweepAnalysis analysis, List<Finding> findings)
    {
        if (analysis.First is not { } first || analysis.Last is not { } last || analysis.Runs.Count < 2)
        {
            return;
        }

        double unitChange = PercentageChange((double)first.Scalars.SlcoeAudPerMwh, (double)last.Scalars.SlcoeAudPerMwh);
        double totalChange = PercentageChange(first.TotalAnnualCostAud, last.TotalAnnualCostAud);
        if (Math.Abs(unitChange) < 0.5 && Math.Abs(totalChange) < 0.5)
        {
            return;
        }

        // A change under half a percent is not a movement worth a verb. Reading both verbs off the
        // unit change alone reported a flat unit cost beside a ten percent rise in the bill as
        // "both hold", which is false about the half that moved.
        bool unitMoved = Math.Abs(unitChange) >= 0.5;
        bool totalMoved = Math.Abs(totalChange) >= 0.5;
        bool diverges = unitMoved && totalMoved && Math.Sign(unitChange) != Math.Sign(totalChange);
        string headline = (unitMoved, totalMoved) switch
        {
            (true, true) when diverges =>
                $"Cost per MWh {Direction(unitChange)} {Math.Abs(unitChange):N0}% while the annual bill "
                    + $"{Direction(totalChange)} {Math.Abs(totalChange):N0}%",
            (true, true) => $"Cost per MWh and the annual bill both {DirectionPlural(unitChange)}",
            (true, false) =>
                $"Cost per MWh {Direction(unitChange)} {Math.Abs(unitChange):N0}% while the annual bill holds",
            (false, true) =>
                $"The annual bill {Direction(totalChange)} {Math.Abs(totalChange):N0}% while cost per MWh holds",
            _ => "Cost per MWh and the annual bill both hold",
        };
        findings.Add(new Finding(
            headline,
            $"From {first.Label} to {last.Label}, levelised cost moves "
            + $"{PlotFormat.Money(first.Scalars.SlcoeAudPerMwh)} → "
            + $"{PlotFormat.Money(last.Scalars.SlcoeAudPerMwh)}/MWh while the annual cost of "
            + $"{analysis.ScopeName} moves {PlotFormat.MoneyTotal((decimal)first.TotalAnnualCostAud)} → "
            + $"{PlotFormat.MoneyTotal((decimal)last.TotalAnnualCostAud)}"
            + (diverges
                ? ". The average megawatt-hour gets cheaper because far more of them are sold, not "
                    + "because the system spends less."
                : "."),
            diverges ? FindingTone.Caution : FindingTone.Neutral,
            PlotFormat.Signed(totalChange, "N0") + "%",
            analysis.AnnualCostLabel.ToLowerInvariant(),
            Priority: diverges ? 100 : 70));
    }

    private static void AddTurningPoint(SweepAnalysis analysis, List<Finding> findings)
    {
        if (analysis.UnitCostTurningPoint is not { } turn || analysis.Last is not { } last)
        {
            return;
        }

        string verb = turn.IsMinimum ? "bottoms out" : "peaks";
        findings.Add(new Finding(
            $"Levelised cost {verb} at {turn.Run.Label}, then reverses",
            $"{turn.Run.Label} reaches {PlotFormat.Money(turn.Run.Scalars.SlcoeAudPerMwh)}/MWh — the "
            + (turn.IsMinimum ? "cheapest" : "dearest") + " run in the sweep — and by "
            + $"{last.Label} the figure has moved {turn.ReboundPercentage:N1}% back to "
            + $"{PlotFormat.Money(last.Scalars.SlcoeAudPerMwh)}/MWh. Reading only the ends of this "
            + "sweep would miss the turn entirely.",
            FindingTone.Caution,
            PlotFormat.Money(turn.Run.Scalars.SlcoeAudPerMwh),
            $"/MWh at {turn.Run.AxisValue:N0} {analysis.AxisUnit}",
            Priority: 95));
    }

    /// <summary>
    /// Splits the levelised cost into the part that keeps falling and the part that takes over. A
    /// turn in the total is rarely visible in either component on its own.
    /// </summary>
    private static void AddCostComposition(SweepAnalysis analysis, List<Finding> findings)
    {
        if (analysis.First is not { } first || analysis.Last is not { } last || analysis.Runs.Count < 3)
        {
            return;
        }

        double generationChange = PercentageChange(
            (double)first.Scalars.GenerationSlcoeAudPerMwh,
            (double)last.Scalars.GenerationSlcoeAudPerMwh);
        double storageChange = PercentageChange(
            (double)first.Scalars.StorageSlcoeAudPerMwh,
            (double)last.Scalars.StorageSlcoeAudPerMwh);
        if (Math.Sign(generationChange) == Math.Sign(storageChange) || Math.Abs(storageChange) < 5)
        {
            return;
        }

        findings.Add(new Finding(
            generationChange < 0
                ? "Generation keeps getting cheaper; storage is what turns the curve"
                : "Storage keeps getting cheaper; generation is what turns the curve",
            $"Across the sweep, generation moves {PlotFormat.Money(first.Scalars.GenerationSlcoeAudPerMwh)} → "
            + $"{PlotFormat.Money(last.Scalars.GenerationSlcoeAudPerMwh)}/MWh "
            + $"({PlotFormat.Signed(generationChange, "N0")}%) while storage moves "
            + $"{PlotFormat.Money(first.Scalars.StorageSlcoeAudPerMwh)} → "
            + $"{PlotFormat.Money(last.Scalars.StorageSlcoeAudPerMwh)}/MWh "
            + $"({PlotFormat.Signed(storageChange, "N0")}%). Storage grows from "
            + $"{PlotFormat.Share(first.StorageCostShare)} to {PlotFormat.Share(last.StorageCostShare)} "
            + "of the levelised cost.",
            FindingTone.Neutral,
            Priority: 65));
    }

    /// <summary>
    /// Where the energy came from and went. A sweep that adds load and a sweep that adds capacity
    /// both change curtailment, but they mean opposite things, so the comparison is chosen by which
    /// side of the balance actually moved.
    /// </summary>
    private static void AddEnergyBalance(SweepAnalysis analysis, List<Finding> findings)
    {
        if (analysis.First is not { } first || analysis.Last is not { } last)
        {
            return;
        }

        double curtailmentChange = last.Scalars.CurtailedEnergyMwh - first.Scalars.CurtailedEnergyMwh;
        if (Math.Abs(curtailmentChange) < 1)
        {
            return;
        }

        double demandChange = last.Scalars.DemandMwh - first.Scalars.DemandMwh;
        if (Math.Abs(demandChange) > 0.01 * Math.Max(1, first.Scalars.DemandMwh))
        {
            double absorbed = demandChange == 0 ? 0 : -curtailmentChange / demandChange;
            findings.Add(new Finding(
                curtailmentChange < 0
                    ? "The added load soaks up energy the system was throwing away"
                    : "The added load did not reduce spilled energy",
                $"Demand rises {PlotFormat.Compact(demandChange)} MWh across the sweep while curtailment "
                + $"moves {PlotFormat.Compact(first.Scalars.CurtailedEnergyMwh)} → "
                + $"{PlotFormat.Compact(last.Scalars.CurtailedEnergyMwh)} MWh"
                + (curtailmentChange < 0
                    ? $". Recovered spill covers {PlotFormat.Share(absorbed)} of the new load; the rest "
                        + "has to be generated."
                    : "."),
                curtailmentChange < 0 ? FindingTone.Favourable : FindingTone.Caution,
                PlotFormat.Share(Math.Clamp(absorbed, 0, 1), 0),
                "of new load met from recovered spill",
                Priority: 75));
            return;
        }

        if (first.RenewableEnergyMwh is not { } firstRenewable
            || last.RenewableEnergyMwh is not { } lastRenewable)
        {
            return;
        }

        double renewableChange = lastRenewable - firstRenewable;
        if (renewableChange <= 0)
        {
            return;
        }

        double spilledShare = curtailmentChange / (renewableChange + curtailmentChange);
        findings.Add(new Finding(
            curtailmentChange > 0
                ? "Most of the extra renewable energy is spilled, not used"
                : "Extra renewable energy is absorbed without new spill",
            $"Renewable delivery rises {PlotFormat.Compact(renewableChange)} MWh across the sweep while "
            + $"curtailment rises {PlotFormat.Compact(curtailmentChange)} MWh. "
            + $"{PlotFormat.Share(spilledShare, 0)} of the additional renewable energy the fleet could "
            + "produce never reaches load, because demand is unchanged and storage is not grown.",
            FindingTone.Caution,
            PlotFormat.Share(spilledShare, 0),
            "of new renewable energy curtailed",
            Priority: 85));
    }

    private static void AddRenewableShare(SweepAnalysis analysis, List<Finding> findings)
    {
        if (analysis.First?.Scalars.AchievedRenewableShareGridScale is not { } firstShare
            || analysis.Last?.Scalars.AchievedRenewableShareGridScale is not { } lastShare)
        {
            return;
        }

        double points = 100 * (lastShare - firstShare);
        if (Math.Abs(points) < 0.5)
        {
            return;
        }

        findings.Add(new Finding(
            points < 0
                ? $"Renewable share falls {Math.Abs(points):N1} points across the sweep"
                : $"Renewable share rises {points:N1} points across the sweep",
            $"Grid-scale renewable share moves {PlotFormat.Share(firstShare)} → {PlotFormat.Share(lastShare)} "
            + $"between {analysis.First!.Label} and {analysis.Last!.Label}"
            + (points < 0
                ? ", because the additional energy is met by the dispatchable fleet rather than by new "
                    + "renewable capacity."
                : "."),
            points < 0 ? FindingTone.Caution : FindingTone.Favourable,
            PlotFormat.Share(lastShare),
            "renewable at the last run",
            Priority: 60));
    }

    private static void AddConstraints(SweepAnalysis analysis, List<Finding> findings)
    {
        if (analysis.ConstrainedPoints.Count == 0)
        {
            return;
        }

        SweepIndexPointDTO firstConstrained = analysis.ConstrainedPoints[0];
        SweepRun? lastFeasible = analysis.Runs
            .Where(run => run.AxisValue < firstConstrained.AxisValue)
            .MaxBy(run => run.AxisValue);
        string stage = firstConstrained.Failure?.Stage.ToString().ToLowerInvariant() ?? "unknown";
        findings.Add(new Finding(
            lastFeasible is null
                ? $"{analysis.ConstrainedPoints.Count} of {analysis.Index.Points.Length} runs hit a modelled limit"
                : $"The scenario stops being feasible past {lastFeasible.Label}",
            (lastFeasible is null
                ? string.Empty
                : $"{lastFeasible.Label} is the last run that produced results. ")
            + $"{firstConstrained.Label} and "
            + (analysis.ConstrainedPoints.Count == 1
                ? "no other run"
                : $"{analysis.ConstrainedPoints.Count - 1} further "
                    + (analysis.ConstrainedPoints.Count == 2 ? "run" : "runs"))
            + $" reached a limit at the {stage} stage: "
            + (firstConstrained.Failure?.Message ?? "no reason was recorded."),
            FindingTone.Constraint,
            analysis.ConstrainedPoints.Count.ToString("N0"),
            "runs without results",
            Priority: 90));
    }

    private static void AddReliabilityBinding(SweepAnalysis analysis, List<Finding> findings)
    {
        SweepRun[] atTarget = [.. analysis.Runs.Where(run =>
            run.Point.Reliability is { } reliability
            && reliability.TargetUsePercentageOfDemand > 0
            && reliability.AchievedUsePercentageOfDemand
                >= reliability.TargetUsePercentageOfDemand * 0.999)];
        if (atTarget.Length < 2)
        {
            return;
        }

        findings.Add(new Finding(
            $"{atTarget.Length} runs sit exactly on the reliability target",
            $"From {atTarget[0].Label} onwards, every run lands at "
            + $"{atTarget[0].Point.Reliability!.TargetUsePercentageOfDemand:G3}% unserved energy rather "
            + "than below it. The sizing loop is buying just enough storage to reach the standard, so "
            + "the target is setting the cost, not the weather.",
            FindingTone.Constraint,
            atTarget.Length.ToString("N0"),
            $"of {analysis.Runs.Count:N0} runs at the limit",
            Priority: 55));
    }

    private static double PercentageChange(double from, double to) =>
        from == 0 ? 0 : 100 * (to - from) / Math.Abs(from);

    private static string Direction(double change) => change switch
    {
        > 0 => "rises",
        < 0 => "falls",
        _ => "holds",
    };

    /// <summary>The same verb for a sentence whose subject is both cost readings at once.</summary>
    private static string DirectionPlural(double change) => change switch
    {
        > 0 => "rise",
        < 0 => "fall",
        _ => "hold",
    };
}
