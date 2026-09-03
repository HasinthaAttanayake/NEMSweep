namespace NEMSweep.Model.Units;

/// <summary>
/// Greenhouse gas released per MWh of electricity <em>served</em>, in tonnes of carbon dioxide
/// equivalent (t CO2-e/MWh served).
/// <para>
/// This is the published outcome of an accounted run, not an input assumption: emissions divided by
/// the same energy-served denominator every levelised cost uses, which is what makes an emissions
/// intensity and an <see cref="EnergyPrice"/> for the same run comparable figures.
/// </para>
/// <para>
/// Deliberately a separate type from <see cref="GenerationEmissionsIntensity"/>. A rate per MWh
/// generated and a rate per MWh served are not interchangeable, and keeping them apart is what
/// stops a technology assumption being assigned where a result belongs, exactly as
/// <see cref="GenerationEnergyCost"/> is kept apart from <see cref="EnergyPrice"/>.
/// </para>
/// </summary>
public readonly record struct ServedEmissionsIntensity
{
    private ServedEmissionsIntensity(double tonnesCO2ePerMwhServed) =>
        TonnesCO2ePerMwhServed = tonnesCO2ePerMwhServed;

    /// <summary>The intensity in tonnes CO2-e per MWh served. Never negative.</summary>
    public double TonnesCO2ePerMwhServed { get; }

    /// <summary>An intensity of zero, for a system that served load without emitting.</summary>
    public static ServedEmissionsIntensity Zero { get; } = new(0);

    /// <summary>Creates a non-negative, finite served emissions intensity in t CO2-e/MWh.</summary>
    public static ServedEmissionsIntensity FromTonnesCO2ePerMwhServed(
        double tonnesCO2ePerMwhServed)
    {
        if (!double.IsFinite(tonnesCO2ePerMwhServed) || tonnesCO2ePerMwhServed < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tonnesCO2ePerMwhServed),
                tonnesCO2ePerMwhServed,
                "Served emissions intensity must be a finite, non-negative number of "
                + "t CO2-e/MWh served.");
        }

        return new ServedEmissionsIntensity(tonnesCO2ePerMwhServed);
    }

    /// <summary>Sums two shares of one system's intensity, which share a denominator.</summary>
    public static ServedEmissionsIntensity operator +(
        ServedEmissionsIntensity a,
        ServedEmissionsIntensity b) =>
        FromTonnesCO2ePerMwhServed(a.TonnesCO2ePerMwhServed + b.TonnesCO2ePerMwhServed);
}
