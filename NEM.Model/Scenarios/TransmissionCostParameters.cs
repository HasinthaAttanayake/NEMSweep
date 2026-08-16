using NEM.Model.Units;

namespace NEM.Model.Scenarios;

/// <summary>Economic assumptions attached to one scenario interconnector.</summary>
/// <remarks>
/// Costed by both route length and transfer capacity: a line's capex and fixed opex scale with
/// the kilometres of conductor built and the megawatts it is rated to carry, since a
/// higher-capacity corridor over the same distance costs more to build and maintain. There is no
/// variable or fuel term: transmission has no marginal fuel cost in this model. Losses already
/// impose a marginal cost implicitly, by requiring more generation to be dispatched for each MWh
/// that reaches load.
/// </remarks>
public sealed record TransmissionCostParameters
{
    public TransmissionCostParameters(
        DistancePowerCost capitalCost,
        AnnualDistancePowerCost fixedOperatingCost)
    {
        if (capitalCost.AudPerKmPerMw < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capitalCost));
        }

        if (fixedOperatingCost.AudPerKmPerMwYear < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedOperatingCost));
        }

        CapitalCost = capitalCost;
        FixedOperatingCost = fixedOperatingCost;
    }

    /// <summary>Overnight capital cost per kilometre of line built, per megawatt of capacity.</summary>
    public DistancePowerCost CapitalCost { get; }

    /// <summary>Annual fixed operating cost per kilometre of line built, per megawatt of capacity.</summary>
    public AnnualDistancePowerCost FixedOperatingCost { get; }
}
