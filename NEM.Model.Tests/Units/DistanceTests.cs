using AwesomeAssertions;
using NEM.Model.Units;

namespace NEM.Model.Tests.Units;

public sealed class DistanceTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    public void FromKilometres_RejectsNonFiniteOrNegativeValue(double value)
    {
        var act = () => Distance.FromKilometres(value);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("kilometres");
    }

    [Fact]
    public void Addition_SumsTwoDistances()
    {
        Distance total = Distance.FromKilometres(300) + Distance.FromKilometres(414);

        total.Kilometres.Should().Be(714);
    }

    [Fact]
    public void Zero_IsSeedForSumming()
    {
        Distance total = Distance.Zero + Distance.FromKilometres(120);

        total.Kilometres.Should().Be(120);
    }
}
