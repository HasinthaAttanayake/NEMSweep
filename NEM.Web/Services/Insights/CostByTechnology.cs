using NEM.Contracts;
using NEM.Web.Components.Viz;

namespace NEM.Web.Services.Insights;

/// <summary>What one technology costs a run, and what it delivered for the money.</summary>
public sealed record CostEntry(
    string Technology,
    decimal AnnualisedCostAud,
    decimal LevelisedContributionAudPerMwh,
    double EnergyMwh,
    decimal TotalGenerationCostAud,
    double TotalEnergyMwh)
{
    public string Color => TechnologyPalette.ForGeneration(Technology);

    /// <summary>This technology's share of the whole generation bill.</summary>
    public double CostShare => TotalGenerationCostAud <= 0
        ? 0
        : (double)(AnnualisedCostAud / TotalGenerationCostAud);

    /// <summary>This technology's share of delivered energy, which is what the money bought.</summary>
    public double EnergyShare => TotalEnergyMwh <= 0 ? 0 : EnergyMwh / TotalEnergyMwh;

    /// <summary>
    /// Cost per megawatt-hour this technology itself delivered — not its contribution to the
    /// system figure, which is spread over every megawatt-hour served. A fleet running few hours
    /// is dear by this measure and cheap by the other, and the two answer different questions.
    /// </summary>
    public decimal AudPerOwnMwh => EnergyMwh <= 0 ? 0 : AnnualisedCostAud / (decimal)EnergyMwh;

    /// <summary>
    /// How much dearer this technology's share of the bill is than its share of the energy. Above
    /// one it is taking more of the money than it is delivering of the energy.
    /// </summary>
    public double CostToEnergyRatio => EnergyShare <= 0 ? 0 : CostShare / EnergyShare;
}

/// <summary>
/// A cost result decomposed by the fleet that incurred it, dearest first.
/// </summary>
/// <remarks>
/// The producer publishes annualised cost and levelised contribution per technology, and the site
/// already integrates delivered energy per technology. Joining them is what turns "which of three
/// buckets" into "which fleet", so nothing here is derived beyond the join and its shares.
/// </remarks>
public sealed record CostByTechnology(
    IReadOnlyList<CostEntry> Entries,
    decimal TotalAnnualisedCostAud,
    decimal TotalLevelisedAudPerMwh)
{
    public static readonly CostByTechnology Empty = new([], 0, 0);

    /// <summary>
    /// Whether the published contributions add up to the published generation cost. The contract
    /// requires exact serialized reconciliation, so a mismatch is a defect worth saying out loud
    /// rather than a rounding tolerance to widen.
    /// </summary>
    public bool ReconcilesTo(decimal annualisedGenerationCostAud) =>
        Entries.Count == 0 || TotalAnnualisedCostAud == annualisedGenerationCostAud;

    public static CostByTechnology From(DispatchCostDTO? cost, EnergyMix mix)
    {
        ArgumentNullException.ThrowIfNull(mix);

        if (cost?.GenerationCostContributions is not { Length: > 0 } contributions)
        {
            return Empty;
        }

        decimal totalCost = contributions.Sum(contribution => contribution.AnnualisedCostAud);
        var byTechnology = mix.ByTechnology.ToDictionary(
            entry => entry.Technology,
            entry => entry.EnergyMwh,
            StringComparer.OrdinalIgnoreCase);

        var entries = contributions
            .Select(contribution => new CostEntry(
                contribution.Technology,
                contribution.AnnualisedCostAud,
                contribution.LevelisedContributionAudPerMwh,
                byTechnology.GetValueOrDefault(contribution.Technology),
                totalCost,
                mix.TotalMwh))
            .OrderByDescending(entry => entry.AnnualisedCostAud)
            .ToArray();

        return new CostByTechnology(
            entries,
            totalCost,
            contributions.Sum(contribution => contribution.LevelisedContributionAudPerMwh));
    }

    public IReadOnlyList<MixSegment> CostSegments() =>
        [.. Entries.Select(entry => new MixSegment(
            entry.Technology,
            entry.Color,
            (double)entry.AnnualisedCostAud))];

    public IReadOnlyList<MixSegment> EnergySegments() =>
        [.. Entries.Select(entry => new MixSegment(entry.Technology, entry.Color, entry.EnergyMwh))];
}
