using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using NEM.CLI.Application;
using NEM.CLI.Configuration;
using NEM.CLI.Infrastructure;
using NEM.CLI.Scenarios;
using NEM.Contracts;

namespace NEM.CLI.Tests.Scenarios;

[Trait("Category", "FullYearAcceptance")]
public sealed class SweepRunTests
{
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
            .GetValue<int>().Should().Be(4);
        Status(fixture, "p0")["status"]!.GetValue<string>().Should().Be("succeeded");
        Status(fixture, "p1")["status"]!.GetValue<string>().Should().Be("succeeded");
        JsonObject index = ReadIndex(fixture);
        index["schemaVersion"]!.GetValue<int>().Should().Be(1);
        index["points"]!.AsArray().Should().HaveCount(2);
        JsonObject firstPoint = index["points"]![0]!.AsObject();
        firstPoint["status"]!.GetValue<string>().Should().Be("succeeded");
        firstPoint["detailPath"]!.GetValue<string>().Should().Be("points/p0.json");
        firstPoint["configPath"]!.GetValue<string>().Should().Be("configs/p0.json");
        firstPoint["scalars"]!["energyServedMwh"]!.GetValue<double>().Should().Be(87_600);
        firstPoint["scalars"]!["achievedRenewableShareGridScale"].Should().BeNull();
        firstPoint["scalars"]!["achievedRenewableShareNative"].Should().BeNull();
        index["provenance"]!["inputFiles"]!.AsArray().Select(input => input!["purpose"]!.GetValue<string>())
            .Should().Contain(["demand-data", "weather-data", "sweep-definition"]);
        new FileInfo(fixture.IndexPath).Length.Should().BeLessThan(10_000);
        File.Exists(fixture.SharedBaseSeriesPath).Should().BeTrue();
        JsonObject pointResult = JsonNode.Parse(File.ReadAllText(fixture.PointResultPath("p0")))!.AsObject();
        pointResult["dataSeries"]!["demand"]!["baseDemandMw"].Should().BeNull();
        pointResult["dataSeries"]!["demand"]!["baseDemandSeriesPath"]!.GetValue<string>()
            .Should().StartWith("../series/base-demand-");
        output.ToString().Should().Contain("Running sweep point p1 (Capacity=1 MW).");
    }

    [Fact]
    public void Run_ContinuesAfterPointFailureAndReturnsNonZeroSummary()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Invalid", "overrides": { "regions": [] } }]
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
        output.ToString().Should().NotContain("failed");
        error.ToString().Should().Contain("Sweep point p1: failed:");
        error.ToString().Should().Contain("failed points: p1");
    }

    [Fact]
    public void Run_ProducesTheSameResultAsTheSingleScenarioPathAfterRestoringBaseDemand()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": { "name": "Changed scenario" } }]
            """);
        using var output = new StringWriter();
        CliContext context = fixture.CreateContext(output);

        SweepRunCommand.Run(context, "sweeps/test-sweep.json").Should().Be(0);
        string standalonePath = Path.Combine(fixture.RootPath, "standalone-p1.json");
        ScenarioCommand.Run(context, "sweeps/test-sweep/configs/p1.json", standalonePath).Should().Be(0);

        JsonObject pointResult = JsonNode.Parse(File.ReadAllText(fixture.PointResultPath("p1")))!.AsObject();
        JsonObject standaloneResult = JsonNode.Parse(File.ReadAllText(standalonePath))!.AsObject();
        pointResult["dataSeries"]!["demand"]!["baseDemandMw"].Should().BeNull();
        standaloneResult["dataSeries"]!["demand"]!["baseDemandMw"]!.AsArray()
            .Should().HaveCount(8_760);
        RestoreExternalizedBaseDemand(fixture.PointResultPath("p1"), pointResult);
        pointResult.Remove("generatedAt");
        standaloneResult.Remove("generatedAt");
        JsonNode.DeepEquals(pointResult, standaloneResult).Should().BeTrue();
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
            4,
            new DispatchScenarioDTO("test", "Test", "NSW1", DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddHours(1), TimeSpan.FromHours(1)),
            DateTimeOffset.UnixEpoch,
            new DispatchSourcesDTO(
                new DispatchInputArtifactDTO("demand.json", 2, "demand"),
                new DispatchInputArtifactDTO("weather.json", 5, "weather"),
                []),
            new DispatchPowerSystemDTO("test", [], [new DispatchStorageFleetDTO("Battery", 20, 20)]),
            new DispatchSeriesDTO(
                new DispatchDemandDTO([100], [], [100]),
                new Dictionary<string, double[]> { ["Solar"] = [120] },
                [0],
                [10],
                [20],
                [0],
                new Dictionary<string, double[]> { ["Battery"] = [0] }),
            new DispatchMetricsDTO(100, 120, 0, 10, 10, 1, 0, 10),
            new DispatchCostDTO("calculated", 0, 0, 0, 0, 0, 0));

        SweepArtifactExport.CreateScalars(result).EnergyServedMwh.Should().Be(90);
    }

    private static JsonObject Status(SweepRunFixture fixture, string pointId) =>
        JsonNode.Parse(File.ReadAllText(fixture.PointStatusPath(pointId)))!.AsObject();

    private static JsonObject ReadIndex(SweepRunFixture fixture) =>
        JsonNode.Parse(File.ReadAllText(fixture.IndexPath))!.AsObject();

    private static byte[] NormalizedResultBytes(string path)
    {
        JsonObject result = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        result.Remove("generatedAt");
        return Encoding.UTF8.GetBytes(JsonFile.Serialize(result));
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
                    : File.ReadAllBytes(path),
                StringComparer.Ordinal);
    }

    private sealed class SweepRunFixture : IDisposable
    {
        private const int HoursPerYear = 8_760;

        public SweepRunFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"nemsim-sweep-run-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(RootPath, "sweeps"));
            Directory.CreateDirectory(Path.Combine(RootPath, "scenarios"));
            File.WriteAllText(Path.Combine(RootPath, "NemSim.slnx"), string.Empty);
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
                    5,
                    "test.epw",
                    new WeatherLocation("Test", "00000", -33.9, 151.2),
                    start,
                    TimeSpan.FromHours(1),
                    10,
                    new WeatherSeriesData(
                        zeroes,
                        zeroes,
                        zeroes,
                        Enumerable.Repeat(90d, HoursPerYear).ToArray(),
                        Enumerable.Repeat(20d, HoursPerYear).ToArray(),
                        Enumerable.Repeat(5d, HoursPerYear).ToArray(),
                        zeroes,
                        zeroes))));
            File.WriteAllText(Path.Combine(RootPath, "scenarios", "baseline.json"), """
            { "id": "baseline", "name": "Baseline", "demandFile": "demand.json", "weatherFile": "weather.json", "costBasis": { "year": 2026, "realDiscountRate": 0.07 }, "storageSizing": { "maximumPowerMw": 100, "maximumEnergyMwh": 400 }, "regions": [{ "regionId": "NSW1", "generatingFleets": [{ "technology": "Gas", "nameplateCapacityMw": 100, "costParameters": { "capitalCostAudPerMw": 0, "fixedOperatingCostAudPerMwYear": 0, "variableOperatingCostAudPerMwh": 0, "fuelPriceAudPerGj": 0 }, "technologyProfile": { "heatRateGjPerMwh": 7, "technicalLifeYears": 30 } }], "storageFleets": [{ "technology": "Battery", "initialEnergyCapacityMwh": 0, "initialPowerCapacityMw": 0, "costParameters": { "powerCapitalCostAudPerMw": 0, "energyCapitalCostAudPerMwh": 0, "fixedOperatingCostAudPerMwYear": 0 }, "technologyProfile": { "technicalLifeYears": 15, "roundTripEfficiency": 0.87 } }] }] }
            """);
            Paths = RepositoryPaths.Discover(RootPath);
        }

        public string RootPath { get; }
        public RepositoryPaths Paths { get; }

        public CliContext CreateContext(TextWriter output, TextWriter? error = null) =>
            new(Paths, RootPath, output, error);

        public string PointResultPath(string pointId) => Path.Combine(
            SweepDataPath, "points", $"{pointId}.json");

        public string PointStatusPath(string pointId) => Path.Combine(
            SweepDataPath, "points", $"{pointId}.status.json");

        public string SweepDataPath => Path.Combine(
            RootPath, "NEM.Web", "wwwroot", "data", "sweeps", "test-sweep");

        public string IndexPath => Path.Combine(SweepDataPath, "index.json");

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

        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}