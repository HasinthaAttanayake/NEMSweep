namespace NEMSweep.Model.Units;

/// <summary>
/// A quantity of greenhouse gas, in tonnes of carbon dioxide equivalent (t CO2-e).
/// <para>
/// Emissions sum across both technologies and regions, so <c>+</c> is defined. A quantity of gas
/// released cannot be negative, so unlike <see cref="Energy"/> this is unsigned: there is no
/// modelled sequestration, and a negative would silently offset another fleet's output.
/// </para>
/// </summary>
public readonly record struct Emissions : IComparable<Emissions>
{
    private Emissions(double tonnesCO2e) => TonnesCO2e = tonnesCO2e;

    /// <summary>The quantity in tonnes of carbon dioxide equivalent. Never negative.</summary>
    public double TonnesCO2e { get; }

    /// <summary>Zero emissions. Seed for summing a collection of emissions.</summary>
    public static Emissions Zero { get; } = new(0);

    /// <summary>Creates a non-negative, finite quantity of emissions in tonnes CO2-e.</summary>
    public static Emissions FromTonnesCO2e(double tonnesCO2e)
    {
        if (!double.IsFinite(tonnesCO2e) || tonnesCO2e < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tonnesCO2e),
                tonnesCO2e,
                "Emissions must be a finite, non-negative number of tonnes CO2-e.");
        }

        return new Emissions(tonnesCO2e);
    }

    /// <summary>Sums two quantities of emissions. Valid across both technologies and regions.</summary>
    public static Emissions operator +(Emissions a, Emissions b) =>
        FromTonnesCO2e(a.TonnesCO2e + b.TonnesCO2e);

    /// <summary>Scales a quantity of emissions by a dimensionless, non-negative factor.</summary>
    public static Emissions operator *(Emissions emissions, double factor) =>
        FromTonnesCO2e(emissions.TonnesCO2e * factor);

    /// <summary>Scales a quantity of emissions by a dimensionless, non-negative factor.</summary>
    public static Emissions operator *(double factor, Emissions emissions) => emissions * factor;

    /// <summary>
    /// Intensity of the load this energy served: t CO2-e/MWh served = t CO2-e ÷ MWh served. The
    /// energy is the denominator an intensity is quoted against, so it must be positive; there is
    /// no meaningful intensity for a system that served nothing.
    /// </summary>
    public ServedEmissionsIntensity Per(Energy energyServed)
    {
        if (energyServed.MegawattHours <= 0)
        {
            throw new DivideByZeroException(
                "Cannot derive an emissions intensity against zero or negative energy served.");
        }

        return ServedEmissionsIntensity.FromTonnesCO2ePerMwhServed(
            TonnesCO2e / energyServed.MegawattHours);
    }

    /// <summary>Whether the left quantity is less than the right.</summary>
    public static bool operator <(Emissions a, Emissions b) => a.TonnesCO2e < b.TonnesCO2e;

    /// <summary>Whether the left quantity is greater than the right.</summary>
    public static bool operator >(Emissions a, Emissions b) => a.TonnesCO2e > b.TonnesCO2e;

    /// <summary>Whether the left quantity is less than or equal to the right.</summary>
    public static bool operator <=(Emissions a, Emissions b) => a.TonnesCO2e <= b.TonnesCO2e;

    /// <summary>Whether the left quantity is greater than or equal to the right.</summary>
    public static bool operator >=(Emissions a, Emissions b) => a.TonnesCO2e >= b.TonnesCO2e;

    /// <summary>Orders this quantity against another by tonnes CO2-e.</summary>
    public int CompareTo(Emissions other) => TonnesCO2e.CompareTo(other.TonnesCO2e);
}
