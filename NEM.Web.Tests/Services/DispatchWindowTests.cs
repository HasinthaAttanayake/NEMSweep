using AwesomeAssertions;
using NEM.Web.Services;

namespace NEM.Web.Tests.Services;

public sealed class DispatchWindowTests
{
    private static readonly DateTimeOffset Start = new(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));
    private static readonly TimeSpan Hourly = TimeSpan.FromHours(1);

    [Fact]
    public void Create_KeepsOnlyTheIntervalsTheSelectionCovers()
    {
        DispatchWindow window = DispatchWindow.Create(Start, Hourly, 48, IsSecondDay, 480);

        window.IntervalCount.Should().Be(24);
        window.Indexes[0].Should().Be(24);
        window.Start.Should().Be(Start.AddHours(24));
        window.End.Should().Be(Start.AddHours(48));
    }

    [Fact]
    public void Create_DrawsEveryIntervalWhenTheyFitWithinTheTarget()
    {
        DispatchWindow window = DispatchWindow.Create(Start, Hourly, 24, _ => true, 480);

        window.BucketSize.Should().Be(1);
        window.PointCount.Should().Be(24);
    }

    [Fact]
    public void Create_CollapsesAYearOfIntervalsToTheRequestedPointCount()
    {
        DispatchWindow window = DispatchWindow.Create(Start, Hourly, 8760, _ => true, 480);

        window.BucketSize.Should().Be(19);
        window.PointCount.Should().Be(462);
        window.Timestamps[0].Should().Be(Start);
    }

    [Fact]
    public void Create_ReturnsAnEmptyWindowWhenNothingIsSelected()
    {
        DispatchWindow window = DispatchWindow.Create(Start, Hourly, 24, _ => false, 480);

        window.IsEmpty.Should().BeTrue();
        window.PointCount.Should().Be(0);
        window.Average([1, 2, 3]).Should().BeEmpty();
    }

    [Fact]
    public void Average_TakesTheMeanOfEachBucket()
    {
        DispatchWindow window = DispatchWindow.Create(Start, Hourly, 4, _ => true, 2);

        window.Average([1, 3, 10, 20]).Should().Equal(2, 15);
    }

    /// <summary>
    /// A three-hour shortfall inside a nineteen-hour bucket disappears under an average, so the
    /// series that answer "did this ever happen" are reduced by peak instead.
    /// </summary>
    [Fact]
    public void Peak_KeepsAShortEventThatAnAverageWouldHide()
    {
        DispatchWindow window = DispatchWindow.Create(Start, Hourly, 4, _ => true, 1);

        window.Peak([0, 0, 400, 0]).Should().Equal(400);
        window.Average([0, 0, 400, 0]).Should().Equal(100);
    }

    [Fact]
    public void First_TakesTheValueTheBucketStartedAt()
    {
        DispatchWindow window = DispatchWindow.Create(Start, Hourly, 4, _ => true, 2);

        window.First([5, 9, 11, 2]).Should().Equal(5, 11);
    }

    [Fact]
    public void Maximum_ReadsTheUnbucketedPeakAcrossTheWindow()
    {
        DispatchWindow window = DispatchWindow.Create(Start, Hourly, 48, IsSecondDay, 4);

        double[] series = new double[48];
        series[30] = 900;
        series[10] = 5000;

        window.Maximum(series).Should().Be(900);
    }

    [Fact]
    public void Integrate_MultipliesByTheIntervalLengthRatherThanSummingPowers()
    {
        DispatchWindow window = DispatchWindow.Create(Start, TimeSpan.FromHours(2), 3, _ => true, 480);

        window.Integrate([100, 100, 100], TimeSpan.FromHours(2)).Should().Be(600);
    }

    /// <summary>
    /// Regions are drawn against one window built from the system series. A region whose artifact
    /// carries fewer intervals must not throw the comparison off a cliff.
    /// </summary>
    [Fact]
    public void Reduce_TreatsAShorterSeriesAsZeroPastItsEnd()
    {
        DispatchWindow window = DispatchWindow.Create(Start, Hourly, 4, _ => true, 4);

        window.Average([1, 2]).Should().Equal(1, 2, 0, 0);
        window.Maximum([1, 2]).Should().Be(2);
    }

    [Fact]
    public void Reduce_ReturnsZerosForAMissingSeries()
    {
        DispatchWindow window = DispatchWindow.Create(Start, Hourly, 4, _ => true, 4);

        window.Average(null).Should().Equal(0, 0, 0, 0);
        window.Integrate(null, Hourly).Should().Be(0);
    }

    private static bool IsSecondDay(DateTimeOffset instant) => instant.Date == Start.AddDays(1).Date;
}
