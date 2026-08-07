using NEM.Model.Units;

namespace NEM.Model.Scenarios;

/// <summary>Economic assumptions attached to one scenario storage fleet.</summary>
public sealed record StorageCostParameters
{
    public StorageCostParameters(
        PowerCapacityCost powerCapitalCost,
        EnergyCapacityCost energyCapitalCost,
        AnnualPowerCapacityCost fixedOperatingCost)
    {
        if (powerCapitalCost.AudPerMwCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(powerCapitalCost));
        }

        if (energyCapitalCost.AudPerMwhCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(energyCapitalCost));
        }

        if (fixedOperatingCost.AudPerMwYear < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedOperatingCost));
        }

        PowerCapitalCost = powerCapitalCost;
        EnergyCapitalCost = energyCapitalCost;
        FixedOperatingCost = fixedOperatingCost;
    }

    public PowerCapacityCost PowerCapitalCost { get; }
    public EnergyCapacityCost EnergyCapitalCost { get; }
    public AnnualPowerCapacityCost FixedOperatingCost { get; }
}