using NEMSweep.Model.Series;

namespace NEMSweep.Model.Weather;

/// <summary>
/// Calculated geometric solar zenith angles for aligned intervals. Each value is
/// calculated at the interval midpoint so it represents the same period as an
/// interval-integrated EPW radiation value.
/// </summary>
public sealed class SolarZenithSeries : TimeSeries
{
    private SolarZenithSeries(
        DateTimeOffset start,
        TimeSpan resolution,
        double[] degrees,
        double latitude,
        double longitude)
        : base(start, resolution, degrees)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>Latitude in decimal degrees used to calculate this series.</summary>
    public double Latitude { get; }

    /// <summary>Longitude in decimal degrees used to calculate this series.</summary>
    public double Longitude { get; }

    /// <summary>Solar zenith angle at <paramref name="index"/>.</summary>
    public SolarZenith this[int index] => SolarZenith.FromDegrees(RawValue(index));

    /// <summary>
    /// Calculates solar zenith angles for every interval midpoint between
    /// <paramref name="start"/> and <paramref name="length"/> intervals later, at
    /// <paramref name="resolution"/>, for the given location.
    /// </summary>
    /// <param name="start">Start of the first interval.</param>
    /// <param name="resolution">Interval duration; must be positive.</param>
    /// <param name="length">Number of intervals to calculate; must be positive.</param>
    /// <param name="latitude">Latitude in degrees; must be finite and within [-90, +90].</param>
    /// <param name="longitude">Longitude in degrees; must be finite and within [-180, +180].</param>
    public static SolarZenithSeries Calculate(
        DateTimeOffset start,
        TimeSpan resolution,
        int length,
        double latitude,
        double longitude)
    {
        NemTime.Require(start, nameof(start));

        if (resolution <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolution), resolution, "Resolution must be positive.");
        }

        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length), length, "Length must be positive.");
        }

        var values = new double[length];
        TimeSpan midpointOffset = TimeSpan.FromTicks(resolution.Ticks / 2);
        for (int index = 0; index < length; index++)
        {
            DateTimeOffset midpoint = start
                + TimeSpan.FromTicks(resolution.Ticks * index)
                + midpointOffset;
            values[index] = SolarZenith.At(latitude, longitude, midpoint).Degrees;
        }

        return new SolarZenithSeries(
            start, resolution, values, latitude, longitude);
    }
}
