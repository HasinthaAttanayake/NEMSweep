using NEM.Model.Grid;

namespace NEM.Model.Simulation;

public static class Dispatcher
{
    public static IReadOnlyList<DispatchOutcome> Dispatch(
        PowerSystem powerSystem) =>
        Dispatch(powerSystem, new GreedySurplusAndIncrementalGenerationChargingPolicy());

    public static IReadOnlyList<DispatchOutcome> Dispatch(
        PowerSystem powerSystem,
        IStoragePolicy storagePolicy)
    {
        ArgumentNullException.ThrowIfNull(powerSystem);
        ArgumentNullException.ThrowIfNull(storagePolicy);

        return Array.AsReadOnly(powerSystem.Regions
            .Select(region => new RegionalDispatchRun(region, storagePolicy).Execute())
            .ToArray());
    }
}
