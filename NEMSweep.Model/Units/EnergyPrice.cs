namespace NEMSweep.Model.Units;

/// <summary>
/// Money per unit of energy served, in AUD/MWh served.
/// <para>
/// This is the output unit of system levelised cost of electricity (SLCoE). It is
/// deliberately distinct from <see cref="GenerationEnergyCost"/>, which prices
/// gross generator output rather than energy served.
/// </para>
/// </summary>
public readonly record struct EnergyPrice
{
    /// <summary>The price in AUD per MWh served.</summary>
    public decimal AudPerMwhServed { get; }

    private EnergyPrice(decimal audPerMwhServed) =>
        AudPerMwhServed = audPerMwhServed;

    /// <summary>Creates a price from AUD per MWh served.</summary>
    public static EnergyPrice FromAudPerMwhServed(decimal audPerMwhServed) =>
        new(audPerMwhServed);

    /// <summary>
    /// Cost of energy served: AUD = AUD/MWh served × MWh served.
    /// Energy served must be non-negative and finite.
    /// </summary>
    public Money For(Energy energyServed) => Money.FromAud(
        AudPerMwhServed * DecimalPhysicalBoundary.RequireNonNegativeFinite(
            energyServed.MegawattHours,
            nameof(energyServed)));

    /// <summary>
    /// Cost of energy served: AUD = AUD/MWh served × MWh served.
    /// </summary>
    public static Money operator *(EnergyPrice price, Energy energyServed) =>
        price.For(energyServed);

    /// <summary>
    /// Cost of energy served: AUD = MWh served × AUD/MWh served.
    /// </summary>
    public static Money operator *(Energy energyServed, EnergyPrice price) =>
        price * energyServed;
}
