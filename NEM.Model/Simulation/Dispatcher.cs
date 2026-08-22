using NEM.Model.Grid;

namespace NEM.Model.Simulation;

/// <summary>
/// The entry point for running a dispatch. Consumes a realised
/// <see cref="PowerSystem"/> and produces one outcome per region.
/// </summary>
/// <remarks>
/// <para>
/// The dispatcher is scenario-blind: it knows about regions, fleets and demand, and nothing about
/// the scenario they were realised from.
/// </para>
/// <para>
/// The interval is the outer loop and the region the inner one, so every region sits at the same
/// hour at the same time and a surplus in one can serve a deficit in another. Within an interval
/// the order is generation, then inter-regional transfer, then storage. A system with no
/// interconnectors therefore produces results identical to dispatching each region alone.
/// </para>
/// </remarks>
public static class Dispatcher
{
    /// <summary>Dispatches the system using the default storage policy.</summary>
    /// <param name="powerSystem">The realised system to dispatch.</param>
    /// <returns>One dispatch outcome per region, in system region order.</returns>
    public static IReadOnlyList<DispatchOutcome> Dispatch(
        PowerSystem powerSystem) =>
        Dispatch(powerSystem, new GreedySurplusAndIncrementalGenerationChargingPolicy());

    /// <summary>Dispatches the system using a supplied storage policy.</summary>
    /// <param name="powerSystem">The realised system to dispatch.</param>
    /// <param name="storagePolicy">
    /// The policy consulted once per region per interval. See <see cref="IStoragePolicy"/>.
    /// </param>
    /// <returns>One dispatch outcome per region, in system region order.</returns>
    public static IReadOnlyList<DispatchOutcome> Dispatch(
        PowerSystem powerSystem,
        IStoragePolicy storagePolicy) =>
        DispatchSystem(powerSystem, storagePolicy).RegionalOutcomes;

    /// <summary>
    /// Dispatches the system and returns the inter-regional transfer alongside the
    /// regional outcomes, for callers that need interconnector flows.
    /// </summary>
    public static SystemDispatchRunResult DispatchSystem(
        PowerSystem powerSystem) =>
        DispatchSystem(powerSystem, new GreedySurplusAndIncrementalGenerationChargingPolicy());

    /// <inheritdoc cref="DispatchSystem(PowerSystem)"/>
    public static SystemDispatchRunResult DispatchSystem(
        PowerSystem powerSystem,
        IStoragePolicy storagePolicy)
    {
        ArgumentNullException.ThrowIfNull(powerSystem);
        ArgumentNullException.ThrowIfNull(storagePolicy);

        SystemDispatchRunResult result = SystemDispatchRun.Execute(powerSystem, storagePolicy);
        return result with
        {
            RegionalOutcomes = Array.AsReadOnly(result.RegionalOutcomes.ToArray()),
            InterconnectorFlows = Array.AsReadOnly(result.InterconnectorFlows.ToArray()),
        };
    }
}
