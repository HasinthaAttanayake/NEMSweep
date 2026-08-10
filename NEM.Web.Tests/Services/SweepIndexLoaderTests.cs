using System.Net;
using System.Text;
using AwesomeAssertions;
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
        ArtifactLoadResult<SweepIndexDTO> result = await LoadAsync("""{ "schemaVersion": 99 }""");

        result.State.Status.Should().Be(ArtifactLoadStatus.InvalidData);
        result.Value.Should().BeNull();
        result.State.Message.Should().Contain("schema 99 is not supported");
    }

    [Fact]
    public async Task LoadAsync_ReturnsInvalidDataForSchemaOnlyJson()
    {
        ArtifactLoadResult<SweepIndexDTO> result = await LoadAsync(
            $$"""{ "schemaVersion": {{ArtifactSchemaVersions.SweepIndex}} }""");

        result.State.Status.Should().Be(ArtifactLoadStatus.InvalidData);
        result.Value.Should().BeNull();
        result.State.Message.Should().Be("Sweep index sweep id is missing.");
    }

    [Fact]
    public async Task LoadAsync_ReturnsInvalidDataForAStatusOutsideTheContract()
    {
        ArtifactLoadResult<SweepIndexDTO> result = await LoadAsync(
            $$"""
            {
              "schemaVersion": {{ArtifactSchemaVersions.SweepIndex}},
              "sweepId": "test",
              "points": [{ "pointId": "p0", "status": "pending" }]
            }
            """);

        result.State.Status.Should().Be(ArtifactLoadStatus.InvalidData);
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_ReturnsInvalidDataForANumericStatusOutsideTheContract()
    {
        // The enum converter accepts bare numbers, so the loader cannot rely on deserialization
        // alone to reject a status outside the closed set.
        ArtifactLoadResult<SweepIndexDTO> result = await LoadAsync(
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
            """);

        result.State.Status.Should().Be(ArtifactLoadStatus.InvalidData);
        result.State.Message.Should().Contain("unsupported status");
    }

    [Fact]
    public void Validate_AcceptsAWellFormedIndex()
    {
        SweepIndexValidator.Validate(ValidIndex(ValidPoint())).Should().BeNull();
    }

    [Fact]
    public void Validate_RejectsAnIndexWithNoPoints()
    {
        // A sweep page cannot choose a series to open on, so a successful-but-blank page would be
        // the alternative.
        SweepIndexValidator.Validate(ArtifactFixtures.Index())
            .Should().Be("Sweep index contains no points.");
    }

    [Fact]
    public void Validate_AcceptsAnIndexWithoutAScope()
    {
        SweepIndexValidator.Validate(ValidIndex(ValidPoint()) with { Scope = null }).Should().BeNull();
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

        SweepIndexValidator.Validate(ValidIndex(failed))
            .Should().Contain("cannot include detail or results");
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

        SweepIndexValidator.Validate(ValidIndex(point)).Should().Contain(expectedMessage);
    }

    [Fact]
    public void Validate_ReturnsInvalidDataWhenASucceededPointOmitsItsModelFacts()
    {
        SweepIndexValidator.Validate(ValidIndex(ValidPoint() with { Reliability = null }))
            .Should().Contain("reliability basis is missing");
        SweepIndexValidator.Validate(ValidIndex(ValidPoint() with { StorageSizing = null }))
            .Should().Contain("storage sizing outcome is missing");
        SweepIndexValidator.Validate(ValidIndex(ValidPoint() with { IntervalPointers = null }))
            .Should().Contain("interval pointers are missing");
    }

    [Fact]
    public void Validate_ReturnsInvalidDataForAnUnusableScope()
    {
        SweepIndexValidator.Validate(ValidIndex(ValidPoint()) with
        {
            Scope = ValidScope() with { RegionIds = [] },
        }).Should().Be("Sweep index scope is invalid.");

        SweepIndexValidator.Validate(ValidIndex(ValidPoint()) with
        {
            Scope = ValidScope() with { PeriodEnd = PeriodStart.AddDays(-1) },
        }).Should().Be("Sweep index scope is invalid.");
    }

    [Fact]
    public void Validate_ReturnsInvalidDataForDuplicatePointIdsAndLabels()
    {
        SweepIndexPointDTO first = ValidPoint();
        SweepIndexPointDTO second = ValidPoint() with { Label = "Second" };

        SweepIndexValidator.Validate(ValidIndex(first, second))
            .Should().Be("Sweep index point id 'p0' is duplicated.");
        SweepIndexValidator.Validate(ValidIndex(first, second with { PointId = "p1", Label = first.Label }))
            .Should().Be("Sweep index point label 'Baseline' is duplicated.");
    }

    private static async Task<ArtifactLoadResult<SweepIndexDTO>> LoadAsync(string json)
    {
        using var http = new HttpClient(new StaticJsonHandler(json))
        {
            BaseAddress = new Uri("https://example.test/"),
        };

        return await new SweepIndexLoader(new ArtifactLoader(http))
            .LoadAsync("data/sweeps/test/index.json");
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
