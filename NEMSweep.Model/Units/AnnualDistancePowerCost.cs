namespace NEMSweep.Model.Units;

/// <summary>
/// Recurring annual fixed operating cost per kilometre of transmission line per megawatt of
/// transfer capacity, in AUD/km/MW/year.
/// </summary>
public readonly record struct AnnualDistancePowerCost
{
    /// <summary>The rate in AUD per kilometre per MW of transfer capacity per year.</summary>
    public decimal AudPerKmPerMwYear { get; }

    private AnnualDistancePowerCost(decimal audPerKmPerMwYear) => AudPerKmPerMwYear = audPerKmPerMwYear;

    /// <summary>Creates an <see cref="AnnualDistancePowerCost"/> from a rate in AUD/km/MW/year.</summary>
    public static AnnualDistancePowerCost FromAudPerKmPerMwYear(decimal audPerKmPerMwYear) =>
        new(audPerKmPerMwYear);

    /// <summary>
    /// Fixed cost over a duration: AUD = AUD/km/MW/year × km × MW × years. Distance, capacity,
    /// and duration must be non-negative and finite.
    /// </summary>
    public Money For(Distance distance, Power capacity, double years)
    {
        decimal kilometres = DecimalPhysicalBoundary.RequireNonNegativeFinite(
            distance.Kilometres,
            nameof(distance));
        decimal megawatts = DecimalPhysicalBoundary.RequireNonNegativeFinite(
            capacity.Megawatts,
            nameof(capacity));
        decimal durationYears = DecimalPhysicalBoundary.RequireNonNegativeFinite(years, nameof(years));
        return Money.FromAud(AudPerKmPerMwYear * kilometres * megawatts * durationYears);
    }
}
