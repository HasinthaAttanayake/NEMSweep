using NEM.Contracts;
using NEM.Web.Components.Viz;

namespace NEM.Web.Services.Insights;

/// <summary>Delivered energy from one technology over a dispatch period.</summary>
public sealed record TechnologyEnergy(string Technology, double EnergyMwh)
{
    public string Color => TechnologyPalette.ForGeneration(Technology);
}

/// <summary>
/// What a dispatch result actually generated, integrated from its interval series. The artifacts
/// publish integrated totals for demand, curtailment and unserved energy but not for generation by
/// technology, so the mix is summed here rather than assumed.
/// </summary>
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
