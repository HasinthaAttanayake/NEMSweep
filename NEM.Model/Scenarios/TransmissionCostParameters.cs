using NEM.Model.Units;

namespace NEM.Model.Scenarios;

/// <summary>Economic assumptions attached to one scenario interconnector.</summary>
/// <remarks>
/// There is no variable or fuel term: transmission has no marginal fuel cost in this
/// model. Losses already impose a marginal cost implicitly, by requiring more generation
/// to be dispatched for each MWh that reaches load.
/// </remarks>
public sealed record TransmissionCostParameters
{
    public TransmissionCostParameters(
        PowerCapacityCost capitalCost,
        AnnualPowerCapacityCost fixedOperatingCost)
    {
        if (capitalCost.AudPerMwCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capitalCost));
        }

        if (fixedOperatingCost.AudPerMwYear < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedOperatingCost));
        }

        CapitalCost = capitalCost;
        FixedOperatingCost = fixedOperatingCost;
    }

    /// <summary>Overnight capital cost per MW of rated transfer capacity.</summary>
    public PowerCapacityCost CapitalCost { get; }

    /// <summary>Annual fixed operating cost per MW of rated transfer capacity.</summary>
    public AnnualPowerCapacityCost FixedOperatingCost { get; }
}
