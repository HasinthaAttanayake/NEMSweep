namespace NEM.Model.Units;

/// <summary>Generation operating cost in AUD per MWh generated.</summary>
public readonly record struct GenerationEnergyCost : IComparable<GenerationEnergyCost>
{
    private GenerationEnergyCost(decimal audPerMwhGenerated) =>
        AudPerMwhGenerated = audPerMwhGenerated;

    public decimal AudPerMwhGenerated { get; }

    public static GenerationEnergyCost Zero { get; } = new(0m);

    public static GenerationEnergyCost FromAudPerMwhGenerated(decimal audPerMwhGenerated) =>
        new(audPerMwhGenerated);

    public Money For(Energy generatedEnergy) => Money.FromAud(
        AudPerMwhGenerated * DecimalPhysicalBoundary.RequireNonNegativeFinite(
            generatedEnergy.MegawattHours,
            nameof(generatedEnergy)));

    public static GenerationEnergyCost operator +(
        GenerationEnergyCost left,
        GenerationEnergyCost right) =>
        FromAudPerMwhGenerated(left.AudPerMwhGenerated + right.AudPerMwhGenerated);

    public static Money operator *(GenerationEnergyCost cost, Energy generatedEnergy) =>
        cost.For(generatedEnergy);

    public static Money operator *(Energy generatedEnergy, GenerationEnergyCost cost) =>
        cost * generatedEnergy;

    public int CompareTo(GenerationEnergyCost other) =>
        AudPerMwhGenerated.CompareTo(other.AudPerMwhGenerated);
}