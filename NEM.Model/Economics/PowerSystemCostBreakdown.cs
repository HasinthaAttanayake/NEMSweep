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
        Energy deliveredEnergy,
        Energy netImportedEnergy)
    {
        RegionId = regionId;
        AnnualisedGenerationCost = annualisedGenerationCost;
        AnnualisedStorageCost = annualisedStorageCost;
        TotalAnnualisedCost = annualisedGenerationCost + annualisedStorageCost;
        DeliveredEnergy = deliveredEnergy;
        NetImportedEnergy = netImportedEnergy;
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

    /// <summary>
    /// Imports less exports over the year, in MWh. Disclosed because the levelised costs
    /// above divide this region's own generation and storage cost by the load it served,
    /// and a net importer serves load its own plant did not produce. A positive figure
    /// means the regional levelised costs understate the true cost of supply.
    /// </summary>
    /// <remarks>
    /// Correcting for this would require pricing energy transferred between regions,
    /// which this model does not do. The figure is reported so the distortion is visible
    /// rather than silent.
    /// </remarks>
    public Energy NetImportedEnergy { get; }
    /// <summary>Annualised generation cost per regional MWh served to load, in AUD/MWh.</summary>
    public EnergyPrice LevelisedCostOfGeneration { get; }
    /// <summary>Annualised storage cost per regional MWh served to load, in AUD/MWh.</summary>
    public EnergyPrice LevelisedCostOfStorage { get; }
    /// <summary>Annualised generation and storage cost per regional MWh served to load, in AUD/MWh.</summary>
    public EnergyPrice LevelisedCostOfElectricity { get; }
}

/// <summary>
/// Annual generation, storage and transmission cost contributions using electricity
/// served to load as their common denominator.
/// </summary>
/// <remarks>
/// Transmission is held at system level and is not attributed to any region. An
/// interconnector spans two regions, and every way of splitting it between them — evenly,
/// by rating, or by realised flow — is arbitrary. The consequence is that the regional
/// levelised costs do not sum to the system figure, because the system figure carries
/// transmission and the regional ones do not.
/// </remarks>
public sealed record PowerSystemCostBreakdown
{
    internal PowerSystemCostBreakdown(
        Money totalAnnualisedGenerationCost,
        Money totalAnnualisedStorageCost,
        Money totalAnnualisedTransmissionCost,
        Energy deliveredEnergy,
        IReadOnlyList<RegionCostBreakdown> regions)
    {
        Regions = regions.ToArray();
        SystemLevelisedCostOfGeneration = totalAnnualisedGenerationCost.Per(deliveredEnergy);
        SystemLevelisedCostOfStorage = totalAnnualisedStorageCost.Per(deliveredEnergy);
        SystemLevelisedCostOfTransmission =
            totalAnnualisedTransmissionCost.Per(deliveredEnergy);
        TotalAnnualisedGenerationCost = totalAnnualisedGenerationCost;
        TotalAnnualisedStorageCost = totalAnnualisedStorageCost;
        TotalAnnualisedTransmissionCost = totalAnnualisedTransmissionCost;
        TotalAnnualisedCost = totalAnnualisedGenerationCost
            + totalAnnualisedStorageCost
            + totalAnnualisedTransmissionCost;
        SystemLevelisedCostOfElectricity = TotalAnnualisedCost.Per(deliveredEnergy);
        DeliveredEnergy = deliveredEnergy;
    }

    public EnergyPrice SystemLevelisedCostOfElectricity { get; }
    public EnergyPrice SystemLevelisedCostOfGeneration { get; }
    public EnergyPrice SystemLevelisedCostOfStorage { get; }

    /// <summary>
    /// Annualised interconnector cost per MWh served to load. Zero when the system has no
    /// interconnectors.
    /// </summary>
    public EnergyPrice SystemLevelisedCostOfTransmission { get; }

    /// <summary>
    /// Regional annualised costs and levelised costs, each using regional electricity
    /// served to load as its denominator. These exclude transmission.
    /// </summary>
    public IReadOnlyList<RegionCostBreakdown> Regions { get; }
    public Money TotalAnnualisedGenerationCost { get; }
    public Money TotalAnnualisedStorageCost { get; }

    /// <summary>Annualised interconnector capital and fixed operating cost, in AUD.</summary>
    public Money TotalAnnualisedTransmissionCost { get; }

    /// <summary>Annualised generation, storage and transmission cost, in AUD.</summary>
    public Money TotalAnnualisedCost { get; }

    /// <summary>Total annual electricity served to load, in MWh.</summary>
    public Energy DeliveredEnergy { get; }
}