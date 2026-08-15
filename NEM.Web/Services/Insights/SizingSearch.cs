using NEM.Contracts;

namespace NEM.Web.Services.Insights;

/// <summary>
/// The storage sizing search read as a path rather than an outcome.
/// </summary>
/// <remarks>
/// The outcome alone says storage grew from one figure to another. It does not say whether the
/// reliability target was expensive or nearly free to reach — and unserved energy against capacity,
/// which the published trajectory now supports, is the shape that argues for or against a storage
/// build.
/// </remarks>
public sealed record SizingSearch(IReadOnlyList<StorageSizingPassDTO> Passes)
{
    public static readonly SizingSearch Empty = new([]);

    /// <summary>
    /// Whether there is a curve to read. One pass is a dispatch rather than a search, and several
    /// passes that all ran the same fleet to the same result are a confirmation rather than one —
    /// drawing those would stack every marker on one point and label it a trade-off.
    /// </summary>
    public bool HasPath => Passes.Count > 1
        && (Passes.Select(pass => pass.EnergyCapacityMwh).Distinct().Count() > 1
            || Passes.Select(pass => pass.UnservedEnergyMwh).Distinct().Count() > 1);

    public StorageSizingPassDTO First => Passes[0];

    public StorageSizingPassDTO Last => Passes[^1];

    public double AddedEnergyMwh => Last.EnergyCapacityMwh - First.EnergyCapacityMwh;

    public double RemovedUnservedMwh => First.UnservedEnergyMwh - Last.UnservedEnergyMwh;

    public bool GrewStorage => AddedEnergyMwh > 0;

    /// <summary>
    /// Unserved energy removed per megawatt-hour of storage added, averaged over the whole search.
    /// Zero where nothing was added, because the ratio has no denominator rather than an infinite one.
    /// </summary>
    public double UnservedRemovedPerMwhAdded =>
        GrewStorage ? RemovedUnservedMwh / AddedEnergyMwh : 0;

    /// <summary>
    /// Passes that re-ran a capacity the search had already reached. Those are probes the search
    /// dispatched and did not accept, so counting them as steps would overstate the path taken.
    /// </summary>
    public int RepeatedCapacityPasses => Passes
        .Skip(1)
        .Count(pass => Passes.Any(earlier =>
            earlier.Pass < pass.Pass && earlier.EnergyCapacityMwh == pass.EnergyCapacityMwh));

    public static SizingSearch From(StorageSizingOutcomeDTO? sizing)
    {
        StorageSizingPassDTO[] passes = [.. (sizing?.Trajectory ?? []).OrderBy(pass => pass.Pass)];
        return passes.Length == 0 ? Empty : new SizingSearch(passes);
    }
}
