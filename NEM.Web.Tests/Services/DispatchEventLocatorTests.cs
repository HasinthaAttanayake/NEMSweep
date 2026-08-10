using AwesomeAssertions;
using NEM.Contracts;
using NEM.Web.Services;

namespace NEM.Web.Tests.Services;

public sealed class DispatchEventLocatorTests
{
    [Fact]
    public void Locate_TurnsTheArtifactsPointersIntoDates()
    {
        DispatchResultsDTO result = ArtifactFixtures.Results(
            intervals: 5,
            series: ArtifactFixtures.Series([0, 0, 535, 12, 0], [100, 900, 0, 0, 40], []),
            pointers: new IntervalPointersDTO(2, 1, 4));

        IReadOnlyList<DispatchEvent> events = DispatchEventLocator.Locate(result);

        events.Select(notable => notable.Key).Should().Equal(
            "peak-unserved", "peak-curtailment", "lowest-storage");
        events.Select(notable => notable.Index).Should().Equal(2, 1, 4);
        events[0].Instant.Should().Be(ArtifactFixtures.PeriodStart.AddHours(2));
    }

    [Fact]
    public void Locate_OmitsAnEventTheRunNeverExperienced()
    {
        DispatchResultsDTO result = ArtifactFixtures.Results(
            pointers: new IntervalPointersDTO(null, null, null));

        DispatchEventLocator.Locate(result).Should().BeEmpty();
    }

    [Fact]
    public void Locate_OffersOnlyThePointersTheArtifactCarries()
    {
        DispatchResultsDTO result = ArtifactFixtures.Results(
            pointers: new IntervalPointersDTO(null, 1, null));

        DispatchEventLocator.Locate(result).Select(notable => notable.Key)
            .Should().Equal("peak-curtailment");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void Locate_IgnoresAPointerOutsideThePeriod(int index)
    {
        // A pointer past the end would send the date filter somewhere the run does not cover.
        DispatchResultsDTO result = ArtifactFixtures.Results(
            intervals: 3,
            pointers: new IntervalPointersDTO(index, null, null));

        DispatchEventLocator.Locate(result).Should().BeEmpty();
    }
}
