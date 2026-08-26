using NEMSweep.Model.Units;
using NEMSweep.Model.Grid;

namespace NEMSweep.Model.Economics;

/// <summary>Annualised generation cost attributable to one generation technology.</summary>
public sealed record GenerationCostContribution(
    GenerationTechnology Technology,
    Money AnnualisedCost);

/// <summary>
/// Annual generation and storage cost contributions for one region, using that
/// region's energy served as the denominator for each levelised cost.
/// </summary>
public sealed record RegionCostBreakdown
{
    internal RegionCostBreakdown(
        string regionId,
        Money annualisedGenerationCost,
        Money annualisedStorageCost,
        Energy energyServed,
        Energy netImportedEnergy,
        IReadOnlyList<GenerationCostContribution> generationCostContributions)
    {
        RegionId = regionId;
        AnnualisedGenerationCost = annualisedGenerationCost;
        AnnualisedStorageCost = annualisedStorageCost;
        TotalAnnualisedCost = annualisedGenerationCost + annualisedStorageCost;
        EnergyServed = energyServed;
        NetImportedEnergy = netImportedEnergy;
        GenerationCostContributions = generationCostContributions.ToArray();
        decimal contributionTotalAud = GenerationCostContributions.Sum(
            contribution => contribution.AnnualisedCost.Aud);
        if (contributionTotalAud != annualisedGenerationCost.Aud)
        {
            throw new ArgumentException(
                "Generation cost contributions must sum to annualised generation cost.",
                nameof(generationCostContributions));
        }
        if (!double.IsFinite(energyServed.MegawattHours) || energyServed.MegawattHours <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(energyServed),
                "Energy served must be positive when deriving levelised costs for region "
                + $"'{regionId}'.");
        }

        LevelisedCostOfGeneration = annualisedGenerationCost.Per(energyServed);
        LevelisedCostOfStorage = annualisedStorageCost.Per(energyServed);
        LevelisedCostOfElectricity = TotalAnnualisedCost.Per(energyServed);
    }

    /// <summary>Region identifier corresponding to the scenario and dispatch outcome.</summary>
    public string RegionId { get; }
    /// <summary>Annualised generation cost, in AUD.</summary>
    public Money AnnualisedGenerationCost { get; }
    /// <summary>Annualised generation cost by technology, in AUD.</summary>
    public IReadOnlyList<GenerationCostContribution> GenerationCostContributions { get; }
    /// <summary>Annualised storage cost, in AUD.</summary>
    public Money AnnualisedStorageCost { get; }
    /// <summary>Annualised generation and storage cost, in AUD.</summary>
    public Money TotalAnnualisedCost { get; }
    /// <summary>Annual energy served in this region, in MWh. The levelised-cost denominator.</summary>
    public Energy EnergyServed { get; }

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
    /// <summary>Annualised generation cost per regional MWh served, in AUD/MWh.</summary>
    public EnergyPrice LevelisedCostOfGeneration { get; }
    /// <summary>Annualised storage cost per regional MWh served, in AUD/MWh.</summary>
    public EnergyPrice LevelisedCostOfStorage { get; }
    /// <summary>Annualised generation and storage cost per regional MWh served, in AUD/MWh.</summary>
    public EnergyPrice LevelisedCostOfElectricity { get; }
}

