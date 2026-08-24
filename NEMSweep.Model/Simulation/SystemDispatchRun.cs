using NEMSweep.Model.Grid;
using NEMSweep.Model.Series;

namespace NEMSweep.Model.Simulation;

/// <summary>Regional outcomes plus the inter-regional transfer that produced them.</summary>
/// <param name="RegionalOutcomes">One dispatch outcome per region, in power-system order.</param>
/// <param name="InterconnectorFlows">Realised flow and loss on each link; empty when unlinked.</param>
public sealed record SystemDispatchRunResult(
    IReadOnlyList<DispatchOutcome> RegionalOutcomes,
    IReadOnlyList<InterconnectorFlow> InterconnectorFlows);

/// <summary>
/// Drives every region through the horizon in lockstep, so that surplus in one region
/// can serve a deficit in another within the same interval.
/// </summary>
/// <remarks>
/// The interval is the outer loop and the region the inner loop. That inversion is what
/// makes transfer possible: previously each region ran to completion before the next
/// began, so no two regions were ever at the same hour.
/// <para>
/// Order within an interval is generation, then transfer, then storage. Exporting before
/// storage means surplus reaches a neighbour's unserved load before it is used to charge
/// a local battery, and it means dispatchable headroom can be started to serve an export.
/// </para>
/// <para>
/// Each region's own sequence of operations is unchanged, so a system with no
/// interconnectors produces results identical to running each region independently.
/// </para>
/// </remarks>
internal static class SystemDispatchRun
{
    public static SystemDispatchRunResult Execute(
        PowerSystem powerSystem,
        IStoragePolicy storagePolicy)
    {
        RegionalDispatchRun[] runs = powerSystem.Regions
            .Select(region => new RegionalDispatchRun(region, storagePolicy))
            .ToArray();

        InterRegionalTransfer? transfer = null;
        if (powerSystem.Interconnectors.Count > 0)
        {
            RequireAlignedTimelines(powerSystem);
            transfer = InterRegionalTransfer.Create(powerSystem, runs, runs[0].Length);
        }

        int horizon = runs.Max(run => run.Length);
        for (int index = 0; index < horizon; index++)
        {
            foreach (RegionalDispatchRun run in runs)
            {
                if (index < run.Length)
                {
                    run.BeginInterval(index);
                    run.DispatchGeneration();
                }
            }

            transfer?.Execute(runs, index);

            foreach (RegionalDispatchRun run in runs)
            {
                if (index < run.Length)
                {
                    run.CompleteInterval();
                }
            }
        }

        FlowSeries reference = powerSystem.Regions[0].Demand.TotalDemand;
        return new SystemDispatchRunResult(
            runs.Select(run => run.BuildOutcome()).ToArray(),
            transfer?.BuildFlows(reference.Start, reference.Resolution) ?? []);
    }

    /// <summary>
    /// Transfer only makes sense between regions sharing an interval timeline, so an
    /// interconnected system must be aligned before dispatch rather than failing later
    /// when the outcomes are aggregated.
    /// </summary>
    private static void RequireAlignedTimelines(PowerSystem powerSystem)
    {
        FlowSeries reference = powerSystem.Regions[0].Demand.TotalDemand;
        foreach (Region region in powerSystem.Regions)
        {
            FlowSeries demand = region.Demand.TotalDemand;
            if (demand.Start != reference.Start
                || demand.Resolution != reference.Resolution
                || demand.Length != reference.Length)
            {
                throw new ArgumentException(
                    $"Region '{region.RegionId}' does not share a timeline with "
                    + $"'{powerSystem.Regions[0].RegionId}'. Interconnected regions must be "
                    + "dispatched over the same intervals.",
                    nameof(powerSystem));
            }
        }
    }
}
