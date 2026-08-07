using NEM.Model.Grid;
using NEM.Model.Units;

namespace NEM.Model.Scenarios;

/// <summary>Economic assumptions attached to one scenario generation fleet.</summary>
public sealed record GenerationCostParameters
{
    public GenerationCostParameters(
        PowerCapacityCost capitalCost,
        AnnualPowerCapacityCost fixedOperatingCost,
        GenerationEnergyCost variableOperatingCost,
        FuelPrice fuelPrice)
    {
        if (capitalCost.AudPerMwCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capitalCost));
        }

        if (fixedOperatingCost.AudPerMwYear < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedOperatingCost));
        }

        if (variableOperatingCost.AudPerMwhGenerated < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(variableOperatingCost));
        }

        if (fuelPrice.AudPerGjThermal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fuelPrice));
        }

        CapitalCost = capitalCost;
        FixedOperatingCost = fixedOperatingCost;
        VariableOperatingCost = variableOperatingCost;
        FuelPrice = fuelPrice;
    }

    public PowerCapacityCost CapitalCost { get; }
    public AnnualPowerCapacityCost FixedOperatingCost { get; }
    public GenerationEnergyCost VariableOperatingCost { get; }
    public FuelPrice FuelPrice { get; }

    public GenerationEnergyCost ShortRunMarginalCostFor(
        GenerationTechnologyProfile technologyProfile)
    {
        ArgumentNullException.ThrowIfNull(technologyProfile);
        return VariableOperatingCost + FuelPrice.ForHeatRate(technologyProfile.HeatRate);
    }
}