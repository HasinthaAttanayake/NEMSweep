using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public async Task LoadAsync_ReturnsSuccessForSchema6WeatherArtifact()
    {
        ArtifactLoadResult<WeatherDataDTO> result = await LoadAsync<WeatherDataDTO>(
            HttpStatusCode.OK,
            """
            {
              "schemaVersion": 6,
              "regionId": "NSW1",
              "start": "2025-07-01T00:00:00+10:00",
              "resolution": "01:00:00",
              "solar": {
                "sourceFile": "solar.epw",
                "location": { "city": "Solar site", "wmo": "123456", "latitude": -33.9, "longitude": 151.2 },
                "globalHorizontalRadiationWhPerSquareMetre": [],
                "directNormalRadiationWhPerSquareMetre": [],
                "diffuseHorizontalRadiationWhPerSquareMetre": [],
                "solarZenithDegrees": [],
                "dryBulbTemperatureDegreesCelsius": [],
                "productionMegawattsAtOneMegawattAc": []
              },
              "wind": {
                "sourceFile": "wind.epw",
                "location": { "city": "Wind site", "wmo": "654321", "latitude": -34.0, "longitude": 151.3 },
                "windSpeedMetresPerSecond": [],
                "measurementHeightMetres": 120,
                "productionMegawattsAtOneMegawattInstalled": []
              }
            }
            """);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Solar.SourceFile.Should().Be("solar.epw");
        result.Value.Wind.MeasurementHeightMetres.Should().Be(120);
    }

    [Fact]
    public async Task LoadAsync_RejectsSchema5WeatherArtifact()
    {
        ArtifactLoadResult<WeatherDataDTO> result = await LoadAsync<WeatherDataDTO>(
            HttpStatusCode.OK,
            """{ "schemaVersion": 5 }""");

        result.State.Status.Should().Be(ArtifactLoadStatus.InvalidData);
        result.State.Message.Should().Contain("schema 5 is not supported; expected schema 6");
    }

    [Fact]
    public async Task LoadAsync_ReturnsSuccessForSystemDispatchResultsSchema()
    {
        ArtifactLoadResult<SystemDispatchResultsDTO> result = await LoadAsync<SystemDispatchResultsDTO>(
            HttpStatusCode.OK,
            Serialize(ArtifactFixtures.SystemResults()));

        result.IsSuccess.Should().BeTrue();
        result.Value!.RunId.Should().Be("run-1");
    }

    [Fact]
    public async Task LoadAsync_RejectsUnsupportedSystemDispatchResultsSchema()
    {
        ArtifactLoadResult<SystemDispatchResultsDTO> result = await LoadAsync<SystemDispatchResultsDTO>(
            HttpStatusCode.OK,
            Serialize(ArtifactFixtures.SystemResults() with { SchemaVersion = 1 }));

        result.State.Status.Should().Be(ArtifactLoadStatus.InvalidData);
            result.State.Message.Should().Be("Artifact schema 1 is not supported; expected schema 10.");
    }

    [Fact]
    public async Task LoadAsync_ReturnsSuccessForRegionDispatchResultsSchema()
    {
        ArtifactLoadResult<RegionDispatchResultsDTO> result = await LoadAsync<RegionDispatchResultsDTO>(
            HttpStatusCode.OK,
            Serialize(ArtifactFixtures.RegionResults()));

        result.IsSuccess.Should().BeTrue();
        result.Value!.RegionId.Should().Be("NSW1");
    }

    [Fact]
    public async Task LoadAsync_RejectsUnsupportedRegionDispatchResultsSchema()
    {
        ArtifactLoadResult<RegionDispatchResultsDTO> result = await LoadAsync<RegionDispatchResultsDTO>(
            HttpStatusCode.OK,
            """{ "schemaVersion": 1 }""");

        result.State.Status.Should().Be(ArtifactLoadStatus.InvalidData);
        result.State.Message.Should().Be("Artifact schema 1 is not supported; expected schema 8.");
    }

    [Fact]
    public async Task LoadRegionForAsync_AcceptsADetailFromTheSameRun()
    {
        ArtifactLoadResult<RegionDispatchResultsDTO> result = await LoadRegionAsync("run-1", "run-1");

        result.IsSuccess.Should().BeTrue();
        result.Value!.RegionId.Should().Be("NSW1");
    }

    [Fact]
    public async Task LoadRegionForAsync_RejectsADetailFromAnotherRunAsInvalidData()
    {
        ArtifactLoadResult<RegionDispatchResultsDTO> result = await LoadRegionAsync("run-1", "run-2");

        result.State.Status.Should().Be(ArtifactLoadStatus.InvalidData);
        result.State.Message.Should().Be("System and regional artifact run IDs do not match.");
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_ReadsAnArtifactOnceAndServesTheParsedInstanceAfterwards()
    {
        var handler = new CountingHandler(Serialize(ArtifactFixtures.SystemResults()));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var loader = new ArtifactLoader(http);

        ArtifactLoadResult<SystemDispatchResultsDTO> first =
            await loader.LoadAsync<SystemDispatchResultsDTO>("data/results.json");
        ArtifactLoadResult<SystemDispatchResultsDTO> second =
            await loader.LoadAsync<SystemDispatchResultsDTO>("data/results.json");

        handler.Requests.Should().Be(1);
        second.IsSuccess.Should().BeTrue();
        second.Value.Should().BeSameAs(first.Value);
    }

    [Fact]
    public async Task LoadAsync_DoesNotServeACachedArtifactToADifferentType()
    {
        var handler = new CountingHandler(Serialize(ArtifactFixtures.SystemResults()));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var loader = new ArtifactLoader(http);

        await loader.LoadAsync<SystemDispatchResultsDTO>("data/results.json");
        ArtifactLoadResult<RegionDispatchResultsDTO> other =
            await loader.LoadAsync<RegionDispatchResultsDTO>("data/results.json");

        handler.Requests.Should().Be(2);
        other.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_CoalescesConcurrentRequestsForTheSameArtifactIntoOneFetch()
    {
        var handler = new GatedHandler(Serialize(ArtifactFixtures.SystemResults()));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var loader = new ArtifactLoader(http);

        // Neither call is awaited before the other starts, so both reach the loader while the
        // first request is still outstanding — exactly what a fast repeat navigation or a sweep
        // manifest's own loop can do before the cache has anything to serve.
        Task<ArtifactLoadResult<SystemDispatchResultsDTO>> first =
            loader.LoadAsync<SystemDispatchResultsDTO>("data/results.json");
        Task<ArtifactLoadResult<SystemDispatchResultsDTO>> second =
            loader.LoadAsync<SystemDispatchResultsDTO>("data/results.json");
        handler.Release();

        ArtifactLoadResult<SystemDispatchResultsDTO>[] results = await Task.WhenAll(first, second);

        handler.Requests.Should().Be(1);
        results[0].Value.Should().BeSameAs(results[1].Value);
    }

    [Fact]
    public async Task LoadAsync_DoesNotCacheAnArtifactThatFailedItsChecks()
    {
        var handler = new CountingHandler("""{ "schemaVersion": 99 }""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var loader = new ArtifactLoader(http);

        await loader.LoadAsync<GenerationInformationDTO>("data/test.json");
        await loader.LoadAsync<GenerationInformationDTO>("data/test.json");

        handler.Requests.Should().Be(2);
    }

    [Fact]
    public async Task LoadAsync_EvictsTheLeastRecentlyReadArtifactRatherThanGrowingWithoutBound()
    {
        var handler = new CountingHandler(Serialize(ArtifactFixtures.SystemResults()));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var loader = new ArtifactLoader(http);

        // One more distinct path than the cache holds, so the first read is evicted.
        for (int index = 0; index < 7; index++)
        {
            await loader.LoadAsync<SystemDispatchResultsDTO>($"data/results-{index}.json");
        }

        await loader.LoadAsync<SystemDispatchResultsDTO>("data/results-0.json");
        await loader.LoadAsync<SystemDispatchResultsDTO>("data/results-6.json");

        handler.Requests.Should().Be(8);
    }

    [Fact]
    public async Task LoadRegionForAsync_DoesNotRefetchTheSystemArtifact()
    {
        var handler = new PairResponseHandler("run-1", "run-1");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        await new ArtifactLoader(http).LoadRegionForAsync(
            ArtifactFixtures.SystemResults(runId: "run-1"),
            "data/results-nsw1.json");

        handler.Requests.Should().ContainSingle().Which.Should().EndWith("results-nsw1.json");
    }

    [Fact]
    public async Task LoadAsync_RejectsMisalignedSystemInterconnectorEvidence()
    {
        SystemDispatchResultsDTO valid = ArtifactFixtures.SystemResults(
            interconnectors:
            [new DispatchInterconnectorDTO("NSW1->VIC1", "NSW1", "VIC1", 100, [1], [0])]);
        SystemDispatchResultsDTO invalid = valid with
        {
            RegionIds = ["NSW1", "VIC1"],
            RegionSummariesById = new Dictionary<string, RegionDispatchSummaryDTO>
            {
                ["NSW1"] = valid.RegionSummariesById["NSW1"],
                ["VIC1"] = valid.RegionSummariesById["NSW1"],
            },
            Topology = new DispatchTopologyDTO(
                ["NSW1", "VIC1"],
                [new DispatchTopologyLinkDTO("NSW1->VIC1", "NSW1", "VIC1", 100)]),
        };

        ArtifactLoadResult<SystemDispatchResultsDTO> result = await LoadAsync<SystemDispatchResultsDTO>(
            HttpStatusCode.OK,
            Serialize(invalid));

        result.State.Status.Should().Be(ArtifactLoadStatus.InvalidData);
        result.State.Message.Should().Contain("interconnector series must align");
    }

    [Fact]
    public async Task LoadAsync_RejectsSystemInterconnectorWithoutRequiredCapacity()
    {
        SystemDispatchResultsDTO valid = ArtifactFixtures.SystemResults(
            interconnectors:
            [new DispatchInterconnectorDTO("NSW1->VIC1", "NSW1", "VIC1", 100, [1, 0, 0], [0, 0, 0])]);
        JsonObject artifact = JsonNode.Parse(Serialize(valid))!.AsObject();
        artifact["interconnectors"]![0]!.AsObject().Remove("capacityMw");

        ArtifactLoadResult<SystemDispatchResultsDTO> result = await LoadAsync<SystemDispatchResultsDTO>(
            HttpStatusCode.OK,
            artifact.ToJsonString());

        result.State.Status.Should().Be(ArtifactLoadStatus.InvalidData);
        result.State.Message.Should().Be("Artifact is not valid JSON data.");
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

    private static string Serialize<T>(T artifact) => JsonSerializer.Serialize(artifact, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    });

    private static async Task<ArtifactLoadResult<RegionDispatchResultsDTO>> LoadRegionAsync(
        string systemRunId,
        string regionRunId)
    {
        using var http = new HttpClient(new PairResponseHandler(systemRunId, regionRunId))
        {
            BaseAddress = new Uri("https://example.test/"),
        };

        return await new ArtifactLoader(http).LoadRegionForAsync(
            ArtifactFixtures.SystemResults(runId: systemRunId),
            "data/results-nsw1.json");
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

    /// <summary>Holds every request open until the test releases it, to force two requests to overlap.</summary>
    private sealed class GatedHandler(string content) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate = new();

        public int Requests { get; private set; }

        public void Release() => _gate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            await _gate.Task;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class CountingHandler(string content) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection failed.");
    }

    private sealed class PairResponseHandler(string systemRunId, string regionRunId) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsolutePath);
            string runId = request.RequestUri!.AbsolutePath.EndsWith("results-nsw1.json", StringComparison.Ordinal)
                ? regionRunId
                : systemRunId;
            string json = request.RequestUri.AbsolutePath.EndsWith("results-nsw1.json", StringComparison.Ordinal)
                ? Serialize(ArtifactFixtures.RegionResults(runId: runId))
                : Serialize(ArtifactFixtures.SystemResults(runId: runId));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}