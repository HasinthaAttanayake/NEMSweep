using AwesomeAssertions;
using NEMSweep.Model.Scenarios;

namespace NEMSweep.Model.Tests.Scenarios;

public sealed class CostBasisTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public void Constructor_RejectsYearOutsideDateRange(int year)
    {
        var act = () => new CostBasis(year, 0.07m);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("year");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-1.01)]
    public void Constructor_RejectsRealDiscountRateAtOrBelowNegativeOne(decimal rate)
    {
        var act = () => new CostBasis(2026, rate);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("realDiscountRate");
    }

    [Fact]
    public void ValueEquality_UsesYearAndRealDiscountRate()
    {
        new CostBasis(2026, 0.07m).Should().Be(new CostBasis(2026, 0.07m));
    }
}