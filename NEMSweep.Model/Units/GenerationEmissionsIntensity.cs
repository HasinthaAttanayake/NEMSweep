namespace NEMSweep.Model.Units;

/// <summary>
/// Greenhouse gas released per MWh of electricity <em>generated</em>, in tonnes of carbon dioxide
/// equivalent (t CO2-e/MWh generated).
/// <para>
/// This is a technology assumption, on the same gross basis as <see cref="HeatRate"/> and
/// <see cref="GenerationEnergyCost"/>: it is charged on what a fleet generated, not on what reached
/// load, so energy a generator produced to charge storage still carries the emissions of
/// generating it. Combined with an <see cref="Energy"/> it produces a quantity of
/// <see cref="Emissions"/>.
/// </para>
/// <para>
/// Deliberately a separate type from <see cref="ServedEmissionsIntensity"/>, which is an outcome
/// per MWh served. The two share a unit name but not a denominator, and the same distinction
/// already separates <see cref="GenerationEnergyCost"/> from <see cref="EnergyPrice"/>.
/// </para>
/// </summary>
public readonly record struct GenerationEmissionsIntensity
{
    private GenerationEmissionsIntensity(double tonnesCO2ePerMwhGenerated) =>
        TonnesCO2ePerMwhGenerated = tonnesCO2ePerMwhGenerated;

    /// <summary>The intensity in tonnes CO2-e per MWh generated. Never negative.</summary>
    public double TonnesCO2ePerMwhGenerated { get; }

    /// <summary>An intensity of zero, for a technology that emits nothing when it runs.</summary>
    public static GenerationEmissionsIntensity Zero { get; } = new(0);

    /// <summary>Creates a non-negative, finite generation emissions intensity in t CO2-e/MWh.</summary>
    public static GenerationEmissionsIntensity FromTonnesCO2ePerMwhGenerated(
        double tonnesCO2ePerMwhGenerated)
    {
        if (!double.IsFinite(tonnesCO2ePerMwhGenerated) || tonnesCO2ePerMwhGenerated < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tonnesCO2ePerMwhGenerated),
                tonnesCO2ePerMwhGenerated,
                "Generation emissions intensity must be a finite, non-negative number of "
                + "t CO2-e/MWh generated.");
        }

        return new GenerationEmissionsIntensity(tonnesCO2ePerMwhGenerated);
    }

    /// <summary>
    /// Emissions from generating <paramref name="energy"/> at this intensity:
    /// t CO2-e = t CO2-e/MWh generated × MWh generated. The energy must not be negative, because
    /// emissions cannot be un-released.
    /// </summary>
    public Emissions For(Energy energy)
    {
        if (energy.MegawattHours < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(energy),
                energy.MegawattHours,
                "Emissions cannot be derived from negative generated energy.");
        }

        return Emissions.FromTonnesCO2e(TonnesCO2ePerMwhGenerated * energy.MegawattHours);
    }
}
