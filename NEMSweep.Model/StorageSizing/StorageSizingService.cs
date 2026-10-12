using NEMSweep.Model.Grid;

namespace NEMSweep.Model.StorageSizing;

/// <summary>
/// Finds Battery capacity that brings a system within its reliability target, or reports why the
/// bounded search did not get there.
/// </summary>
/// <remarks>
/// <para>
/// The service is pure and whole-system scoped. It builds immutable
/// <see cref="PowerSystem"/> candidates and re-dispatches the entire linked system for each one,
/// changing capacity only in regions that fail the target. Pumped hydro is fixed; existing Battery
/// capacity is the starting lower bound, and results report total Battery capacity rather than the
/// increment added.
/// </para>
/// <para>
/// The result is a deterministic coordinate-wise near-frontier point. It is not a global minimum
/// and not a cost-optimal point: nothing in the search prices the capacity it adds.
/// </para>
/// <para>
/// During growth, the service prioritises failing regions by descending total unserved energy,
/// with ordinal region-ID ties. It tests one region at a time, accepts its best candidate only when
/// that region's own unserved energy improves by more than 0.001 MWh, and re-dispatches and
/// re-ranks after each accepted change. Blocked regions are skipped and reconsidered after an
/// accepted change. The priority is intentional and can affect successful sizing results.
/// </para>
/// <para>
/// When growth makes no progress, the whole-system status is
/// <see cref="StorageSizingStatus.BatteryCapacityLimitReached"/> if any failing region has no
/// larger candidate; otherwise it is
/// <see cref="StorageSizingStatus.StorageNoLongerImprovesReliability"/>. Per-region status and
/// termination evidence identify all current blockers. These are bounded-search outcomes, not
/// global infeasibility or optimality claims.
/// </para>
/// </remarks>
public static class StorageSizingService
{
    /// <summary>Runs the sizing search over a realised system.</summary>
    /// <param name="powerSystem">The system to size. Never mutated.</param>
    /// <param name="options">The reliability target and the capacity and pass bounds.</param>
    /// <returns>
    /// The final system, its dispatch evidence, the per-region capacity settled on, and a status
    /// saying whether the target was met and, if not, what stopped the search. Whole-system
    /// termination evidence lists the blockers for all failing regions. Only
    /// <see cref="StorageSizingStatus.EnergyLimited"/> establishes that no Battery capacity could
    /// have met the target; other failure statuses describe this bounded search.
    /// </returns>
    public static StorageSizingRunResult Size(
        PowerSystem powerSystem,
        StorageSizingOptions options)
    {
        ArgumentNullException.ThrowIfNull(powerSystem);
        ArgumentNullException.ThrowIfNull(options);

        return new StorageSizingSearch(powerSystem, options).Execute();
    }
}