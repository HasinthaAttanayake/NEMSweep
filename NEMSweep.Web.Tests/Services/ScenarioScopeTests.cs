using AwesomeAssertions;
using NEMSweep.Contracts;
using NEMSweep.Web.Services;

namespace NEMSweep.Web.Tests.Services;

public sealed class ScenarioScopeTests
{
    private static SweepIndexDTO Sweep() => ArtifactFixtures.Index(
        ArtifactFixtures.SucceededPoint("p0", "Baseline", 0),
        ArtifactFixtures.FailedPoint("p1", "250 MW", 250, "sizing hit its bound"),
        ArtifactFixtures.SucceededPoint("p2", "500 MW", 500),
        ArtifactFixtures.SucceededPoint("p3", "750 MW", 750));

    [Theory]
    [InlineData("regions")]
    [InlineData("dispatch")]
    [InlineData("/regions")]
    public void Resolve_ReadsBothResultViewsAsTheBaselineScenario(string path)
    {
        ScenarioScope scope = ScenarioScope.Resolve(path, [Sweep()]);

        scope.Kind.Should().Be(ScenarioKind.Baseline);
        scope.Title.Should().Be("Baseline");
        scope.IsSingleRun.Should().BeTrue();
    }

    [Fact]
    public void Resolve_NamesASweepAndCountsOnlyItsViewableRuns()
    {
        ScenarioScope scope = ScenarioScope.Resolve("sweeps/test-sweep", [Sweep()]);

        scope.Kind.Should().Be(ScenarioKind.SweepOverview);
        scope.Title.Should().Be("Test sweep");
        // Four points, one of which failed and cannot be opened.
        scope.RunCount.Should().Be(3);
        scope.Detail.Should().Contain("3 scenarios");
        scope.IsSingleRun.Should().BeFalse();
    }

    [Fact]
    public void Resolve_NumbersARunAgainstTheViewableRunsOnly()
    {
        ScenarioScope scope = ScenarioScope.Resolve("runs/test-sweep/p2", [Sweep()]);

        scope.Kind.Should().Be(ScenarioKind.SweepRun);
        scope.Title.Should().Be("500 MW");
        // p1 failed, so p2 is the second run a reader can actually open, not the third.
        scope.RunNumber.Should().Be(2);
        scope.RunCount.Should().Be(3);
        scope.Detail.Should().Be("Added demand 500 MW");
    }

    [Fact]
    public void Resolve_StepsOverARunThatProducedNoResults()
    {
        ScenarioScope scope = ScenarioScope.Resolve("runs/test-sweep/p2", [Sweep()]);

        scope.PreviousRoute.Should().Be("/runs/test-sweep/p0");
        scope.NextRoute.Should().Be("/runs/test-sweep/p3");
    }

    [Fact]
    public void Resolve_CarriesTheRunsOwnRouteSoTheNavigationNeedNotRebuildIt()
    {
        ScenarioScope scope = ScenarioScope.Resolve("runs/test-sweep/p2", [Sweep()]);

        scope.RunRoute.Should().Be("/runs/test-sweep/p2");
        scope.SweepRoute.Should().Be("/sweeps/test-sweep");
    }

    [Fact]
    public void Resolve_LeavesTheRunRouteUnsetWhenNoRunIsOpen()
    {
        ScenarioScope.Resolve("regions", [Sweep()]).RunRoute.Should().BeNull();
        ScenarioScope.Resolve("sweeps/test-sweep", [Sweep()]).RunRoute.Should().BeNull();
    }

    [Fact]
    public void Resolve_OmitsStepsAtTheEndsOfASweep()
    {
        ScenarioScope first = ScenarioScope.Resolve("runs/test-sweep/p0", [Sweep()]);
        ScenarioScope last = ScenarioScope.Resolve("runs/test-sweep/p3", [Sweep()]);

        first.PreviousRoute.Should().BeNull();
        first.NextRoute.Should().Be("/runs/test-sweep/p2");
        last.NextRoute.Should().BeNull();
        last.PreviousRoute.Should().Be("/runs/test-sweep/p2");
    }

    [Fact]
    public void Resolve_RefusesToNumberARunThatProducedNoResults()
    {
        ScenarioScope scope = ScenarioScope.Resolve("runs/test-sweep/p1", [Sweep()]);

        scope.Kind.Should().Be(ScenarioKind.None);
        scope.RunNumber.Should().BeNull();
    }

    [Fact]
    public void Resolve_SaysAnInputBelongsToEveryScenarioRatherThanNamingOne()
    {
        ScenarioScope scope = ScenarioScope.Resolve("inputs/weather", [Sweep()]);

        scope.Kind.Should().Be(ScenarioKind.SharedInput);
        scope.Title.Should().Be("Weather");
        scope.Detail.Should().Contain("every scenario");
        scope.IsSingleRun.Should().BeFalse();
    }

    [Fact]
    public void Resolve_HandlesAnEscapedSweepIdInTheRoute()
    {
        ScenarioScope scope = ScenarioScope.Resolve("sweeps/test%2Dsweep", [Sweep()]);

        scope.Kind.Should().Be(ScenarioKind.SweepOverview);
        scope.Title.Should().Be("Test sweep");
    }

    [Fact]
    public void Resolve_FallsBackWhenTheSweepIsNotLoaded()
    {
        ScenarioScope scope = ScenarioScope.Resolve("sweeps/absent", [Sweep()]);

        scope.Kind.Should().Be(ScenarioKind.None);
        scope.Detail.Should().Be("Not found");
    }

    [Fact]
    public void Resolve_TreatsTheLandingPageAsNoScenario()
    {
        ScenarioScope scope = ScenarioScope.Resolve(string.Empty, []);

        scope.Kind.Should().Be(ScenarioKind.None);
        scope.IsSingleRun.Should().BeFalse();
    }
}
