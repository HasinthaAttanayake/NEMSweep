using NEM.Model.Units;

namespace NEM.Model.Economics;

/// <summary>
/// Annual generation and storage cost contributions using electricity served
/// to load as their common denominator. Transmission remains a zero placeholder.
/// </summary>
public sealed record PowerSystemCostBreakdown
{
    internal PowerSystemCostBreakdown(
        EnergyPrice systemLevelisedCostOfGeneration,
        Money totalAnnualisedGenerationCost,
        EnergyPrice systemLevelisedCostOfStorage,
        Money totalAnnualisedStorageCost,
        Energy deliveredEnergy)
    {
        SystemLevelisedCostOfGeneration = systemLevelisedCostOfGeneration;
        SystemLevelisedCostOfStorage = systemLevelisedCostOfStorage;
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
    public Money TotalAnnualisedGenerationCost { get; }
    public Money TotalAnnualisedStorageCost { get; }
    /// <summary>Annualised generation and storage cost, in AUD.</summary>
    public Money TotalAnnualisedCost { get; }
    /// <summary>Total annual electricity served to load, in MWh.</summary>
    public Energy DeliveredEnergy { get; }
}