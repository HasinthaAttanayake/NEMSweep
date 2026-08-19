using NEM.Model.Grid;

namespace NEM.Model.Simulation;

/// <summary>
/// Dispatch order for a region's generating fleets against local demand, exports, and
/// incremental storage charging.
///
/// Fleets are ordered by short-run marginal cost, then by
/// <see cref="GenerationTechnology"/> to break ties deterministically. Conventional Hydro
/// is sorted into this order like any other technology (its zero fuel cost typically ties
/// it with Solar/Wind) - it is not excluded here. What keeps it from being spent greedily
/// on whichever hours come first each month is a separate, causal cap on its REQUEST inside
/// <see cref="RegionalDispatchRun"/>'s dispatch loop, paced against its remaining monthly
/// budget by <see cref="HydroReservationState"/>; see that type for why sort position alone
/// (an earlier version of this file sorted Hydro last, see NEM-076) was not enough - it
/// merely relocated the greed rather than rationing it.
/// </summary>
internal static class GenerationMeritOrder
{
    internal static IOrderedEnumerable<GeneratingFleet> Sort(IEnumerable<GeneratingFleet> fleets) =>
        fleets
            .OrderBy(fleet => fleet.ShortRunMarginalCost)
            .ThenBy(fleet => fleet.GenerationTechnology);
}
