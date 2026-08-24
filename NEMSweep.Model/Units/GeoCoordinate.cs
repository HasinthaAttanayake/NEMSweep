namespace NEMSweep.Model.Units;

/// <summary>A point on the Earth's surface in decimal degrees.</summary>
public readonly record struct GeoCoordinate
{
    /// <summary>Mean Earth radius in kilometres, used for great-circle distance.</summary>
    private const double EarthRadiusKilometres = 6371.0088;

    /// <summary>Latitude in decimal degrees, in [-90, +90].</summary>
    public double Latitude { get; }

    /// <summary>Longitude in decimal degrees, in [-180, +180].</summary>
    public double Longitude { get; }

    private GeoCoordinate(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>Creates a <see cref="GeoCoordinate"/> from a latitude and longitude in decimal degrees.</summary>
    /// <param name="latitude">Latitude in degrees; must be finite and within [-90, +90].</param>
    /// <param name="longitude">Longitude in degrees; must be finite and within [-180, +180].</param>
    public static GeoCoordinate FromDegrees(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude < -90 || latitude > 90)
        {
            throw new ArgumentOutOfRangeException(
                nameof(latitude), latitude,
                "Latitude must be a finite value between -90 and +90 degrees.");
        }

        if (!double.IsFinite(longitude) || longitude < -180 || longitude > 180)
        {
            throw new ArgumentOutOfRangeException(
                nameof(longitude), longitude,
                "Longitude must be a finite value between -180 and +180 degrees.");
        }

        return new GeoCoordinate(latitude, longitude);
    }

    /// <summary>
    /// Great-circle distance to <paramref name="other"/> using the haversine formula over a
    /// mean-radius spherical Earth model.
    /// </summary>
    public Distance DistanceTo(GeoCoordinate other)
    {
        double latitude1 = DegreesToRadians(Latitude);
        double latitude2 = DegreesToRadians(other.Latitude);
        double deltaLatitude = DegreesToRadians(other.Latitude - Latitude);
        double deltaLongitude = DegreesToRadians(other.Longitude - Longitude);

        double a = Math.Sin(deltaLatitude / 2) * Math.Sin(deltaLatitude / 2)
            + Math.Cos(latitude1) * Math.Cos(latitude2)
                * Math.Sin(deltaLongitude / 2) * Math.Sin(deltaLongitude / 2);
        double centralAngle = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return Distance.FromKilometres(EarthRadiusKilometres * centralAngle);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
