using System.Net;
using System.Text;
using FluentAssertions;
using NEM.Contracts;
using NEM.Web.Services;

namespace NEM.Web.Tests.Services;

public sealed class SweepIndexLoaderTests
{
    private static readonly DateTimeOffset PeriodStart =
        new(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

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
        using var http = new HttpClient(new StaticJsonHandler(
            $$"""{ "schemaVersion": {{ArtifactSchemaVersions.SweepIndex}} }"""))
        {
            BaseAddress = new Uri("https://example.test/"),
        };

        SweepIndexLoadState state = await new SweepIndexLoader().LoadAsync(http, "data/sweeps/test/index.json");

        state.Status.Should().Be(SweepIndexLoadStatus.InvalidData);
        state.Index.Should().BeNull();
        state.Message.Should().Be("Sweep index sweep id is missing.");
    }

    [Fact]
    public async Task LoadAsync_ReturnsInvalidDataForAStatusOutsideTheContract()
    {
        using var http = new HttpClient(new StaticJsonHandler(
            $$"""
            {
              "schemaVersion": {{ArtifactSchemaVersions.SweepIndex}},
              "sweepId": "test",
              "points": [{ "pointId": "p0", "status": "pending" }]
            }
            """))
        {
            BaseAddress = new Uri("https://example.test/"),
        };

        SweepIndexLoadState state = await new SweepIndexLoader().LoadAsync(http, "data/sweeps/test/index.json");

        state.Status.Should().Be(SweepIndexLoadStatus.InvalidData);
        state.Index.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_ReturnsInvalidDataForANumericStatusOutsideTheContract()
    {
        // The enum converter accepts bare numbers, so the loader cannot rely on deserialization
        // alone to reject a status outside the closed set.
        using var http = new HttpClient(new StaticJsonHandler(
            $$"""
            {
              "schemaVersion": {{ArtifactSchemaVersions.SweepIndex}},
              "sweepId": "test",
              "name": "Test",
              "axis": { "label": "Capacity", "unit": "MW" },
              "provenance": {
                "gitCommitSha": "commit",
                "workingTreeDirty": false,
                "resolvedDefinitionSha256": "definition-hash",
                "inputFiles": [{ "path": "input.json", "purpose": "input", "sha256": "hash" }],
                "schemaVersions": { "sweepIndex": {{ArtifactSchemaVersions.SweepIndex}} }
              },
              "points": [{
                "pointId": "p0",
                "label": "Baseline",
                "axisValue": 0,
                "status": 7,
                "configPath": "configs/p0.json"
              }]
            }
            """))
        {
            BaseAddress = new Uri("https://example.test/"),
        };

        SweepIndexLoadState state = await new SweepIndexLoader().LoadAsync(http, "data/sweeps/test/index.json");

        state.Status.Should().Be(SweepIndexLoadStatus.InvalidData);
        state.Message.Should().Contain("unsupported status");
    }

    [Fact]
    public void Validate_ReturnsInvalidDataWhenAFailedPointCarriesResultBlocks()
    {
        SweepIndexPointDTO failed = ValidPoint() with
        {
            Status = SweepPointStatus.Failed,
            DetailPath = null,
            Scalars = null,
            Failure = new SweepPointFailureDTO(SweepFailureStage.Sizing, "capped", "Capped."),
        };

        SweepIndexLoader.Validate(ValidIndex(failed)).Message
            .Should().Contain("cannot include detail or results");
    }

    [Fact]
    public void Validate_ReturnsExplicitLoadingAndReadyStates()
    {
        SweepIndexLoadState loading = SweepIndexLoadState.Loading();
        SweepIndexLoadState ready = SweepIndexLoader.Validate(ValidIndex());

        loading.Status.Should().Be(SweepIndexLoadStatus.Loading);
        ready.Status.Should().Be(SweepIndexLoadStatus.Ready);
        ready.Index!.SweepId.Should().Be("test");
        ready.Index.Scope!.RegionIds.Should().Equal("NSW1");
    }

    [Fact]
    public void Validate_AcceptsAnIndexWithoutAScope()
    {
        SweepIndexLoadState state = SweepIndexLoader.Validate(ValidIndex() with { Scope = null });

        state.Status.Should().Be(SweepIndexLoadStatus.Ready);
    }

    [Theory]
    [InlineData(SweepPointStatus.Succeeded, null, true, false, "detail path is missing")]
    [InlineData(SweepPointStatus.Succeeded, "points/p0.json", true, true, "cannot include a failure")]
    [InlineData(SweepPointStatus.Failed, null, false, false, "failure is missing")]
    [InlineData(SweepPointStatus.Failed, "points/p0.json", false, true, "cannot include detail or results")]
    public void Validate_ReturnsInvalidDataForMalformedPoint(
        SweepPointStatus status,
        string? detailPath,
        bool hasScalars,
        bool hasFailure,
        string expectedMessage)
    {
        bool failed = status == SweepPointStatus.Failed;
        SweepIndexPointDTO point = ValidPoint() with
        {
            Status = status,
            DetailPath = detailPath,
            Scalars = hasScalars ? ValidScalars() : null,
            Reliability = failed ? null : ValidPoint().Reliability,
            StorageSizing = failed ? null : ValidPoint().StorageSizing,
            IntervalPointers = failed ? null : ValidPoint().IntervalPointers,
            Failure = hasFailure
                ? new SweepPointFailureDTO(SweepFailureStage.Input, "invalidInput", "Bad input.")
                : null,
        };

        SweepIndexLoadState state = SweepIndexLoader.Validate(ValidIndex(point));

        state.Status.Should().Be(SweepIndexLoadStatus.InvalidData);
        state.Index.Should().BeNull();
        state.Message.Should().Contain(expectedMessage);
    }

    [Fact]
    public void Validate_ReturnsInvalidDataWhenASucceededPointOmitsItsModelFacts()
    {
        SweepIndexLoadState missingReliability = SweepIndexLoader.Validate(
            ValidIndex(ValidPoint() with { Reliability = null }));
        SweepIndexLoadState missingSizing = SweepIndexLoader.Validate(
            ValidIndex(ValidPoint() with { StorageSizing = null }));
        SweepIndexLoadState missingPointers = SweepIndexLoader.Validate(
            ValidIndex(ValidPoint() with { IntervalPointers = null }));

        missingReliability.Message.Should().Contain("reliability basis is missing");
        missingSizing.Message.Should().Contain("storage sizing outcome is missing");
        missingPointers.Message.Should().Contain("interval pointers are missing");
    }

    [Fact]
    public void Validate_ReturnsInvalidDataForAnUnusableScope()
    {
        SweepIndexLoadState noRegions = SweepIndexLoader.Validate(ValidIndex() with
        {
            Scope = ValidScope() with { RegionIds = [] },
        });
        SweepIndexLoadState invertedPeriod = SweepIndexLoader.Validate(ValidIndex() with
        {
            Scope = ValidScope() with { PeriodEnd = PeriodStart.AddDays(-1) },
        });

        noRegions.Message.Should().Be("Sweep index scope is invalid.");
        invertedPeriod.Message.Should().Be("Sweep index scope is invalid.");
    }

    [Fact]
    public void Validate_ReturnsInvalidDataForDuplicatePointIdsAndLabels()
    {
        SweepIndexPointDTO first = ValidPoint();
        SweepIndexPointDTO second = ValidPoint() with { Label = "Second" };

        SweepIndexLoadState duplicateId = SweepIndexLoader.Validate(ValidIndex(first, second));
        SweepIndexLoadState duplicateLabel = SweepIndexLoader.Validate(ValidIndex(
            first,
            second with { PointId = "p1", Label = first.Label }));

        duplicateId.Message.Should().Be("Sweep index point id 'p0' is duplicated.");
        duplicateLabel.Message.Should().Be("Sweep index point label 'Baseline' is duplicated.");
    }

    private static SweepIndexDTO ValidIndex(params SweepIndexPointDTO[] points) => new(
        ArtifactSchemaVersions.SweepIndex,
        "test",
        "Test",
        new SweepAxisDTO("Capacity", "MW"),
        ValidScope(),
        new SweepProvenanceDTO(
            "commit",
            false,
            "definition-hash",
            [new SweepInputFileDTO("input.json", "input", "input-hash")],
            new Dictionary<string, int> { ["sweepIndex"] = ArtifactSchemaVersions.SweepIndex }),
        points);

    private static SweepScopeDTO ValidScope() => new(
        ["NSW1"],
        PeriodStart,
        PeriodStart.AddYears(1),
        TimeSpan.FromHours(1),
        new WeatherBasisDTO(
            WeatherBasisKind.TypicalMeteorologicalYear,
            "sydney.epw",
            "Sydney (WMO 947680)",
            "Typical meteorological year from sydney.epw."));

    private static SweepIndexPointDTO ValidPoint() => new(
        "p0",
        "Baseline",
        0,
        SweepPointStatus.Succeeded,
        "points/p0.json",
        "configs/p0.json",
        ValidScalars(),
        new ReliabilityBasisDTO(0.002, 0, true, "NEM reliability standard"),
        new StorageSizingOutcomeDTO(StorageSizingOutcome.NotRequired, 1, 1, 1, 1, 400, 100, 1),
        new IntervalPointersDTO(null, null, 0),
        null);

    private static SweepPointScalarResultsDTO ValidScalars() => new(
        1m,
        1m,
        0m,
        1,
        1,
        1,
        null,
        null,
        1,
        1,
        0,
        0,
        0,
        1,
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
