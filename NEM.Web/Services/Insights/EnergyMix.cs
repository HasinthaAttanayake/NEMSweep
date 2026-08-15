using NEM.Contracts;
using NEM.Web.Components.Viz;

namespace NEM.Web.Services.Insights;

/// <summary>Delivered energy from one technology over a dispatch period.</summary>
public sealed record TechnologyEnergy(string Technology, double EnergyMwh)
{
    public string Color => TechnologyPalette.ForGeneration(Technology);
}

/// <summary>
/// What a dispatch result actually generated, by technology, over its period.
/// </summary>
/// <remarks>
/// A system result publishes these totals per region and needs no series
/// (<see cref="FromTotals"/>). A single region's own artifact publishes the interval series and no
/// totals, so those are integrated here (<see cref="From"/>) rather than assumed.
/// </remarks>
public sealed record EnergyMix(IReadOnlyList<TechnologyEnergy> ByTechnology, double TotalMwh)
{
    public static readonly EnergyMix Empty = new([], 0);

    /// <summary>
    /// Share of delivered energy from solar, wind and hydro. This matches the model's own
    /// grid-scale renewable share, so a figure derived here and a figure read off a sweep scalar
    /// describe the same thing.
    /// </summary>
    public double RenewableShare => Share(TechnologyPalette.Renewable);

    public IReadOnlyList<MixSegment> Segments() =>
        [.. ByTechnology.Select(entry => new MixSegment(entry.Technology, entry.Color, entry.EnergyMwh))];

    /// <summary>
    /// Reads a mix the producer has already integrated. Technologies are ordered by the site's own
    /// palette order so a region's bar reads left to right the same way every other one does.
    /// </summary>
    public static EnergyMix FromTotals(IReadOnlyDictionary<string, double>? byTechnologyMwh)
    {
        if (byTechnologyMwh is not { Count: > 0 })
        {
            return Empty;
        }

        var entries = new List<TechnologyEnergy>();
        foreach (string technology in TechnologyPalette.Order(byTechnologyMwh.Keys))
        {
            entries.Add(new TechnologyEnergy(technology, byTechnologyMwh[technology]));
        }

        return new EnergyMix(entries, entries.Sum(entry => entry.EnergyMwh));
    }

    /// <summary>
    /// Sums several mixes into one, for the system total behind a set of regional mixes.
    /// </summary>
    public static EnergyMix Combine(IEnumerable<EnergyMix> mixes)
    {
        ArgumentNullException.ThrowIfNull(mixes);

        var totals = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (EnergyMix mix in mixes)
        {
            foreach (TechnologyEnergy entry in mix.ByTechnology)
            {
                totals[entry.Technology] = totals.GetValueOrDefault(entry.Technology) + entry.EnergyMwh;
            }
        }

        return FromTotals(totals);
    }

    /// <summary>
    /// Integrates the delivered-generation series. Interval values are powers in MW, so each is
    /// multiplied by the interval length rather than added as though it were energy.
    /// </summary>
    public static EnergyMix From(DispatchSeriesDTO? series, TimeSpan resolution)
    {
        if (series?.DeliveredGenerationByTechnologyMw is not { Count: > 0 } byTechnology
            || resolution <= TimeSpan.Zero)
        {
            return Empty;
        }

        double hours = resolution.TotalHours;
        var entries = new List<TechnologyEnergy>();
        foreach (string technology in TechnologyPalette.Order(byTechnology.Keys))
        {
            double[]? values = byTechnology.GetValueOrDefault(technology);
            if (values is null)
            {
                continue;
            }

            entries.Add(new TechnologyEnergy(technology, values.Sum() * hours));
        }

        return new EnergyMix(entries, entries.Sum(entry => entry.EnergyMwh));
    }

    private double Share(IReadOnlySet<string> technologies) => TotalMwh <= 0
        ? 0
        : ByTechnology.Where(entry => technologies.Contains(entry.Technology))
            .Sum(entry => entry.EnergyMwh) / TotalMwh;
}
