using NEM.Model.Grid;

namespace NEM.Model.StorageSizing;

public static class StorageSizingService
{
    public static StorageSizingRunResult Size(
        PowerSystem powerSystem,
        StorageSizingOptions options)
    {
        ArgumentNullException.ThrowIfNull(powerSystem);
        ArgumentNullException.ThrowIfNull(options);

        return new StorageSizingSearch(powerSystem, options).Execute();
    }
}