namespace NEM.Model.Units;

/// <summary>
/// Generation operating cost in AUD per MWh generated — a rate, not a cost total.
/// Multiply by generated energy (see <see cref="For"/>) to get an actual spend.
/// </summary>
public readonly record struct GenerationEnergyCost : IComparable<GenerationEnergyCost>
{
    private GenerationEnergyCost(decimal audPerMwhGenerated) =>
        AudPerMwhGenerated = audPerMwhGenerated;

    /// <summary>The rate in AUD per MWh generated.</summary>
    public decimal AudPerMwhGenerated { get; }

    /// <summary>Zero cost per MWh. Seed for summing a collection of rates.</summary>
    public static GenerationEnergyCost Zero { get; } = new(0m);

    /// <summary>Creates a <see cref="GenerationEnergyCost"/> from a rate in AUD/MWh generated.</summary>
    public static GenerationEnergyCost FromAudPerMwhGenerated(decimal audPerMwhGenerated) =>
        new(audPerMwhGenerated);

    /// <summary>
    /// Operating cost of generating <paramref name="generatedEnergy"/>: AUD = AUD/MWh × MWh.
    /// The energy must be non-negative and finite.
    /// </summary>
    public Money For(Energy generatedEnergy) => Money.FromAud(
        AudPerMwhGenerated * DecimalPhysicalBoundary.RequireNonNegativeFinite(
            generatedEnergy.MegawattHours,
            nameof(generatedEnergy)));

    /// <summary>Sums two generation cost rates.</summary>
    public static GenerationEnergyCost operator +(
        GenerationEnergyCost left,
        GenerationEnergyCost right) =>
        FromAudPerMwhGenerated(left.AudPerMwhGenerated + right.AudPerMwhGenerated);

    /// <summary>Operating cost of generating <paramref name="generatedEnergy"/> at this rate.</summary>
    public static Money operator *(GenerationEnergyCost cost, Energy generatedEnergy) =>
        cost.For(generatedEnergy);

    /// <summary>Operating cost of generating <paramref name="generatedEnergy"/> at this rate.</summary>
    public static Money operator *(Energy generatedEnergy, GenerationEnergyCost cost) =>
        cost * generatedEnergy;

    /// <summary>Orders this rate against another by AUD/MWh generated.</summary>
    public int CompareTo(GenerationEnergyCost other) =>
        AudPerMwhGenerated.CompareTo(other.AudPerMwhGenerated);
}