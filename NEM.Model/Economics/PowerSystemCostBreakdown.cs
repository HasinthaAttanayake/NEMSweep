using NEM.Model.Units;

namespace NEM.Model.Economics;

/// <summary>
/// Annual generation and storage cost contributions for one region, using that
/// region's electricity served to load as the denominator for each levelised cost.
/// </summary>
public sealed record RegionCostBreakdown
{
    internal RegionCostBreakdown(
        string regionId,
        Money annualisedGenerationCost,
        Money annualisedStorageCost,
        Energy deliveredEnergy)
    {
        RegionId = regionId;
        AnnualisedGenerationCost = annualisedGenerationCost;
        AnnualisedStorageCost = annualisedStorageCost;
        TotalAnnualisedCost = annualisedGenerationCost + annualisedStorageCost;
        DeliveredEnergy = deliveredEnergy;
        if (!double.IsFinite(deliveredEnergy.MegawattHours) || deliveredEnergy.MegawattHours <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deliveredEnergy),
                "Delivered energy must be positive when deriving levelised costs for region "
                + $"'{regionId}'.");
        }

        LevelisedCostOfGeneration = annualisedGenerationCost.Per(deliveredEnergy);
        LevelisedCostOfStorage = annualisedStorageCost.Per(deliveredEnergy);
        LevelisedCostOfElectricity = TotalAnnualisedCost.Per(deliveredEnergy);
    }

    /// <summary>Region identifier corresponding to the scenario and dispatch outcome.</summary>
    public string RegionId { get; }
    /// <summary>Annualised generation cost, in AUD.</summary>
    public Money AnnualisedGenerationCost { get; }
    /// <summary>Annualised storage cost, in AUD.</summary>
    public Money AnnualisedStorageCost { get; }
    /// <summary>Annualised generation and storage cost, in AUD.</summary>
    public Money TotalAnnualisedCost { get; }
    /// <summary>Annual electricity served to load in this region, in MWh.</summary>
    public Energy DeliveredEnergy { get; }
    /// <summary>Annualised generation cost per regional MWh served to load, in AUD/MWh.</summary>
    public EnergyPrice LevelisedCostOfGeneration { get; }
    /// <summary>Annualised storage cost per regional MWh served to load, in AUD/MWh.</summary>
    public EnergyPrice LevelisedCostOfStorage { get; }
    /// <summary>Annualised generation and storage cost per regional MWh served to load, in AUD/MWh.</summary>
    public EnergyPrice LevelisedCostOfElectricity { get; }
}

/// <summary>
/// Annual generation and storage cost contributions using electricity served
/// to load as their common denominator. Transmission remains a zero placeholder.
/// </summary>
public sealed record PowerSystemCostBreakdown
{
    internal PowerSystemCostBreakdown(
        Money totalAnnualisedGenerationCost,
        Money totalAnnualisedStorageCost,
        Energy deliveredEnergy,
        IReadOnlyList<RegionCostBreakdown> regions)
    {
        Regions = regions.ToArray();
        SystemLevelisedCostOfGeneration = totalAnnualisedGenerationCost.Per(deliveredEnergy);
        SystemLevelisedCostOfStorage = totalAnnualisedStorageCost.Per(deliveredEnergy);
        SystemLevelisedCostOfTransmission = default;
        TotalAnnualisedGenerationCost = totalAnnualisedGenerationCost;
        TotalAnnualisedStorageCost = totalAnnualisedStorageCost;
        TotalAnnualisedCost = totalAnnualisedGenerationCost + totalAnnualisedStorageCost;
        SystemLevelisedCostOfElectricity = TotalAnnualisedCost.Per(deliveredEnergy);
        DeliveredEnergy = deliveredEnergy;
    }

    public EnergyPrice SystemLevelisedCostOfElectricity { get; }
    public EnergyPrice SystemLevelisedCostOfGeneration { get; }
    public EnergyPrice SystemLevelisedCostOfStorage { get; }
    /// <summary>Zero placeholder; transmission cost is not yet calculated.</summary>
    public EnergyPrice SystemLevelisedCostOfTransmission { get; }
    /// <summary>
    /// Regional annualised costs and levelised costs, each using regional electricity
    /// served to load as its denominator.
    /// </summary>
    public IReadOnlyList<RegionCostBreakdown> Regions { get; }
    public Money TotalAnnualisedGenerationCost { get; }
    public Money TotalAnnualisedStorageCost { get; }
    /// <summary>Annualised generation and storage cost, in AUD.</summary>
    public Money TotalAnnualisedCost { get; }
    /// <summary>Total annual electricity served to load, in MWh.</summary>
    public Energy DeliveredEnergy { get; }
}