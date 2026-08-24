namespace NEMSweep.Model.Units;

/// <summary>
/// Recurring annual cost per unit of installed power capacity, in AUD/MW/year.
/// <para>
/// This quantity represents fixed operating expenditure. It is distinct from
/// <see cref="PowerCapacityCost"/>, which represents a one-time capital cost in
/// AUD/MW without a time denominator.
/// </para>
/// </summary>
public readonly record struct AnnualPowerCapacityCost
{
    /// <summary>The recurring cost in AUD per MW of capacity per year.</summary>
    public decimal AudPerMwYear { get; }

    private AnnualPowerCapacityCost(decimal audPerMwYear) => AudPerMwYear = audPerMwYear;

    /// <summary>Creates an annual fixed cost from AUD per MW per year.</summary>
    public static AnnualPowerCapacityCost FromAudPerMwYear(decimal audPerMwYear) =>
        new(audPerMwYear);

    /// <summary>
    /// Fixed cost over a duration: AUD = AUD/MW/year × MW × years. Capacity and
    /// duration must be non-negative and finite.
    /// </summary>
    public Money For(Power capacity, double years)
    {
        decimal capacityMegawatts = DecimalPhysicalBoundary.RequireNonNegativeFinite(
            capacity.Megawatts,
            nameof(capacity));
        decimal durationYears = DecimalPhysicalBoundary.RequireNonNegativeFinite(years, nameof(years));
        return Money.FromAud(AudPerMwYear * capacityMegawatts * durationYears);
    }
}