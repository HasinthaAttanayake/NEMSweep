using NEM.Model.Units;

namespace NEM.Model.Scenarios;

/// <summary>Economic assumptions attached to one scenario storage fleet.</summary>
public sealed record StorageCostParameters
{
    /// <summary>Validates and creates storage cost parameters.</summary>
    /// <param name="powerCapitalCost">Overnight capital cost per MW of power capacity.</param>
    /// <param name="energyCapitalCost">Overnight capital cost per MWh of storage capacity.</param>
    /// <param name="fixedOperatingCost">Annual fixed operating cost per MW of power capacity.</param>
    /// <exception cref="ArgumentOutOfRangeException">Any cost component is negative.</exception>
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

    /// <summary>Overnight capital cost per MW of power capacity.</summary>
    public PowerCapacityCost PowerCapitalCost { get; }
    /// <summary>Overnight capital cost per MWh of storage capacity.</summary>
    public EnergyCapacityCost EnergyCapitalCost { get; }
    /// <summary>Annual fixed operating cost per MW of power capacity.</summary>
    public AnnualPowerCapacityCost FixedOperatingCost { get; }
}