using AwesomeAssertions;
using NEM.Model.Units;

namespace NEM.Model.Tests.Units;

public sealed class GeoCoordinateTests
{
    private const double Tolerance = 0.5;

    [Fact]
    public void DistanceTo_SamePoint_IsZero()
    {
        GeoCoordinate point = GeoCoordinate.FromDegrees(-33.9, 151.2);

        point.DistanceTo(point).Kilometres.Should().Be(0);
    }

    [Fact]
    public void DistanceTo_IsSymmetric()
    {
        GeoCoordinate nsw = GeoCoordinate.FromDegrees(-33.9, 151.2);
        GeoCoordinate vic = GeoCoordinate.FromDegrees(-37.8, 144.9);

        nsw.DistanceTo(vic).Kilometres.Should().Be(vic.DistanceTo(nsw).Kilometres);
    }

    [Fact]
    public void DistanceTo_QuarterOfEquator_IsAQuarterOfEarthsCircumference()
    {
        GeoCoordinate origin = GeoCoordinate.FromDegrees(0, 0);
        GeoCoordinate quarterAround = GeoCoordinate.FromDegrees(0, 90);

        // A quarter of the equator is a quarter of Earth's ~40,030 km mean circumference.
        origin.DistanceTo(quarterAround).Kilometres.Should().BeApproximately(10_007.5, Tolerance);
    }

    [Fact]
    public void DistanceTo_PoleToPole_IsHalfEarthsCircumference()
    {
        GeoCoordinate northPole = GeoCoordinate.FromDegrees(90, 0);
        GeoCoordinate southPole = GeoCoordinate.FromDegrees(-90, 0);

        northPole.DistanceTo(southPole).Kilometres.Should().BeApproximately(20_015.1, Tolerance);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(91)]
    [InlineData(-91)]
    public void FromDegrees_RejectsOutOfRangeLatitude(double latitude)
    {
        var act = () => GeoCoordinate.FromDegrees(latitude, 0);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("latitude");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(181)]
    [InlineData(-181)]
    public void FromDegrees_RejectsOutOfRangeLongitude(double longitude)
    {
        var act = () => GeoCoordinate.FromDegrees(0, longitude);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("longitude");
    }
}
