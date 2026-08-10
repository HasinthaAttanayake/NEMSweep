using System.Net;
using System.Text;
using FluentAssertions;
using NEM.Contracts;
using NEM.Web.Services;

namespace NEM.Web.Tests.Services;

public sealed class SweepManifestLoaderTests
{
    [Fact]
    public async Task LoadAsync_ReturnsTheSweepsTheManifestLists()
    {
        ArtifactLoadResult<SweepManifestDTO> result = await LoadAsync(
            $$"""
            {
              "schemaVersion": {{ArtifactSchemaVersions.SweepManifest}},
              "sweeps": [
                { "sweepId": "one", "name": "One", "indexPath": "one/index.json" },
                { "sweepId": "two", "name": "Two", "indexPath": "two/index.json" }
              ]
            }
            """);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Sweeps.Select(sweep => sweep.SweepId).Should().Equal("one", "two");
    }

    [Fact]
    public async Task LoadAsync_AcceptsAManifestWithNoSweeps()
    {
        ArtifactLoadResult<SweepManifestDTO> result = await LoadAsync(
            $$"""{ "schemaVersion": {{ArtifactSchemaVersions.SweepManifest}}, "sweeps": [] }""");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Sweeps.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_ReturnsInvalidDataForAnUnsupportedSchema()
    {
        ArtifactLoadResult<SweepManifestDTO> result = await LoadAsync("""{ "schemaVersion": 99 }""");

        result.State.Status.Should().Be(ArtifactLoadStatus.InvalidData);
        result.State.Message.Should().Contain("schema 99 is not supported");
    }

    [Theory]
    [InlineData("""{ "sweeps": null }""", "Sweep manifest entries are missing.")]
    [InlineData("""{ "sweeps": [{ "sweepId": "one", "name": "One" }] }""", "A sweep manifest entry is incomplete.")]
    [InlineData("""{ "sweeps": [{ "sweepId": "", "name": "One", "indexPath": "a.json" }] }""", "A sweep manifest entry is incomplete.")]
    [InlineData(
        """{ "sweeps": [{ "sweepId": "one", "name": "One", "indexPath": "a.json" }, { "sweepId": "one", "name": "Dup", "indexPath": "b.json" }] }""",
        "Sweep manifest entry 'one' is duplicated.")]
    public async Task LoadAsync_ReturnsInvalidDataForAMalformedManifest(string body, string expectedMessage)
    {
        string json = $$"""{ "schemaVersion": {{ArtifactSchemaVersions.SweepManifest}}, """
            + body.TrimStart('{');

        ArtifactLoadResult<SweepManifestDTO> result = await LoadAsync(json);

        result.State.Status.Should().Be(ArtifactLoadStatus.InvalidData);
        result.Value.Should().BeNull();
        result.State.Message.Should().Be(expectedMessage);
    }

    [Fact]
    public void SweepPaths_BuildsArtifactAndRouteLocations()
    {
        SweepPaths.IndexPath("a b").Should().Be("data/sweeps/a%20b/index.json");
        SweepPaths.IndexPath(new SweepManifestEntryDTO("a b", "A B", "a b/index.json"))
            .Should().Be("data/sweeps/a b/index.json");
        SweepPaths.DetailPath("a b", "points/p0.json").Should().Be("data/sweeps/a%20b/points/p0.json");
        SweepPaths.PageRoute("a b").Should().Be("/sweeps/a%20b");
        SweepPaths.RunRoute("a b", "p 1").Should().Be("/runs/a%20b/p%201");
    }

    private static async Task<ArtifactLoadResult<SweepManifestDTO>> LoadAsync(string json)
    {
        using var http = new HttpClient(new StaticJsonHandler(json))
        {
            BaseAddress = new Uri("https://example.test/"),
        };

        return await new SweepManifestLoader(new ArtifactLoader(http)).LoadAsync();
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }
}
