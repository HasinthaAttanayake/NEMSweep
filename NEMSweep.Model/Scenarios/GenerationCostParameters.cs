using NEMSweep.Model.Grid;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Scenarios;

/// <summary>Economic assumptions attached to one scenario generation fleet.</summary>
public sealed record GenerationCostParameters
{
    /// <summary>Validates and creates generation cost parameters.</summary>
    /// <param name="capitalCost">Overnight capital cost per MW of nameplate capacity.</param>
    /// <param name="fixedOperatingCost">Annual fixed operating cost per MW of nameplate capacity.</param>
    /// <param name="variableOperatingCost">Variable operating cost in AUD/MWh generated.</param>
    /// <param name="fuelPrice">Fuel price in AUD/GJ thermal, multiplied by heat rate to cost fuel.</param>
    /// <exception cref="ArgumentOutOfRangeException">Any cost component is negative.</exception>
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

    /// <summary>Overnight capital cost per MW of nameplate capacity.</summary>
    public PowerCapacityCost CapitalCost { get; }
    /// <summary>Annual fixed operating cost per MW of nameplate capacity.</summary>
    public AnnualPowerCapacityCost FixedOperatingCost { get; }
    /// <summary>Variable operating cost in AUD/MWh generated.</summary>
    public GenerationEnergyCost VariableOperatingCost { get; }
    /// <summary>Fuel price in AUD/GJ thermal.</summary>
    public FuelPrice FuelPrice { get; }

    /// <summary>
    /// Combines variable operating cost and fuel cost into the dispatch-relevant short-run
    /// marginal cost, in AUD/MWh generated: <see cref="VariableOperatingCost"/> plus
    /// <see cref="FuelPrice"/> multiplied by the profile's heat rate.
    /// </summary>
    /// <param name="technologyProfile">Supplies the heat rate used to convert fuel price into an energy cost.</param>
    /// <returns>Short-run marginal cost in AUD/MWh generated.</returns>
    public GenerationEnergyCost ShortRunMarginalCostFor(
        GenerationTechnologyProfile technologyProfile)
    {
        ArgumentNullException.ThrowIfNull(technologyProfile);
        return VariableOperatingCost + FuelPrice.ForHeatRate(technologyProfile.HeatRate);
    }
}