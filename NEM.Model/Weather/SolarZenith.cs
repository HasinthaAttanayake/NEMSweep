namespace NEM.Model.Weather
{
    /// <summary>
    /// Geometric solar zenith angle in degrees. Zero degrees is directly overhead
    /// and 90 degrees is the astronomical horizon.
    /// </summary>
    public readonly record struct SolarZenith : IComparable<SolarZenith>
    {
        /// <summary>The angle in degrees, in [0, 180].</summary>
        public double Degrees { get; }

        private SolarZenith(double degrees)
        {
            Degrees = degrees;
        }

        /// <summary>Creates a <see cref="SolarZenith"/> from a value in degrees, in [0, 180].</summary>
        public static SolarZenith FromDegrees(double degrees)
        {
            if (!double.IsFinite(degrees) || degrees is < 0 or > 180)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(degrees), degrees,
                    "Solar zenith must be a finite value between 0 and 180 degrees.");
            }

            return new SolarZenith(degrees);
        }

        /// <summary>
        /// Calculates the geometric solar zenith angle for a location and instant
        /// using NOAA's General Solar Position Calculations equations.
        /// </summary>
        public static SolarZenith At(double latitude, double longitude, DateTimeOffset timeOfDay)
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

            int daysInYear = DateTime.IsLeapYear(timeOfDay.Year) ? 366 : 365;
            double fractionalHour = timeOfDay.Hour
                + timeOfDay.Minute / 60.0
                + timeOfDay.Second / 3600.0
                + timeOfDay.Millisecond / 3_600_000.0;
            double fractionalYear = 2 * Math.PI / daysInYear
                * (timeOfDay.DayOfYear - 1 + (fractionalHour - 12) / 24);

            double equationOfTime = 229.18 * (
                0.000075
                + 0.001868 * Math.Cos(fractionalYear)
                - 0.032077 * Math.Sin(fractionalYear)
                - 0.014615 * Math.Cos(2 * fractionalYear)
                - 0.040849 * Math.Sin(2 * fractionalYear));

            double declination = 0.006918
                - 0.399912 * Math.Cos(fractionalYear)
                + 0.070257 * Math.Sin(fractionalYear)
                - 0.006758 * Math.Cos(2 * fractionalYear)
                + 0.000907 * Math.Sin(2 * fractionalYear)
                - 0.002697 * Math.Cos(3 * fractionalYear)
                + 0.00148 * Math.Sin(3 * fractionalYear);

            double timeOffset = equationOfTime
                + 4 * longitude
                - 60 * timeOfDay.Offset.TotalHours;
            double trueSolarTime = fractionalHour * 60 + timeOffset;
            double hourAngle = DegreesToRadians(trueSolarTime / 4 - 180);
            double latitudeRadians = DegreesToRadians(latitude);

            double cosineZenith = Math.Sin(latitudeRadians) * Math.Sin(declination)
                + Math.Cos(latitudeRadians) * Math.Cos(declination) * Math.Cos(hourAngle);

            return FromDegrees(RadiansToDegrees(Math.Acos(Math.Clamp(cosineZenith, -1, 1))));
        }

        /// <summary>Orders this angle against another by degrees.</summary>
        public int CompareTo(SolarZenith other) => Degrees.CompareTo(other.Degrees);

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
        private static double RadiansToDegrees(double radians) => radians * 180 / Math.PI;
    }
}