using FluentAssertions;
using NEM.Contracts;
using NEM.Web.Services;

namespace NEM.Web.Tests.Services;

public sealed class DispatchRunContextTests
{
    [Fact]
    public void Resolve_UsesIndexOrderForViewableAdjacentPoints()
    {
        DispatchRunContext? context = DispatchRunContextResolver.Resolve(Index(
            Point("p0"),
            Point("failed", status: SweepPointStatus.Failed, detailPath: null),
            Point("p2")),
            "test-sweep",
            "p2");

        context.Should().NotBeNull();
        context!.PreviousPoint!.PointId.Should().Be("p0");
        context.NextPoint.Should().BeNull();
    }

    [Theory]
    [InlineData("unknown", "p0")]
    [InlineData("test-sweep", "unknown")]
    [InlineData("test-sweep", "failed")]
    public void Resolve_ReturnsNullForAnUnknownOrNonViewablePoint(string sweepId, string pointId)
    {
        DispatchRunContext? context = DispatchRunContextResolver.Resolve(Index(
            Point("p0"),
            Point("failed", status: SweepPointStatus.Failed, detailPath: null)),
            sweepId,
            pointId);

        context.Should().BeNull();
    }

    [Fact]
    public void ResolveReferencedArtifactPath_ResolvesTheDeclaredSeriesWithinTheSweep()
    {
        DispatchRunContext context = DispatchRunContextResolver.Resolve(Index(Point("p0")), "test-sweep", "p0")!;

        context.DetailArtifactPath.Should().Be("data/sweeps/test-sweep/points/p0.json");
        context.ResolveReferencedArtifactPath("../series/base-demand.json")
            .Should().Be("data/sweeps/test-sweep/series/base-demand.json");
        context.ResolveReferencedArtifactPath("../../outside.json").Should().BeNull();
        context.ConfigRepositoryUrl.Should().Contain("configs/p0.json");
    }

    private static SweepIndexDTO Index(params SweepIndexPointDTO[] points) =>
        ArtifactFixtures.Index(points) with { SweepId = "test-sweep" };

    private static SweepIndexPointDTO Point(
        string pointId,
        SweepPointStatus status = SweepPointStatus.Succeeded,
        string? detailPath = "points/p0.json") =>
        status == SweepPointStatus.Succeeded
            ? ArtifactFixtures.SucceededPoint(pointId, pointId, 0) with { DetailPath = detailPath }
            : ArtifactFixtures.FailedPoint(pointId, pointId, 0, "Constraint reached.");
}
