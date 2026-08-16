namespace NEM.Model.Units;

/// <summary>
/// One-time capital cost per kilometre of transmission line per megawatt of transfer capacity,
/// in AUD/km/MW.
/// </summary>
public readonly record struct DistancePowerCost
{
    public decimal AudPerKmPerMw { get; }

    private DistancePowerCost(decimal audPerKmPerMw) => AudPerKmPerMw = audPerKmPerMw;

    public static DistancePowerCost FromAudPerKmPerMw(decimal audPerKmPerMw) => new(audPerKmPerMw);

    /// <summary>Cost of a line: AUD = AUD/km/MW × km built × MW of capacity.</summary>
    public Money For(Distance distance, Power capacity)
    {
        decimal kilometres = DecimalPhysicalBoundary.RequireNonNegativeFinite(
            distance.Kilometres,
            nameof(distance));
        decimal megawatts = DecimalPhysicalBoundary.RequireNonNegativeFinite(
            capacity.Megawatts,
            nameof(capacity));
        return Money.FromAud(AudPerKmPerMw * kilometres * megawatts);
    }
}
