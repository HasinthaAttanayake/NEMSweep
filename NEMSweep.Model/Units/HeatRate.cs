namespace NEMSweep.Model.Units;

/// <summary>Thermal energy required to generate one MWh of electricity, in GJ/MWh.</summary>
public readonly record struct HeatRate
{
    private HeatRate(double gigajoulesPerMegawattHour) =>
        GigajoulesPerMegawattHour = gigajoulesPerMegawattHour;

    /// <summary>Thermal energy input in GJ required per MWh generated.</summary>
    public double GigajoulesPerMegawattHour { get; }

    /// <summary>Creates a non-negative, finite heat rate in GJ/MWh.</summary>
    public static HeatRate FromGigajoulesPerMegawattHour(double gigajoulesPerMegawattHour)
    {
        if (double.IsNaN(gigajoulesPerMegawattHour)
            || double.IsInfinity(gigajoulesPerMegawattHour)
            || gigajoulesPerMegawattHour < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gigajoulesPerMegawattHour));
        }

        return new HeatRate(gigajoulesPerMegawattHour);
    }
}