using AwesomeAssertions;
using NEM.Web.Components.Viz;

namespace NEM.Web.Tests.Components;

public sealed class PlotGeometryTests
{
    [Fact]
    public void Nice_PlacesTicksOnRoundValues()
    {
        PlotAxis axis = PlotAxis.Nice(0, 173.53);

        axis.Ticks.Should().OnlyContain(tick => tick % 50 == 0);
        axis.Maximum.Should().BeGreaterThanOrEqualTo(173.53);
        axis.Minimum.Should().Be(0);
    }

    [Fact]
    public void Nice_KeepsASmallFractionalRangeReadableRatherThanRoundingItAway()
    {
        PlotAxis axis = PlotAxis.Nice(0, 0.4, includeZero: false);

        axis.Ticks.Should().HaveCountGreaterThan(2);
        axis.TickDecimals.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Nice_AnchorsAtZeroByDefaultSoASmallMovementIsNotDrawnAsACollapse()
    {
        PlotAxis axis = PlotAxis.Nice(130.69, 173.53);

        axis.Minimum.Should().Be(0);
    }

    [Fact]
    public void Nice_FramesTheDataWhenZeroIsNotRequested()
    {
        PlotAxis axis = PlotAxis.Nice(130.69, 173.53, includeZero: false);

        axis.Minimum.Should().BeGreaterThan(0);
        axis.Minimum.Should().BeLessThanOrEqualTo(130.69);
        axis.Maximum.Should().BeGreaterThanOrEqualTo(173.53);
    }

    [Fact]
    public void Nice_GivesAFlatSeriesAnAxisWithHeight()
    {
        PlotAxis axis = PlotAxis.Nice(5, 5, includeZero: false);

        axis.Maximum.Should().BeGreaterThan(axis.Minimum);
        axis.Fraction(5).Should().BeInRange(0, 1);
    }

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(0, double.PositiveInfinity)]
    public void Nice_FallsBackRatherThanProducingAnUnusableAxis(double minimum, double maximum)
    {
        PlotAxis axis = PlotAxis.Nice(minimum, maximum);

        axis.Ticks.Should().NotBeEmpty();
        axis.Maximum.Should().BeGreaterThan(axis.Minimum);
    }

    [Fact]
    public void Segments_BreaksTheLineWhereAValueIsMissingRatherThanDroppingToZero()
    {
        PlotBox box = new(0, 0, 100, 100);
        PlotAxis axis = PlotAxis.Nice(0, 10);

        IReadOnlyList<string> segments = PlotPath.Segments([1, 2, null, 4, 5], box, axis);

        segments.Should().HaveCount(2);
        segments[0].Split(' ').Should().HaveCount(2);
        segments[1].Split(' ').Should().HaveCount(2);
    }

    [Fact]
    public void Segments_ProducesNothingForASeriesWithNoValues()
    {
        PlotPath.Segments([null, null], new PlotBox(0, 0, 100, 100), PlotAxis.Nice(0, 1))
            .Should().BeEmpty();
    }

    [Fact]
    public void X_CentresASinglePointRatherThanPinningItToTheLeftEdge()
    {
        PlotBox box = new(40, 0, 140, 100);

        PlotPath.X(0, 1, box).Should().Be(90);
    }

    [Fact]
    public void Y_ClampsAValueOutsideTheAxisToThePlotArea()
    {
        PlotBox box = new(0, 10, 100, 110);
        PlotAxis axis = PlotAxis.Nice(0, 10);

        PlotPath.Y(1000, box, axis).Should().Be(10);
        PlotPath.Y(-1000, box, axis).Should().Be(110);
    }

    [Fact]
    public void Band_ClosesThePolygonByReturningAlongTheLowerBoundary()
    {
        string band = PlotPath.Band([0, 0, 0], [1, 2, 3], new PlotBox(0, 0, 100, 100), PlotAxis.Nice(0, 3));

        band.Split(' ').Should().HaveCount(6);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(738, "738")]
    [InlineData(66_275_989, "66.3M")]
    [InlineData(17_629_045_241, "17.6B")]
    [InlineData(-2_500, "-2,500")]
    [InlineData(-266_101, "-266.1k")]
    public void Compact_ScalesByMagnitudeSoAnAxisLabelFits(double value, string expected)
    {
        PlotFormat.Compact(value).Should().Be(expected);
    }

    /// <summary>
    /// Storage capacities across regions straddle ten thousand megawatt-hours, and abbreviating
    /// from there printed 5,515 beside 12.3k in the same comparison.
    /// </summary>
    [Theory]
    [InlineData(5_515, "5,515")]
    [InlineData(6_772, "6,772")]
    [InlineData(12_287, "12,287")]
    public void Compact_KeepsOneUnitAcrossValuesThatStraddleTenThousand(double value, string expected)
    {
        PlotFormat.Compact(value).Should().Be(expected);
    }

    [Fact]
    public void Money_KeepsTheMinusSignOutsideTheCurrencySymbol()
    {
        PlotFormat.Money(-12.99m).Should().Be("-$12.99");
    }

    [Theory]
    [InlineData(17_629_045_241.79, "$17.63b")]
    [InlineData(295_667_863.65, "$295.67m")]
    [InlineData(-1_500, "-$1.50k")]
    public void MoneyTotal_AbbreviatesLargeTotals(decimal value, string expected)
    {
        PlotFormat.MoneyTotal(value).Should().Be(expected);
    }

    [Fact]
    public void Share_ReadsAFractionAsAPercentage()
    {
        PlotFormat.Share(0.3798).Should().Be("38.0%");
    }

    [Theory]
    [InlineData(500, "0.5s")]
    [InlineData(26_569.68, "26.6s")]
    [InlineData(65_502.67, "1m 5s")]
    [InlineData(953_300.92, "15m 53s")]
    public void Duration_UsesWholeSecondsUnderAMinuteAndMinutesAndSecondsAbove(
        double milliseconds, string expected)
    {
        PlotFormat.Duration(milliseconds).Should().Be(expected);
    }

    /// <summary>
    /// The nice-step family includes 2.5, so a step can be 0.25. Taking the precision from the
    /// step's magnitude gave one decimal, labelling consecutive ticks 0.2 and 0.3.
    /// </summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(50, 0)]
    [InlineData(0.5, 1)]
    [InlineData(0.25, 2)]
    [InlineData(0.025, 3)]
    public void TickDecimals_CountsWhatTheStepActuallyNeeds(double step, int expected)
    {
        new PlotAxis(0, step * 4, [0, step], step).TickDecimals.Should().Be(expected);
    }

    [Fact]
    public void TickDecimals_LabelsAQuarterStepAxisDistinctly()
    {
        PlotAxis axis = PlotAxis.Nice(0, 1, targetTicks: 4);

        string[] labels = [.. axis.Ticks.Select(tick => PlotFormat.Tick(tick, axis))];

        labels.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Coordinate_UsesInvariantSeparatorsSoAPathCannotBreakUnderAnotherCulture()
    {
        PlotFormat.Coordinate(1.5, 2.25).Should().Be("1.50,2.25");
    }
}