/// <summary>
/// Annual generation, storage and transmission cost contributions using energy
/// served as their common denominator.
/// </summary>
/// <remarks>
/// Transmission is held at system level and is not attributed to any region. An
/// interconnector spans two regions, and every way of splitting it between them is
/// arbitrary, whether evenly, by rating, or by realised flow. The consequence is that the regional
/// levelised costs do not sum to the system figure, because the system figure carries
/// transmission and the regional ones do not.
/// </remarks>
public sealed record PowerSystemCostBreakdown
{
    internal PowerSystemCostBreakdown(
        Money totalAnnualisedGenerationCost,
        Money totalAnnualisedStorageCost,
        Money totalAnnualisedTransmissionCost,
        Energy energyServed,
        IReadOnlyList<RegionCostBreakdown> regions,
        IReadOnlyList<GenerationCostContribution> generationCostContributions,
        bool transmissionCostModelled)
    {
        Regions = regions.ToArray();
        GenerationCostContributions = generationCostContributions.ToArray();
        decimal contributionTotalAud = GenerationCostContributions.Sum(
            contribution => contribution.AnnualisedCost.Aud);
        if (contributionTotalAud != totalAnnualisedGenerationCost.Aud)
        {
            throw new ArgumentException(
                "Generation cost contributions must sum to total annualised generation cost.",
                nameof(generationCostContributions));
        }
        SystemLevelisedCostOfGeneration = totalAnnualisedGenerationCost.Per(energyServed);
        SystemLevelisedCostOfStorage = totalAnnualisedStorageCost.Per(energyServed);
        SystemLevelisedCostOfTransmission =
            totalAnnualisedTransmissionCost.Per(energyServed);
        TotalAnnualisedGenerationCost = totalAnnualisedGenerationCost;
        TotalAnnualisedStorageCost = totalAnnualisedStorageCost;
        TotalAnnualisedTransmissionCost = totalAnnualisedTransmissionCost;
        TransmissionCostModelled = transmissionCostModelled;
        TotalAnnualisedCost = totalAnnualisedGenerationCost
            + totalAnnualisedStorageCost
            + totalAnnualisedTransmissionCost;
        SystemLevelisedCostOfElectricity = TotalAnnualisedCost.Per(energyServed);
        EnergyServed = energyServed;
    }

    /// <summary>
    /// Total annualised system cost per MWh served, in AUD/MWh. The cost of building and
    /// running the system, not a retail price.
    /// </summary>
    public EnergyPrice SystemLevelisedCostOfElectricity { get; }

    /// <summary>Annualised generation cost per MWh served, in AUD/MWh.</summary>
    public EnergyPrice SystemLevelisedCostOfGeneration { get; }

    /// <summary>
    /// Annualised storage asset cost per MWh served, in AUD/MWh. Not a standalone levelised
    /// cost of storage: the denominator is system energy served, and charging energy is already
    /// priced through gross-generation variable and fuel cost.
    /// </summary>
    public EnergyPrice SystemLevelisedCostOfStorage { get; }

    /// <summary>
    /// Annualised interconnector cost per MWh served to load. Zero when the system has no
    /// interconnectors.
    /// </summary>
    public EnergyPrice SystemLevelisedCostOfTransmission { get; }

    /// <summary>
    /// Regional annualised costs and levelised costs, each using regional energy
    /// served as its denominator. These exclude transmission.
    /// </summary>
    public IReadOnlyList<RegionCostBreakdown> Regions { get; }
    /// <summary>System annualised generation cost aggregated by technology, in AUD.</summary>
    public IReadOnlyList<GenerationCostContribution> GenerationCostContributions { get; }
    /// <summary>
    /// Annualised generation capital, fixed operating, variable operating and fuel cost, in AUD.
    /// Exactly the sum of <see cref="GenerationCostContributions"/>.
    /// </summary>
    public Money TotalAnnualisedGenerationCost { get; }

    /// <summary>
    /// Annualised storage power and energy capital plus fixed operating cost, in AUD. Covers total
    /// final capacity, including capacity storage sizing introduced.
    /// </summary>
    public Money TotalAnnualisedStorageCost { get; }

    /// <summary>Annualised interconnector capital and fixed operating cost, in AUD.</summary>
    public Money TotalAnnualisedTransmissionCost { get; }

    /// <summary>Whether declared interconnector economics were evaluated for this system.</summary>
    public bool TransmissionCostModelled { get; }

    /// <summary>Annualised generation, storage and transmission cost, in AUD.</summary>
    public Money TotalAnnualisedCost { get; }

    /// <summary>Total annual energy served, in MWh. The levelised-cost denominator.</summary>
    public Energy EnergyServed { get; }
}