using System.Net;
using System.Text;
using AwesomeAssertions;
using NEM.Contracts;
using NEM.Web.Services;

namespace NEM.Web.Tests.Services;

public sealed class ArtifactLoaderTests
{
    [Fact]
    public async Task LoadAsync_ReturnsSuccessForASupportedArtifact()
    {
        ArtifactLoadResult<GenerationInformationDTO> result = await LoadAsync<GenerationInformationDTO>(
            HttpStatusCode.OK,
            """{ "schemaVersion": 1 }""");

        result.IsSuccess.Should().BeTrue();
        result.Value!.SchemaVersion.Should().Be(1);
    }

    [Fact]
    public async Task LoadUnversionedAsync_LoadsAReferencedSeries()
    {
        using var http = new HttpClient(new StaticResponseHandler(
            HttpStatusCode.OK,
            """{ "start": "2026-01-01T00:00:00+10:00", "resolution": "01:00:00", "valuesMw": [1] }"""))
        {
            BaseAddress = new Uri("https://example.test/"),
        };

        ArtifactLoadResult<RegularSeriesDTO> result = await new ArtifactLoader(http)
            .LoadUnversionedAsync<RegularSeriesDTO>("data/sweeps/test/series/base-demand.json");

        result.IsSuccess.Should().BeTrue();
        result.Value!.ValuesMw.Should().Equal(1);
    }

    [Fact]
    public async Task LoadAsync_ReturnsNotFoundForAMissingArtifact()
    {
        ArtifactLoadResult<GenerationInformationDTO> result = await LoadAsync<GenerationInformationDTO>(
            HttpStatusCode.NotFound,
            "");

        result.State.Status.Should().Be(ArtifactLoadStatus.NotFound);
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_ReturnsInvalidDataForMalformedJson()
    {
        ArtifactLoadResult<GenerationInformationDTO> result = await LoadAsync<GenerationInformationDTO>(
            HttpStatusCode.OK,
            "{");

        result.State.Status.Should().Be(ArtifactLoadStatus.InvalidData);
        result.State.Message.Should().Be("Artifact is not valid JSON data.");
    }

    [Fact]
    public async Task LoadAsync_ReturnsInvalidDataForAnUnsupportedSchema()
    {
        ArtifactLoadResult<GenerationInformationDTO> result = await LoadAsync<GenerationInformationDTO>(
            HttpStatusCode.OK,
            """{ "schemaVersion": 99 }""");

        result.State.Status.Should().Be(ArtifactLoadStatus.InvalidData);
        result.State.Message.Should().Contain("schema 99 is not supported; expected schema 1");
    }

    [Fact]
    public async Task LoadAsync_ReturnsFailedForATransportFailure()
    {
        using var http = new HttpClient(new ThrowingHandler())
        {
            BaseAddress = new Uri("https://example.test/"),
        };

        ArtifactLoadResult<GenerationInformationDTO> result = await new ArtifactLoader(http).LoadAsync<GenerationInformationDTO>("data/test.json");

        result.State.Status.Should().Be(ArtifactLoadStatus.Failed);
        result.Value.Should().BeNull();
    }

    private static async Task<ArtifactLoadResult<T>> LoadAsync<T>(HttpStatusCode statusCode, string json)
        where T : class
    {
        using var http = new HttpClient(new StaticResponseHandler(statusCode, json))
        {
            BaseAddress = new Uri("https://example.test/"),
        };

        return await new ArtifactLoader(http).LoadAsync<T>("data/test.json");
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection failed.");
    }
}