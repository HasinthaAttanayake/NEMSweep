namespace NEMSweep.Model.Units;

/// <summary>
/// One-time capital cost per unit of storage energy capacity, in AUD/MWh of storage
/// capacity. It is not AUD/MWh generated or delivered.
/// </summary>
public readonly record struct EnergyCapacityCost
{
    /// <summary>The rate in AUD per MWh of storage capacity.</summary>
    public decimal AudPerMwhCapacity { get; }

    private EnergyCapacityCost(decimal audPerMwhCapacity) =>
        AudPerMwhCapacity = audPerMwhCapacity;

    /// <summary>Creates an <see cref="EnergyCapacityCost"/> from a rate in AUD/MWh of storage capacity.</summary>
    public static EnergyCapacityCost FromAudPerMwhCapacity(decimal audPerMwhCapacity) =>
        new(audPerMwhCapacity);

    /// <summary>
    /// One-time capital cost of building <paramref name="capacity"/>: AUD = AUD/MWh × MWh.
    /// The capacity must be non-negative and finite.
    /// </summary>
    public Money For(Energy capacity) => Money.FromAud(
        AudPerMwhCapacity * DecimalPhysicalBoundary.RequireNonNegativeFinite(
            capacity.MegawattHours,
            nameof(capacity)));

    /// <summary>One-time capital cost of building <paramref name="capacity"/> at this rate.</summary>
    public static Money operator *(EnergyCapacityCost cost, Energy capacity) =>
        cost.For(capacity);

    /// <summary>One-time capital cost of building <paramref name="capacity"/> at this rate.</summary>
    public static Money operator *(Energy capacity, EnergyCapacityCost cost) =>
        cost * capacity;
}