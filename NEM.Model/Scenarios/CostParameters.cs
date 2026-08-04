using NEM.Model.Units;

namespace NEM.Model.Scenarios;

/// <summary>
/// Economic assumptions attached to one scenario fleet plan.
/// <para>
/// Properties use named economic quantities, so their names describe the cost
/// concept without repeating units. This object validates that scenario cost
/// assumptions are non-negative; it stores inputs only and performs no cost,
/// annuity, escalation, or parameter-derivation calculation.
/// </para>
/// </summary>
public sealed record CostParameters
{
    public CostParameters(
        PowerCapacityCost capitalCost,
        EnergyCapacityCost energyCapitalCost,
        AnnualPowerCapacityCost fixedOperatingCost,
        EnergyPrice variableOperatingCost,
        FuelPrice fuelPrice)
    {
        if (capitalCost.AudPerMwCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capitalCost));
        }

        if (energyCapitalCost.AudPerMwhStorage < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(energyCapitalCost));
        }

        if (fixedOperatingCost.AudPerMwYear < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedOperatingCost));
        }

        if (variableOperatingCost.AudPerMwhDelivered < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(variableOperatingCost));
        }

        if (fuelPrice.AudPerGjThermal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fuelPrice));
        }

        CapitalCost = capitalCost;
        EnergyCapitalCost = energyCapitalCost;
        FixedOperatingCost = fixedOperatingCost;
        VariableOperatingCost = variableOperatingCost;
        FuelPrice = fuelPrice;
    }

    /// <summary>One-time capital cost per MW of power capacity.</summary>
    public PowerCapacityCost CapitalCost { get; }

    /// <summary>One-time capital cost per MWh of storage energy capacity.</summary>
    public EnergyCapacityCost EnergyCapitalCost { get; }

    /// <summary>Recurring fixed operating cost per MW per year.</summary>
    public AnnualPowerCapacityCost FixedOperatingCost { get; }

    /// <summary>Variable operating cost per MWh delivered.</summary>
    public EnergyPrice VariableOperatingCost { get; }

    /// <summary>Fuel cost per GJ of thermal energy input.</summary>
    public FuelPrice FuelPrice { get; }
}