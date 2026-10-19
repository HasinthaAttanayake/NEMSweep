using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NEMSweep.CLI.Application;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.CLI.Scenarios;
using NEMSweep.Contracts;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Simulation;

namespace NEMSweep.CLI.Tests.Scenarios;

[Trait("Category", "FullYearAcceptance")]
public sealed class SweepRunTests
{
    [Fact]
    public void Run_Version2SweepOverLegacyBaseline_PublishesFixedAndSizedModes()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "fixed", "axisValue": 0, "label": "Declared fleet",
               "overrides": { "executionMode": "fixedFleet", "storageSizing": null } },
             { "pointId": "sized", "axisValue": 1, "label": "Sized fleet", "overrides": {} }]
            """);
        JsonObject definition = JsonNode.Parse(File.ReadAllBytes(fixture.DefinitionPath))!.AsObject();
        definition["schemaVersion"] = 2;
        File.WriteAllText(fixture.DefinitionPath, definition.ToJsonString());
        string baselinePath = Path.Combine(fixture.RootPath, "scenarios", "baseline.json");
        JsonObject baseline = JsonNode.Parse(File.ReadAllBytes(baselinePath))!.AsObject();
        baseline["$schema"] =
            "https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/schema/scenario-v6.json";
        File.WriteAllText(baselinePath, baseline.ToJsonString());

        SweepRunCommand.Run(fixture.CreateContext(TextWriter.Null), "sweeps/test-sweep.json")
            .Should().Be(0);

        JsonObject index = ReadIndex(fixture);
        JsonObject fixedPoint = index["points"]![0]!.AsObject();
        fixedPoint["status"]!.GetValue<string>().Should().Be("succeeded");
        fixedPoint["executionMode"]!.GetValue<string>().Should().Be("fixedFleet");
        fixedPoint["storageSizing"].Should().BeNull();
        JsonObject sizedPoint = index["points"]![1]!.AsObject();
        sizedPoint["executionMode"]!.GetValue<string>().Should().Be("sizeBattery");
        sizedPoint["storageSizing"].Should().NotBeNull();
        JsonObject fixedDetail = JsonNode.Parse(File.ReadAllBytes(fixture.PointResultPath("fixed")))!.AsObject();
        fixedDetail["executionMode"]!.GetValue<string>().Should().Be("fixedFleet");
        fixedDetail["storageSizing"].Should().BeNull();
        JsonObject config = JsonNode.Parse(File.ReadAllBytes(PublishedConfig(fixture, fixedPoint)))!.AsObject();
        config["schemaVersion"]!.GetValue<int>().Should().Be(7);
        config["$schema"]!.GetValue<string>().Should().EndWith("/scenario-v7.json");
        config["reliability"]!["targetUsePercentage"]!.GetValue<double>().Should().Be(0.002);
        fixedPoint["scalars"]!["storageEnergyMwh"]!.GetValue<double>().Should().Be(0);
    }

    [Fact]
    public void Run_WritesPublishedIndexAndSucceededPointArtifacts()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": { "name": "Changed scenario" } }]
            """);
        using var output = new StringWriter();
        int exitCode = SweepRunCommand.Run(fixture.CreateContext(output), "sweeps/test-sweep.json");

        exitCode.Should().Be(0);
        File.Exists(fixture.PointResultPath("p0")).Should().BeTrue();
        File.Exists(fixture.PointResultPath("p1")).Should().BeTrue();
        JsonNode.Parse(File.ReadAllText(fixture.PointResultPath("p1")))!["schemaVersion"]!
            .GetValue<int>().Should().Be(ArtifactSchemaVersions.SystemDispatchResults);
        File.Exists(fixture.RegionResultPath("p0", "NSW1")).Should().BeTrue();
        File.Exists(fixture.RegionResultPath("p1", "NSW1")).Should().BeTrue();
        Status(fixture, "p0")["status"]!.GetValue<string>().Should().Be("succeeded");
        Status(fixture, "p1")["status"]!.GetValue<string>().Should().Be("succeeded");
        JsonObject index = ReadIndex(fixture);
        index["schemaVersion"]!.GetValue<int>().Should().Be(ArtifactSchemaVersions.SweepIndex);
        index["schemaVersion"]!.GetValue<int>().Should().Be(14);
        JsonObject schemaVersions = index["provenance"]!["schemaVersions"]!.AsObject();
        schemaVersions["systemDispatchResults"]!.GetValue<int>().Should().Be(18);
        schemaVersions["systemDispatchOverview"]!.GetValue<int>().Should().Be(7);
        schemaVersions["regionDispatchResults"]!.GetValue<int>().Should().Be(13);
        schemaVersions["regionDispatchOverview"]!.GetValue<int>().Should().Be(6);
        schemaVersions["sweepIndex"]!.GetValue<int>().Should().Be(14);
        schemaVersions.ContainsKey("dispatchResults").Should().BeFalse();
        index["points"]!.AsArray().Should().HaveCount(2);
        JsonObject firstPoint = index["points"]![0]!.AsObject();
        firstPoint["status"]!.GetValue<string>().Should().Be("succeeded");
        firstPoint["detailPath"]!.GetValue<string>().Should().Be("points/p0.json");
        firstPoint["regionScalars"]!.AsArray().Should().ContainSingle();
        firstPoint["regionScalars"]![0]!["regionId"]!.GetValue<string>().Should().Be("NSW1");
        firstPoint["regionDetails"]!.AsArray().Should().ContainSingle();
        firstPoint["regionDetails"]![0]!["detailPath"]!.GetValue<string>()
            .Should().Be("points/p0-nsw1.json");
        firstPoint["configPath"]!.GetValue<string>().Should().Be("configs/p0.json");
        firstPoint["scalars"]!["energyServedMwh"]!.GetValue<double>().Should().Be(87_600);
        firstPoint["scalars"]!["achievedRenewableShareGridScale"]!.GetValue<double>().Should().Be(0);
        firstPoint["scalars"]!["achievedRenewableShareNative"]!.GetValue<double>().Should().Be(0);
        firstPoint["scalars"]!["unservedHours"]!.GetValue<int>().Should().Be(0);
        firstPoint["reliability"]!["targetUsePercentageOfDemand"]!.GetValue<double>()
            .Should().Be(0.002);
        firstPoint["reliability"]!["withinTarget"]!.GetValue<bool>().Should().BeTrue();
        firstPoint["storageSizing"]!["outcome"]!.GetValue<string>().Should().Be("notRequired");
        firstPoint["intervalPointers"]!.AsObject().Select(pointer => pointer.Key)
            .Should().Contain("peakUnservedIntervalIndex");
        index["scope"]!["regionIds"]!.AsArray().Select(region => region!.GetValue<string>())
            .Should().Equal("NSW1");
        index["scope"]!["resolution"]!.GetValue<string>().Should().Be("01:00:00");
        index["scope"]!["weatherBasis"]!["kind"]!.GetValue<string>()
            .Should().Be("typicalMeteorologicalYear");
        string[] inputPurposes = index["provenance"]!["inputFiles"]!.AsArray()
            .Select(input => input!["purpose"]!.GetValue<string>()).ToArray();
        inputPurposes.Should().Contain(["demand-data", "weather-data", "sweep-definition"]);
        inputPurposes.Should().NotContain("emitted-scenario-config");
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(fixture.ManifestPath))!.AsObject();
        manifest["schemaVersion"]!.GetValue<int>().Should().Be(1);
        manifest["sweeps"]!.AsArray().Should().ContainSingle().Which!["sweepId"]!.GetValue<string>()
            .Should().Be("test-sweep");
        new FileInfo(fixture.IndexPath).Length.Should().BeLessThan(10_000);
        File.Exists(fixture.SharedBaseSeriesPath).Should().BeTrue();
        JsonObject pointResult = JsonNode.Parse(File.ReadAllText(fixture.PointResultPath("p0")))!.AsObject();
        pointResult["dataSeries"]!["demand"]!["baseDemandMw"].Should().BeNull();
        pointResult["dataSeries"]!["demand"]!["baseDemandSeriesPath"]!.GetValue<string>()
            .Should().StartWith("../series/base-demand-");
        JsonObject regionResult = JsonNode.Parse(File.ReadAllText(
            fixture.RegionResultPath("p0", "NSW1")))!.AsObject();
        regionResult["dataSeries"]!["demand"]!["baseDemandMw"].Should().BeNull();
        regionResult["dataSeries"]!["demand"]!["baseDemandSeriesPath"]!.GetValue<string>()
            .Should().StartWith("../series/base-demand-");
        output.ToString().Should().Contain("Running sweep point p1 (Capacity=1 MW).");
    }

    [Fact]
    public void Run_RecordsPerPointAndTotalDurationInIndex()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": { "name": "Changed scenario" } }]
            """);

        SweepRunCommand.Run(fixture.CreateContext(TextWriter.Null), "sweeps/test-sweep.json").Should().Be(0);

        JsonObject index = ReadIndex(fixture);
        double totalDurationMs = index["provenance"]!["totalDurationMs"]!.GetValue<double>();
        totalDurationMs.Should().BeGreaterThan(0);
        double sumOfPointDurations = 0;
        foreach (JsonNode? point in index["points"]!.AsArray())
        {
            double pointDurationMs = point!["durationMs"]!.GetValue<double>();
            pointDurationMs.Should().BeGreaterThan(0);
            sumOfPointDurations += pointDurationMs;
        }

        sumOfPointDurations.Should().BeLessThanOrEqualTo(totalDurationMs);
    }

    [Fact]
    public void Run_DeletesSeriesFilesNoPointReferences()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition(
            """[{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }]""");
        string stalePath = Path.Combine(fixture.SweepDataPath, "series", "base-demand-stale.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stalePath)!);
        File.WriteAllText(stalePath, "{}");

        SweepRunCommand.Run(fixture.CreateContext(TextWriter.Null), "sweeps/test-sweep.json")
            .Should().Be(0);

        File.Exists(stalePath).Should().BeFalse();
        File.Exists(fixture.SharedBaseSeriesPath).Should().BeTrue();
    }

    [Fact]
    public void Run_ContinuesAfterPointFailureAndReturnsNonZeroSummary()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Invalid", "overrides": { "costBasis": { "year": 1999 } } }]
            """);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.PointResultPath("p1"))!);
        File.WriteAllText(fixture.PointResultPath("p1"), "stale result");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = SweepRunCommand.Run(fixture.CreateContext(output, error), "sweeps/test-sweep.json");

        exitCode.Should().Be(1);
        File.Exists(fixture.PointResultPath("p0")).Should().BeTrue();
        File.Exists(fixture.PointResultPath("p1")).Should().BeFalse();
        Status(fixture, "p0")["status"]!.GetValue<string>().Should().Be("succeeded");
        Status(fixture, "p1")["status"]!.GetValue<string>().Should().Be("failed");
        JsonObject failedPoint = ReadIndex(fixture)["points"]![1]!.AsObject();
        failedPoint["status"]!.GetValue<string>().Should().Be("failed");
        failedPoint["detailPath"].Should().BeNull();
        failedPoint["scalars"].Should().BeNull();
        failedPoint["failure"]!["stage"]!.GetValue<string>().Should().Be("input");
        failedPoint["failure"]!["code"]!.GetValue<string>().Should().Be("invalidConfig");
        failedPoint["failure"]!["message"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        // The per-point failure detail belongs on standard error, so a caller piping results does
        // not have to filter it out. The end-of-run summary table does carry each point's status,
        // a failed one included, because a summary that quietly omitted it would be worse.
        output.ToString().Should().NotContain("Sweep point p1: failed:");
        output.ToString().Should().NotContain("failed points:");
        error.ToString().Should().Contain("Sweep point p1: failed:");
        error.ToString().Should().Contain("failed points: p1");
    }

    /// <summary>
    /// A malformed override can fail before a config is even generated, not just after: a keyed
    /// array override missing its key property fails inside the JSON merge patch itself. That used
    /// to happen in an unguarded fan-out pass before any point's dispatch even started, aborting the
    /// whole run with no results published for any point. It must now isolate to the one point.
    /// Run for the malformed point in either position: the failure handler cleans up inside the
    /// points directory, which nothing creates until a point dispatches, so a malformed *first*
    /// point used to take the whole run down inside the handler meant to contain it.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Run_ContinuesAfterAnOverrideFailsToMergeIntoTheBaseline(int malformedIndex)
    {
        const string Malformed = """{ "regions": [{ "generatingFleets": [] }] }""";
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition($$"""
            [{ "pointId": "p0", "axisValue": 0, "label": "First", "overrides": {{(malformedIndex == 0 ? Malformed : "{}")}} },
             { "pointId": "p1", "axisValue": 1, "label": "Second", "overrides": {{(malformedIndex == 1 ? Malformed : "{}")}} }]
            """);
        string failedId = $"p{malformedIndex}";
        string succeededId = $"p{1 - malformedIndex}";
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = SweepRunCommand.Run(fixture.CreateContext(output, error), "sweeps/test-sweep.json");

        exitCode.Should().Be(1);
        File.Exists(fixture.PointResultPath(succeededId)).Should().BeTrue();
        File.Exists(fixture.PointResultPath(failedId)).Should().BeFalse();
        Status(fixture, succeededId)["status"]!.GetValue<string>().Should().Be("succeeded");
        Status(fixture, failedId)["status"]!.GetValue<string>().Should().Be("failed");
        JsonObject failedPoint = ReadIndex(fixture)["points"]![malformedIndex]!.AsObject();
        failedPoint["status"]!.GetValue<string>().Should().Be("failed");
        failedPoint["failure"]!["stage"]!.GetValue<string>().Should().Be("input");
        failedPoint["failure"]!["code"]!.GetValue<string>().Should().Be("invalidConfig");
        error.ToString().Should().Contain($"failed points: {failedId}");
    }

    /// <summary>
    /// A point that fails while running still ran against a real config, and the index cites one
    /// for every point. That config must therefore be published before dispatch, or the index
    /// points at a file that was never written.
    /// </summary>
    [Fact]
    public void Run_PublishesTheConfigOfAPointThatFailsItsSchemaValidation()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Invalid", "overrides": { "costBasis": { "year": 1000 } } }]
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = SweepRunCommand.Run(fixture.CreateContext(output, error), "sweeps/test-sweep.json");

        exitCode.Should().Be(1);
        JsonObject failedPoint = ReadIndex(fixture)["points"]![1]!.AsObject();
        failedPoint["status"]!.GetValue<string>().Should().Be("failed");
        failedPoint["failure"]!["stage"]!.GetValue<string>().Should().Be("input");
        failedPoint["failure"]!["code"]!.GetValue<string>().Should().Be("invalidConfig");
        File.Exists(PublishedConfig(fixture, failedPoint)).Should().BeTrue();
    }

    /// <summary>
    /// A point whose overrides never merged has no config of its own, so a config left at that
    /// path by an earlier run must not survive to be cited as the one this point ran against.
    /// </summary>
    [Fact]
    public void Run_DoesNotPublishAStaleConfigForAPointWhoseOverridesFailedToMerge()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": { "name": "Changed scenario" } }]
            """);
        using var output = new StringWriter();
        SweepRunCommand.Run(fixture.CreateContext(output), "sweeps/test-sweep.json").Should().Be(0);
        string publishedBefore = PublishedConfig(fixture, ReadIndex(fixture)["points"]![1]!.AsObject());
        File.Exists(publishedBefore).Should().BeTrue();
        File.Delete(Path.Combine(fixture.RootPath, "sweeps", "test-sweep", "configs", "p1.json"));

        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": { "regions": [{ "generatingFleets": [] }] } }]
            """);
        using var rerunOutput = new StringWriter();
        using var rerunError = new StringWriter();

        SweepRunCommand.Run(fixture.CreateContext(rerunOutput, rerunError), "sweeps/test-sweep.json")
            .Should().Be(1);

        File.Exists(publishedBefore).Should().BeFalse();
    }

    [Fact]
    public void Run_ExternalizesBothSystemAndRegionalDemandToTheSameSharedSeriesFile()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": { "name": "Changed scenario" } }]
            """);
        CliContext context = fixture.CreateContext(TextWriter.Null);

        SweepRunCommand.Run(context, "sweeps/test-sweep.json").Should().Be(0);

        JsonObject pointResult = JsonNode.Parse(File.ReadAllText(fixture.PointResultPath("p1")))!.AsObject();
        pointResult["dataSeries"]!["demand"]!["baseDemandMw"].Should().BeNull();
        string pointSeriesPath = pointResult["dataSeries"]!["demand"]!["baseDemandSeriesPath"]!.GetValue<string>();
        pointSeriesPath.Should().StartWith("../series/base-demand-");
        JsonObject regionResult = JsonNode.Parse(File.ReadAllText(
            fixture.RegionResultPath("p1", "NSW1")))!.AsObject();
        regionResult["dataSeries"]!["demand"]!["baseDemandMw"].Should().BeNull();
        string regionSeriesPath = regionResult["dataSeries"]!["demand"]!["baseDemandSeriesPath"]!.GetValue<string>();
        regionSeriesPath.Should().StartWith("../series/base-demand-");
        // A single-region sweep's system-wide demand equals its one region's demand, so both
        // point and regional results should reference the same content-addressed series file.
        regionSeriesPath.Should().Be(pointSeriesPath);
    }

    [Fact]
    public void Run_IsDeterministicAcrossCleanDirectoriesExceptGeneratedAt()
    {
        using var first = new SweepRunFixture();
        using var second = new SweepRunFixture();
        const string points = """
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": { "name": "Changed scenario" } }]
            """;
        first.WriteDefinition(points);
        second.WriteDefinition(points);

        SweepRunCommand.Run(first.CreateContext(TextWriter.Null), "sweeps/test-sweep.json").Should().Be(0);
        SweepRunCommand.Run(second.CreateContext(TextWriter.Null), "sweeps/test-sweep.json").Should().Be(0);

        Dictionary<string, byte[]> firstFiles = SweepFiles(first);
        Dictionary<string, byte[]> secondFiles = SweepFiles(second);
        firstFiles.Keys.Should().Equal(secondFiles.Keys);
        foreach (string path in firstFiles.Keys)
        {
            firstFiles[path].Should().Equal(secondFiles[path]);
        }
    }

    [Fact]
    public void CreateProvenance_ChangesDemandHashWhenTheInputBytesChange()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""[{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }]""");
        CliContext context = fixture.CreateContext(TextWriter.Null);
        SweepDefinition definition = SweepFanOutCommand.WriteConfigs(
            context,
            "sweeps/test-sweep.json",
            validateGeneratedConfigs: false);
        string configPath = Path.Combine(
            fixture.RootPath,
            "sweeps",
            "test-sweep",
            "configs",
            "p0.json");

        SweepProvenanceDTO first = SweepArtifactExport.CreateProvenance(
            context,
            definition,
            fixture.DefinitionPath,
            [configPath],
            new SweepRunMetadata("test", false));
        File.AppendAllText(Path.Combine(fixture.RootPath, "demand.json"), Environment.NewLine);
        SweepProvenanceDTO second = SweepArtifactExport.CreateProvenance(
            context,
            definition,
            fixture.DefinitionPath,
            [configPath],
            new SweepRunMetadata("test", false));

        first.InputFiles.Single(input => input.Purpose == "demand-data").Sha256.Should().NotBe(
            second.InputFiles.Single(input => input.Purpose == "demand-data").Sha256);
    }

    [Fact]
    public void CreateProvenance_ListsDemandAndWeatherInputsForEveryRegion()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteTwoRegionBaseline();
        fixture.WriteDefinition("""[{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }]""");
        CliContext context = fixture.CreateContext(TextWriter.Null);
        SweepDefinition definition = SweepFanOutCommand.WriteConfigs(
            context,
            "sweeps/test-sweep.json",
            validateGeneratedConfigs: false);
        string configPath = Path.Combine(
            fixture.RootPath,
            "sweeps",
            "test-sweep",
            "configs",
            "p0.json");

        SweepProvenanceDTO provenance = SweepArtifactExport.CreateProvenance(
            context,
            definition,
            fixture.DefinitionPath,
            [configPath],
            new SweepRunMetadata("test", false));

        provenance.InputFiles.Count(input => input.Purpose == "demand-data").Should().Be(2);
        provenance.InputFiles.Count(input => input.Purpose == "weather-data").Should().Be(2);
    }

    [Fact]
    public void CreateProvenance_ResolvesScenarioInputsFromTheDataRoot()
    {
        using var fixture = new SweepRunFixture();
        fixture.MoveInputsToDataRoot();
        fixture.WriteDefinition("""[{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }]""");
        CliContext context = fixture.CreateContext(
            TextWriter.Null,
            dataRoot: SweepRunFixture.MovedInputDirectory);
        SweepDefinition definition = SweepFanOutCommand.WriteConfigs(
            context,
            "sweeps/test-sweep.json",
            validateGeneratedConfigs: false);
        string configPath = Path.Combine(
            fixture.RootPath,
            "sweeps",
            "test-sweep",
            "configs",
            "p0.json");

        SweepProvenanceDTO provenance = SweepArtifactExport.CreateProvenance(
            context,
            definition,
            fixture.DefinitionPath,
            [configPath],
            new SweepRunMetadata("test", false));

        provenance.InputFiles.Should().Contain(
            input => input.Purpose == "demand-data" && input.Path == "demand.json");
        provenance.InputFiles.Should().Contain(
            input => input.Purpose == "weather-data" && input.Path == "weather.json");
    }

    [Fact]
    public void CreateProvenance_DistinguishesCloseEconomicValuesInResolvedDefinition()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""[{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }]""");
        CliContext context = fixture.CreateContext(TextWriter.Null);
        SweepDefinition definition = SweepDefinition.Load("sweeps/test-sweep.json", fixture.Paths);
        SweepDefinition firstDefinition = definition with
        {
            Points = [definition.Points[0] with
            {
                Overrides = new JsonObject { ["realDiscountRate"] = 0.070001d },
            }],
        };
        SweepDefinition secondDefinition = definition with
        {
            Points = [definition.Points[0] with
            {
                Overrides = new JsonObject { ["realDiscountRate"] = 0.070002d },
            }],
        };

        SweepProvenanceDTO first = SweepArtifactExport.CreateProvenance(
            context,
            firstDefinition,
            fixture.DefinitionPath,
            [],
            new SweepRunMetadata("test", false));
        SweepProvenanceDTO second = SweepArtifactExport.CreateProvenance(
            context,
            secondDefinition,
            fixture.DefinitionPath,
            [],
            new SweepRunMetadata("test", false));

        first.ResolvedDefinitionSha256.Should().NotBe(second.ResolvedDefinitionSha256);
    }

    [Fact]
    public void CreateScalars_UsesDemandMinusUnservedEnergyWhenGenerationChargesStorage()
    {
        var result = new DispatchResultsDTO(
            ArtifactSchemaVersions.DispatchResults,
            new DispatchScenarioDTO("test", "Test", "NSW1", DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddHours(1), TimeSpan.FromHours(1)),
            DateTimeOffset.UnixEpoch,
            new DispatchSourcesDTO(
                new DispatchInputArtifactDTO("demand.json", 2, "demand"),
                new DispatchInputArtifactDTO("weather.json", 5, "weather"),
                new WeatherBasisDTO(
                    WeatherBasisKind.TypicalMeteorologicalYear,
                    new WeatherSiteDTO("test-solar.epw", "Test solar"),
                    new WeatherSiteDTO("test-wind.epw", "Test wind"),
                    "Typical meteorological year from test.epw."),
                []),
            new DispatchPowerSystemDTO("test", [], [new DispatchStorageFleetDTO("Battery", 20, 20)]),
            new DispatchSeriesDTO(
                new DispatchDemandDTO([100], new Dictionary<string, double[]> { ["Data centres"] = [50] }, [150]),
                new Dictionary<string, double[]> { ["Solar"] = [40], ["Wind"] = [20], ["Hydro"] = [30], ["Gas"] = [30] },
                [0],
                [10],
                [20],
                [0],
                new Dictionary<string, double[]> { ["Battery"] = [0] },
                [0],
                [0],
                [0]),
            new DispatchMetricsDTO(150, 120, 0, 10, 10.0 / 150 * 100, 1, 0, 10, new IntervalPointersDTO(0, null, 0)),
            new ReliabilityBasisDTO(0.002, 10, false, "NEM reliability standard"),
            new StorageSizingOutcomeDTO(StorageSizingOutcome.Resized, 10, 10, 20, 20, 400, 100, 2),
            new DispatchCostDTO(
                "calculated", 0, 0, 0, 0, 0, 0, 0, 0,
                TransmissionCostStatus.NotModelled, 0, []),
            new DispatchEmissionsDTO(
                27,
                27.0 / 140,
                [new DispatchGenerationEmissionsContributionDTO("Gas", 27, 27.0 / 140)]));

        SweepPointScalarResultsDTO scalars = SweepArtifactExport.CreateScalars(result);
        RenewableShareMetrics sourceMetrics = RenewableShareMetrics.FromDeliveredEnergy(
            new Dictionary<GenerationTechnology, double>
            {
                [GenerationTechnology.Solar] = 40,
                [GenerationTechnology.Wind] = 20,
                [GenerationTechnology.Hydro] = 30,
                [GenerationTechnology.Gas] = 30,
            },
            100);

        scalars.EnergyServedMwh.Should().Be(140);
        scalars.DemandMwh.Should().Be(150);
        scalars.DeliveredGenerationMwh.Should().Be(120);
        scalars.AchievedRenewableShareGridScale.Should().Be(sourceMetrics.GridScaleShare);
        scalars.AchievedRenewableShareNative.Should().Be(sourceMetrics.NativeShare);
        scalars.UnservedHours.Should().Be(1);
        scalars.PeakUnservedPowerMw.Should().Be(10);
    }

    [Fact]
    public void CreateScalars_RegionalStorageUsesPublishedBreakdownInsteadOfRoundedInputs()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""[{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }]""");
        SweepRunCommand.Run(fixture.CreateContext(TextWriter.Null), "sweeps/test-sweep.json")
            .Should().Be(0);
        string path = fixture.RegionResultPath("p0", "NSW1");
        JsonObject json = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        RestoreExternalizedBaseDemand(path, json);
        RegionDispatchResultsDTO result = json.Deserialize<RegionDispatchResultsDTO>(JsonFile.ReadOptions)!;
        result = result with
        {
            PowerSystem = result.PowerSystem with
            {
                StorageFleets = [new DispatchStorageFleetDTO("Battery", 300.1, 300.1)],
            },
            StorageSizing = new StorageSizingOutcomeDTO(
                StorageSizingOutcome.Resized, 150.0, 150.0, 300.1, 300.1, 400, 100, 2),
        };
        // Before serialization: 150.04 declared, 300.08 installed, 150.04 added.
        var published = new StorageCapacityBreakdownDTO(150.0, 150.0, 150.0, 150.0, 300.1, 300.1, 0, 0, 0, 0);

        SweepPointScalarResultsDTO scalars = SweepArtifactExport.CreateScalars(result, published);

        scalars.DeclaredBatteryEnergyMwh.Should().Be(150.0);
        scalars.AddedBatteryEnergyMwh.Should().Be(150.0);
        scalars.TotalBatteryEnergyMwh.Should().Be(300.1);
    }

    [Fact]
    public void Run_PartialCsv_FirstRunWritesSelectedDimensionAndPreservesUntouchedRows()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base,\nquoted \"label\"", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": {} }]
            """);
        SweepRunCommand.Run(fixture.CreateContext(TextWriter.Null, points: ["p0"], csv: true),
            "sweeps/test-sweep.json").Should().Be(0);
        string csvPath = Path.Combine(fixture.SweepDataPath, "csv", "dim_point.csv");
        string first = File.ReadAllText(csvPath);
        first.Should().Contain("p0,").And.NotContain("p1,");
        string factsPath = Path.Combine(fixture.SweepDataPath, "csv", "points", "p0", "fact_scalars.csv");
        byte[] facts = File.ReadAllBytes(factsPath);

        SweepRunCommand.Run(fixture.CreateContext(TextWriter.Null, points: ["p1"], csv: true),
            "sweeps/test-sweep.json").Should().Be(0);

        File.ReadAllText(csvPath).Should().StartWith(first).And.Contain("p1,");
        File.ReadAllBytes(factsPath).Should().Equal(facts);
    }

    [Fact]
    public void Run_PartialCsv_FailedRerunRemovesSelectedFactsAndPreservesUntouchedPoint()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": {} }]
            """);
        SweepRunCommand.Run(fixture.CreateContext(TextWriter.Null, csv: true), "sweeps/test-sweep.json")
            .Should().Be(0);
        string csvDirectory = Path.Combine(fixture.SweepDataPath, "csv");
        string untouchedFacts = Path.Combine(csvDirectory, "points", "p0", "fact_scalars.csv");
        byte[] before = File.ReadAllBytes(untouchedFacts);
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": { "costBasis": { "year": 1000 } } }]
            """);

        SweepRunCommand.Run(fixture.CreateContext(TextWriter.Null, points: ["p1"], csv: true),
            "sweeps/test-sweep.json").Should().Be(1);

        Directory.Exists(Path.Combine(csvDirectory, "points", "p1")).Should().BeFalse();
        File.ReadAllBytes(untouchedFacts).Should().Equal(before);
        File.ReadAllText(Path.Combine(csvDirectory, "dim_point.csv"))
            .Should().Contain("p0,Base,0,Succeeded,").And.Contain("p1,Changed,1,Failed,");
    }

    [Fact]
    public void Run_PointsSelection_RunsOnlyTheNamedPoints()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": { "name": "Changed scenario" } }]
            """);
        using var output = new StringWriter();

        SweepRunCommand.Run(fixture.CreateContext(output, points: ["p1"]), "sweeps/test-sweep.json")
            .Should().Be(0);

        File.Exists(fixture.PointResultPath("p1")).Should().BeTrue();
        File.Exists(fixture.PointResultPath("p0")).Should().BeFalse();
    }

    /// <summary>
    /// The results site reads <c>index.json</c> as the whole sweep. Rewriting it from a subset
    /// would silently drop every point this run did not touch, so a partial run publishes its own
    /// point artifacts and leaves everything that speaks for the sweep as a whole alone.
    /// </summary>
    [Fact]
    public void Run_PointsSelection_LeavesTheIndexAndSummaryUntouchedAndSaysSo()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": { "name": "Changed scenario" } }]
            """);
        SweepRunCommand.Run(fixture.CreateContext(TextWriter.Null), "sweeps/test-sweep.json")
            .Should().Be(0);
        byte[] indexBefore = File.ReadAllBytes(fixture.IndexPath);
        byte[] summaryBefore = File.ReadAllBytes(fixture.SummaryJsonPath);
        using var output = new StringWriter();

        SweepRunCommand.Run(fixture.CreateContext(output, points: ["p1"]), "sweeps/test-sweep.json")
            .Should().Be(0);

        File.ReadAllBytes(fixture.IndexPath).Should().Equal(indexBefore);
        File.ReadAllBytes(fixture.SummaryJsonPath).Should().Equal(summaryBefore);
        output.ToString().Should().Contain("Partial run of 1 of 2 points");
        output.ToString().Should().Contain("rerun without --points");
    }

    [Fact]
    public void Run_PointsNamingAnUnknownId_FailsAndListsTheRealIds()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }]
            """);

        Action run = () => SweepRunCommand.Run(
            fixture.CreateContext(TextWriter.Null, points: ["p9"]),
            "sweeps/test-sweep.json");

        run.Should().Throw<UsageException>()
            .WithMessage("*has no point p9*")
            .WithMessage("*Its points are: p0*");
    }

    [Fact]
    public void Run_WritesSummaryArtifactsAndPrintsTheTable()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 25, "label": "Changed", "overrides": { "name": "Changed scenario" } }]
            """);
        using var output = new StringWriter();

        SweepRunCommand.Run(fixture.CreateContext(output), "sweeps/test-sweep.json").Should().Be(0);

        JsonObject summary = JsonNode.Parse(File.ReadAllText(fixture.SummaryJsonPath))!.AsObject();
        summary["sweepId"]!.GetValue<string>().Should().Be("test-sweep");
        summary["axis"]!["label"]!.GetValue<string>().Should().Be("Capacity");
        JsonArray points = summary["points"]!.AsArray();
        points.Should().HaveCount(2);
        points[0]!["pointId"]!.GetValue<string>().Should().Be("p0");
        points[0]!["reliabilityTargetMet"]!.GetValue<bool>().Should().BeTrue();
        points[1]!["axisValue"]!.GetValue<double>().Should().Be(25);

        string[] csv = File.ReadAllLines(fixture.SummaryCsvPath);
        csv[0].Should().Be(
            "pointId,label,axisValue,status,reliabilityTargetMet,"
            + "unservedEnergyPercentageOfDemand,totalBatteryEnergyMwh,slcoeAudPerMwh");
        csv.Should().HaveCount(3);
        csv[1].Should().StartWith("\"p0\",\"Base\",0,\"succeeded\",true,");

        output.ToString().Should().Contain("Capacity (MW)");
        output.ToString().Should().Contain("battery MWh");
    }

    /// <summary>
    /// <c>status</c> answers whether the run completed, which is not the question a reader asks of
    /// it. A point that dispatched a full period and missed its target is <c>succeeded</c>, so the
    /// verdict needs a field of its own.
    /// </summary>
    [Fact]
    public void Run_StatusFile_RecordsTheReliabilityVerdictSeparatelyFromCompletion()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }]
            """);

        SweepRunCommand.Run(fixture.CreateContext(TextWriter.Null), "sweeps/test-sweep.json")
            .Should().Be(0);

        JsonObject status = Status(fixture, "p0");
        status["status"]!.GetValue<string>().Should().Be("succeeded");
        status["reliabilityTargetMet"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void Run_MissedTarget_SucceedsUnlessFailOnUnmetWasAskedFor()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteUnmeetableDefinition();
        using var output = new StringWriter();
        using var error = new StringWriter();

        int withoutFlag = SweepRunCommand.Run(
            fixture.CreateContext(output, error),
            "sweeps/test-sweep.json");
        int withFlag = SweepRunCommand.Run(
            fixture.CreateContext(output, error, failOnUnmet: true),
            "sweeps/test-sweep.json");

        withoutFlag.Should().Be(0);
        withFlag.Should().Be(1);
        Status(fixture, "p0")["status"]!.GetValue<string>().Should().Be("succeeded");
        Status(fixture, "p0")["reliabilityTargetMet"]!.GetValue<bool>().Should().BeFalse();
        error.ToString().Should().Contain("reliability target not met at points: p0");
        output.ToString().Should().Contain("NOT met");
    }

    private static JsonObject Status(SweepRunFixture fixture, string pointId) =>
        JsonNode.Parse(File.ReadAllText(fixture.PointStatusPath(pointId)))!.AsObject();

    private static JsonObject ReadIndex(SweepRunFixture fixture) =>
        JsonNode.Parse(File.ReadAllText(fixture.IndexPath))!.AsObject();

    /// <summary>Resolves an index point's <c>configPath</c> the way a reader of the published
    /// sweep would, so a citation that does not resolve to a file fails the test.</summary>
    private static string PublishedConfig(SweepRunFixture fixture, JsonObject point) =>
        Path.Combine(
            fixture.SweepDataPath,
            point["configPath"]!.GetValue<string>().Replace('/', Path.DirectorySeparatorChar));

    private static byte[] NormalizedResultBytes(string path)
    {
        JsonObject result = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        result.Remove("generatedAt");
        result.Remove("runId");
        return Encoding.UTF8.GetBytes(JsonFile.Serialize(result));
    }

    /// <summary>Strips wall-clock run-timing figures, which genuinely vary run to run.</summary>
    private static byte[] NormalizedIndexBytes(string path)
    {
        JsonObject index = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        index["provenance"]!.AsObject().Remove("totalDurationMs");
        foreach (JsonNode? point in index["points"]!.AsArray())
        {
            point!.AsObject().Remove("durationMs");
        }

        return Encoding.UTF8.GetBytes(JsonFile.Serialize(index));
    }

    private static void RestoreExternalizedBaseDemand(string pointResultPath, JsonObject result)
    {
        JsonObject demand = result["dataSeries"]!["demand"]!.AsObject();
        string relativePath = demand["baseDemandSeriesPath"]!.GetValue<string>();
        string seriesPath = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(pointResultPath)!,
            relativePath));
        JsonArray values = JsonNode.Parse(File.ReadAllText(seriesPath))!["valuesMw"]!.AsArray();
        demand["baseDemandMw"] = values.DeepClone();
        demand["baseDemandSeriesPath"] = null;
    }

    private static Dictionary<string, byte[]> SweepFiles(SweepRunFixture fixture)
    {
        string sweepPath = fixture.SweepDataPath;
        return Directory.GetFiles(sweepPath, "*.json", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(sweepPath, path),
                path => path.Contains($"{Path.DirectorySeparatorChar}points{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !path.EndsWith(".status.json", StringComparison.Ordinal)
                    ? NormalizedResultBytes(path)
                    : path.EndsWith("index.json", StringComparison.Ordinal)
                        ? NormalizedIndexBytes(path)
                        : File.ReadAllBytes(path),
                StringComparer.Ordinal);
    }

    private sealed class SweepRunFixture : IDisposable
    {
        private const int HoursPerYear = 8_760;

        public SweepRunFixture()
        {
            RootPath = Path.Combine(Directory.GetCurrentDirectory(), $"nemsweep-sweep-run-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(RootPath, "sweeps"));
            Directory.CreateDirectory(Path.Combine(RootPath, "scenarios"));
            File.WriteAllText(Path.Combine(RootPath, "NEMSweep.slnx"), string.Empty);
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(10));
            double[] zeroes = new double[HoursPerYear];
            File.WriteAllText(Path.Combine(RootPath, "demand.json"), JsonSerializer.Serialize(
                new ModelInputOutputDTO(
                    2,
                    new Scenario("test", "NSW1", start, start.AddYears(1), TimeSpan.FromHours(1), "hourly"),
                    start.ToUniversalTime(),
                    new Sources(["source.zip"]),
                    new Series(Enumerable.Repeat(10d, HoursPerYear).ToArray()))));
            File.WriteAllText(Path.Combine(RootPath, "weather.json"), JsonSerializer.Serialize(
                new WeatherDataDTO(
                    6,
                    "NSW1",
                    start,
                    TimeSpan.FromHours(1),
                    new SolarWeatherData(
                        "solar.epw",
                        new WeatherLocation("Test", "00000", -33.9, 151.2),
                        zeroes,
                        zeroes,
                        zeroes,
                        Enumerable.Repeat(90d, HoursPerYear).ToArray(),
                        Enumerable.Repeat(20d, HoursPerYear).ToArray(),
                        zeroes),
                    new WindWeatherData(
                        "wind.epw",
                        new WeatherLocation("Test", "00000", -33.9, 151.2),
                        Enumerable.Repeat(5d, HoursPerYear).ToArray(),
                        10,
                        zeroes))));
            File.WriteAllText(Path.Combine(RootPath, "scenarios", "baseline.json"), """
            { "schemaVersion": 6, "id": "baseline", "name": "Baseline", "costBasis": { "year": 2026, "realDiscountRate": 0.07 }, "storageSizing": { "maximumPowerMw": 100, "maximumEnergyMwh": 400 }, "regions": [{ "regionId": "NSW1", "demandFile": "demand.json", "weatherFile": "weather.json", "generatingFleets": [{ "technology": "Gas", "nameplateCapacityMw": 100, "costParameters": { "capitalCostAudPerMw": 0, "fixedOperatingCostAudPerMwYear": 0, "variableOperatingCostAudPerMwh": 0, "fuelPriceAudPerGj": 0 }, "technologyProfile": { "heatRateGjPerMwh": 7, "technicalLifeYears": 30, "emissionsIntensityTonnesPerMwh": 0.4 } }], "storageFleets": [{ "technology": "Battery", "initialEnergyCapacityMwh": 0, "initialPowerCapacityMw": 0, "costParameters": { "powerCapitalCostAudPerMw": 0, "energyCapitalCostAudPerMwh": 0, "fixedOperatingCostAudPerMwYear": 0 }, "technologyProfile": { "technicalLifeYears": 15, "roundTripEfficiency": 0.87 } }] }] }
            """);
            Paths = WorkspacePaths.FromRoots(RootPath, RootPath, Path.Combine(RootPath, "out"));
        }

        public string RootPath { get; }
        public WorkspacePaths Paths { get; }

        public CliSettings Settings { get; } =
            new("bundle", "data", "out", "scenarios/baseline.json");

        public CliContext CreateContext(
            TextWriter output,
            TextWriter? error = null,
            string? dataRoot = null,
            IReadOnlyList<string>? points = null,
            bool failOnUnmet = false,
            bool csv = false) =>
            new(
                dataRoot is null
                    ? Paths
                    : WorkspacePaths.FromRoots(RootPath, Path.Combine(RootPath, dataRoot), Path.Combine(RootPath, "out")),
                Settings,
                output,
                error,
                Csv: csv,
                Points: points,
                FailOnUnmet: failOnUnmet);

        public string SummaryJsonPath => Path.Combine(SweepDataPath, "summary.json");

        public string SummaryCsvPath => Path.Combine(SweepDataPath, "summary.csv");

        public string PointResultPath(string pointId) => Path.Combine(
            SweepDataPath, "points", $"{pointId}.json");

        public string RegionResultPath(string pointId, string regionId) => Path.Combine(
            SweepDataPath, "points", $"{pointId}-{regionId.ToLowerInvariant()}.json");

        public string PointStatusPath(string pointId) => Path.Combine(
            SweepDataPath, "points", $"{pointId}.status.json");

        public string SweepDataPath => Path.Combine(
            RootPath, "out", "sweeps", "test-sweep");

        public string IndexPath => Path.Combine(SweepDataPath, "index.json");

        public string ManifestPath => Path.Combine(
            RootPath, "out", "sweeps", "index.json");

        public string DefinitionPath => Path.Combine(RootPath, "sweeps", "test-sweep.json");

        public string SharedBaseSeriesPath => Directory.GetFiles(
            Path.Combine(SweepDataPath, "series"),
            "base-demand-*.json",
            SearchOption.TopDirectoryOnly).Single();

        public void WriteDefinition(string points) => File.WriteAllText(
            Path.Combine(RootPath, "sweeps", "test-sweep.json"),
            $$"""
            { "schemaVersion": 1, "sweepId": "test-sweep", "name": "Test sweep", "axis": { "label": "Capacity", "unit": "MW" }, "baselineConfigPath": "scenarios/baseline.json", "points": {{points}} }
            """);

        /// <summary>
        /// A single point whose generation energy is far below its demand energy, so no battery
        /// within the baseline's bounds can close the gap: the run completes and misses its target.
        /// </summary>
        public void WriteUnmeetableDefinition() => WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Starved",
               "overrides": {
                 "regions": [{ "regionId": "NSW1",
                   "generatingFleets": [{ "technology": "Gas", "nameplateCapacityMw": 1 }] }] } }]
            """);

        public void WriteTwoRegionBaseline()
        {
            File.Copy(
                Path.Combine(RootPath, "demand.json"),
                Path.Combine(RootPath, "demand-vic1.json"));
            File.Copy(
                Path.Combine(RootPath, "weather.json"),
                Path.Combine(RootPath, "weather-vic1.json"));
            File.WriteAllText(Path.Combine(RootPath, "scenarios", "baseline.json"), """
            { "schemaVersion": 6, "id": "baseline", "name": "Baseline", "costBasis": { "year": 2026, "realDiscountRate": 0.07 }, "storageSizing": { "maximumPowerMw": 100, "maximumEnergyMwh": 400 }, "regions": [{ "regionId": "NSW1", "demandFile": "demand.json", "weatherFile": "weather.json", "generatingFleets": [{ "technology": "Gas", "nameplateCapacityMw": 100, "costParameters": { "capitalCostAudPerMw": 0, "fixedOperatingCostAudPerMwYear": 0, "variableOperatingCostAudPerMwh": 0, "fuelPriceAudPerGj": 0 }, "technologyProfile": { "heatRateGjPerMwh": 7, "technicalLifeYears": 30, "emissionsIntensityTonnesPerMwh": 0.4 } }], "storageFleets": [{ "technology": "Battery", "initialEnergyCapacityMwh": 0, "initialPowerCapacityMw": 0, "costParameters": { "powerCapitalCostAudPerMw": 0, "energyCapitalCostAudPerMwh": 0, "fixedOperatingCostAudPerMwYear": 0 }, "technologyProfile": { "technicalLifeYears": 15, "roundTripEfficiency": 0.87 } }] }, { "regionId": "VIC1", "demandFile": "demand-vic1.json", "weatherFile": "weather-vic1.json", "generatingFleets": [{ "technology": "Gas", "nameplateCapacityMw": 100, "costParameters": { "capitalCostAudPerMw": 0, "fixedOperatingCostAudPerMwYear": 0, "variableOperatingCostAudPerMwh": 0, "fuelPriceAudPerGj": 0 }, "technologyProfile": { "heatRateGjPerMwh": 7, "technicalLifeYears": 30, "emissionsIntensityTonnesPerMwh": 0.4 } }], "storageFleets": [{ "technology": "Battery", "initialEnergyCapacityMwh": 0, "initialPowerCapacityMw": 0, "costParameters": { "powerCapitalCostAudPerMw": 0, "energyCapitalCostAudPerMwh": 0, "fixedOperatingCostAudPerMwYear": 0 }, "technologyProfile": { "technicalLifeYears": 15, "roundTripEfficiency": 0.87 } }] }] }
            """);
        }

        public const string MovedInputDirectory = "published-inputs";

        /// <summary>Moves the scenario inputs somewhere only a data-root override reaches.</summary>
        public void MoveInputsToDataRoot()
        {
            string fullDirectory = Path.Combine(RootPath, MovedInputDirectory);
            Directory.CreateDirectory(fullDirectory);
            File.Move(
                Path.Combine(RootPath, "demand.json"),
                Path.Combine(fullDirectory, "demand.json"));
            File.Move(
                Path.Combine(RootPath, "weather.json"),
                Path.Combine(fullDirectory, "weather.json"));
        }

        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}