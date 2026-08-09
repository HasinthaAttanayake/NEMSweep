using System.Net;
using System.Text;
using FluentAssertions;
using NEM.Web.Services;

namespace NEM.Web.Tests.Services;

public sealed class SweepIndexLoaderTests
{
    [Fact]
    public async Task LoadAsync_ReturnsInvalidDataForAnUnsupportedSchema()
    {
        using var http = new HttpClient(new StaticJsonHandler("""{ "schemaVersion": 99 }"""))
        {
            BaseAddress = new Uri("https://example.test/"),
        };

        SweepIndexLoadState state = await new SweepIndexLoader().LoadAsync(http, "data/sweeps/test/index.json");

        state.Status.Should().Be(SweepIndexLoadStatus.InvalidData);
        state.Index.Should().BeNull();
        state.Message.Should().Contain("schema 99 is not supported");
    }

    [Fact]
    public async Task LoadAsync_ReturnsInvalidDataForSchemaOnlyJson()
    {
        using var http = new HttpClient(new StaticJsonHandler("""{ "schemaVersion": 1 }"""))
        {
            BaseAddress = new Uri("https://example.test/"),
        };

        SweepIndexLoadState state = await new SweepIndexLoader().LoadAsync(http, "data/sweeps/test/index.json");

        state.Status.Should().Be(SweepIndexLoadStatus.InvalidData);
        state.Index.Should().BeNull();
        state.Message.Should().Be("Sweep index sweep id is missing.");
    }

    [Fact]
    public void Validate_ReturnsExplicitLoadingAndReadyStates()
    {
        SweepIndexLoadState loading = SweepIndexLoadState.Loading();
        SweepIndexLoadState ready = SweepIndexLoader.Validate(ValidIndex());

        loading.Status.Should().Be(SweepIndexLoadStatus.Loading);
        ready.Status.Should().Be(SweepIndexLoadStatus.Ready);
        ready.Index!.SweepId.Should().Be("test");
    }

    [Theory]
    [InlineData("succeeded", null, true, null, "detail path is missing")]
    [InlineData("succeeded", "points/p0.json", true, "failure", "cannot include a failure")]
    [InlineData("failed", null, false, null, "failure is missing")]
    [InlineData("failed", "points/p0.json", false, "failed", "cannot include detail or scalars")]
    [InlineData("pending", null, false, null, "unsupported status 'pending'")]
    public void Validate_ReturnsInvalidDataForMalformedPoint(
        string status,
        string? detailPath,
        bool hasScalars,
        string? failure,
        string expectedMessage)
    {
        NEM.Contracts.SweepIndexPointDTO point = ValidPoint() with
        {
            Status = status,
            DetailPath = detailPath,
            Scalars = hasScalars ? ValidScalars() : null,
            Failure = failure,
        };

        SweepIndexLoadState state = SweepIndexLoader.Validate(ValidIndex(point));

        state.Status.Should().Be(SweepIndexLoadStatus.InvalidData);
        state.Index.Should().BeNull();
        state.Message.Should().Contain(expectedMessage);
    }

    [Fact]
    public void Validate_ReturnsInvalidDataForDuplicatePointIdsAndLabels()
    {
        NEM.Contracts.SweepIndexPointDTO first = ValidPoint();
        NEM.Contracts.SweepIndexPointDTO second = ValidPoint() with { Label = "Second" };

        SweepIndexLoadState duplicateId = SweepIndexLoader.Validate(ValidIndex(first, second));
        SweepIndexLoadState duplicateLabel = SweepIndexLoader.Validate(ValidIndex(
            first,
            second with { PointId = "p1", Label = first.Label }));

        duplicateId.Message.Should().Be("Sweep index point id 'p0' is duplicated.");
        duplicateLabel.Message.Should().Be("Sweep index point label 'Baseline' is duplicated.");
    }

    private static NEM.Contracts.SweepIndexDTO ValidIndex(
        params NEM.Contracts.SweepIndexPointDTO[] points) => new(
        1,
        "test",
        "Test",
        new NEM.Contracts.SweepAxisDTO("Capacity", "MW"),
        new NEM.Contracts.SweepProvenanceDTO(
            "commit",
            false,
            "definition-hash",
            [new NEM.Contracts.SweepInputFileDTO("input.json", "input", "input-hash")],
            new Dictionary<string, int> { ["sweepIndex"] = 1 }),
        points);

    private static NEM.Contracts.SweepIndexPointDTO ValidPoint() => new(
        "p0",
        "Baseline",
        0,
        "succeeded",
        "points/p0.json",
        "configs/p0.json",
        ValidScalars(),
        null);

    private static NEM.Contracts.SweepPointScalarResultsDTO ValidScalars() => new(
        1m,
        1m,
        0m,
        1,
        null,
        null,
        1,
        1,
        0,
        0,
        0);

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