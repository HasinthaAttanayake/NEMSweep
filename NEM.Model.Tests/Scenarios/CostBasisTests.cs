using FluentAssertions;
using NEM.Model.Scenarios;

namespace NEM.Model.Tests.Scenarios;

public sealed class CostBasisTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public void Constructor_RejectsYearOutsideDateRange(int year)
    {
        var act = () => new CostBasis(year, 0.07);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("year");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void Constructor_RejectsInvalidRealDiscountRate(double rate)
    {
        var act = () => new CostBasis(2026, rate);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("realDiscountRate");
    }

    [Fact]
    public void ValueEquality_UsesYearAndRealDiscountRate()
    {
        new CostBasis(2026, 0.07).Should().Be(new CostBasis(2026, 0.07));
    }
}