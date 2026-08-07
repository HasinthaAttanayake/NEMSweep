namespace NEM.Model.Units;

/// <summary>One-time capital cost per unit of storage energy capacity, in AUD/MWh.</summary>
public readonly record struct EnergyCapacityCost
{
    public decimal AudPerMwhCapacity { get; }

    private EnergyCapacityCost(decimal audPerMwhCapacity) =>
        AudPerMwhCapacity = audPerMwhCapacity;

    public static EnergyCapacityCost FromAudPerMwhCapacity(decimal audPerMwhCapacity) =>
        new(audPerMwhCapacity);

    public Money For(Energy capacity) => Money.FromAud(
        AudPerMwhCapacity * DecimalPhysicalBoundary.RequireNonNegativeFinite(
            capacity.MegawattHours,
            nameof(capacity)));

    public static Money operator *(EnergyCapacityCost cost, Energy capacity) =>
        cost.For(capacity);

    public static Money operator *(Energy capacity, EnergyCapacityCost cost) =>
        cost * capacity;
}