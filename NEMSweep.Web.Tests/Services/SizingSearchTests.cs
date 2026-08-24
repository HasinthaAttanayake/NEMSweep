using AwesomeAssertions;
using NEMSweep.Contracts;
using NEMSweep.Web.Services.Insights;

namespace NEMSweep.Web.Tests.Services;

public sealed class SizingSearchTests
{
    [Fact]
    public void From_OrdersPassesBySequenceRatherThanByPublicationOrder()
    {
        SizingSearch search = SizingSearch.From(Sizing(
            Pass(3, 12_287, 738),
            Pass(1, 8_758.4, 3_526.1),
            Pass(2, 12_287, 738)));

        search.Passes.Select(pass => pass.Pass).Should().Equal(1, 2, 3);
        search.First.EnergyCapacityMwh.Should().Be(8_758.4);
        search.Last.Pass.Should().Be(3);
    }

    [Fact]
    public void HasPath_IsFalseForASingleDispatchBecauseOnePointIsNotACurve()
    {
        SizingSearch.From(Sizing(Pass(1, 5_515, 0))).HasPath.Should().BeFalse();
        SizingSearch.From(null).HasPath.Should().BeFalse();
        SizingSearch.From(null).Should().BeSameAs(SizingSearch.Empty);
    }

    [Fact]
    public void HasPath_IsFalseWhenEveryPassRanTheSameFleetToTheSameResult()
    {
        // A region the system's sizing loop carried along without changing dispatches several
        // times and moves nothing. Drawing that stacks three markers on one point.
        SizingSearch.From(Sizing(Pass(1, 5_515, 0), Pass(2, 5_515, 0), Pass(3, 5_515, 0)))
            .HasPath.Should().BeFalse();
    }

    [Fact]
    public void HasPath_IsTrueWhenUnservedEnergyMovedEvenIfCapacityHeldStill()
    {
        SizingSearch.From(Sizing(Pass(1, 5_515, 400), Pass(2, 5_515, 0)))
            .HasPath.Should().BeTrue();
    }

    [Fact]
    public void AddedEnergy_AndRemovedUnserved_ArePricedAcrossTheWholeSearch()
    {
        SizingSearch search = SizingSearch.From(Sizing(
            Pass(1, 8_758.4, 3_526.1),
            Pass(2, 12_287, 738),
            Pass(3, 12_287, 738)));

        search.GrewStorage.Should().BeTrue();
        search.AddedEnergyMwh.Should().BeApproximately(3_528.6, 0.001);
        search.RemovedUnservedMwh.Should().BeApproximately(2_788.1, 0.001);
        search.UnservedRemovedPerMwhAdded.Should().BeApproximately(0.790, 0.001);
    }

    [Fact]
    public void UnservedRemovedPerMwhAdded_IsZeroRatherThanInfiniteWhenNoStorageWasAdded()
    {
        SizingSearch search = SizingSearch.From(Sizing(
            Pass(1, 5_515, 0),
            Pass(2, 5_515, 0)));

        search.GrewStorage.Should().BeFalse();
        search.UnservedRemovedPerMwhAdded.Should().Be(0);
    }

    [Fact]
    public void RepeatedCapacityPasses_CountsProbesTheSearchDidNotAcceptAsProbesNotSteps()
    {
        SizingSearch search = SizingSearch.From(Sizing(
            Pass(1, 8_758.4, 3_526.1),
            Pass(2, 12_287, 738),
            Pass(3, 12_287, 738)));

        // Pass 3 re-ran the capacity pass 2 had already reached: the loop dispatched it and kept
        // the fleet it had, which is not a third step in the search.
        search.RepeatedCapacityPasses.Should().Be(1);
    }

    [Fact]
    public void RepeatedCapacityPasses_IsZeroForASearchThatMovedAtEveryPass()
    {
        SizingSearch search = SizingSearch.From(Sizing(
            Pass(1, 1_000, 500),
            Pass(2, 2_000, 200),
            Pass(3, 3_000, 0)));

        search.RepeatedCapacityPasses.Should().Be(0);
    }

    private static StorageSizingPassDTO Pass(int pass, double energyMwh, double unservedMwh) =>
        new(pass, energyMwh, 940, unservedMwh, unservedMwh > 0 ? 2 : 0);

    private static StorageSizingOutcomeDTO Sizing(params StorageSizingPassDTO[] trajectory) =>
        ArtifactFixtures.Sizing() with { Trajectory = trajectory };
}
