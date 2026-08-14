using NEM.Model.Grid;

namespace NEM.Model.Simulation;

public static class Dispatcher
{
    public static IReadOnlyList<DispatchOutcome> Dispatch(
        PowerSystem powerSystem) =>
        Dispatch(powerSystem, new GreedySurplusAndIncrementalGenerationChargingPolicy());

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
