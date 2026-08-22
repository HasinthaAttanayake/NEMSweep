using NEM.Model.Grid;

namespace NEM.Model.StorageSizing;

/// <summary>
/// Finds Battery capacity that brings a system within its reliability target, or reports why no
/// capacity can.
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
/// </remarks>
public static class StorageSizingService
{
    /// <summary>Runs the sizing search over a realised system.</summary>
    /// <param name="powerSystem">The system to size. Never mutated.</param>
    /// <param name="options">The reliability target and the capacity and pass bounds.</param>
    /// <returns>
    /// The final system, its dispatch evidence, the per-region capacity settled on, and a status
    /// saying whether the target was met and, if not, what stopped the search.
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