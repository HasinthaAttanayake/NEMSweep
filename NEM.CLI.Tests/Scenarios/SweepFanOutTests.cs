using System.Text.Json.Nodes;
using AwesomeAssertions;
using NEM.CLI.Application;
using NEM.CLI.Configuration;
using NEM.CLI.Infrastructure;
using NEM.CLI.Scenarios;

namespace NEM.CLI.Tests.Scenarios;

public sealed class SweepFanOutTests
{
    [Fact]
    public void Load_ParsesValidDefinition()
    {
        using var fixture = new SweepFixture();
        fixture.WriteDefinition("""[{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }]""");

        SweepDefinition definition = SweepDefinition.Load("sweeps/test-sweep.json", fixture.Paths);

        definition.SweepId.Should().Be("test-sweep");
        definition.Points.Should().ContainSingle().Which.PointId.Should().Be("p0");
    }

    [Theory]
    [InlineData("[{ \"pointId\": \"p0\", \"axisValue\": 0, \"label\": \"Base\", \"overrides\": {} }, { \"pointId\": \"p0\", \"axisValue\": 1, \"label\": \"Duplicate\", \"overrides\": {} }]", "duplicate point id 'p0'")]
    [InlineData("[]", "at least one point is required")]
    [InlineData("[{ \"pointId\": \"unsafe_id\", \"axisValue\": 0, \"label\": \"Base\", \"overrides\": {} }]", "point 'unsafe_id' must have a filename-safe id")]
    [InlineData("[{ \"pointId\": \"p0\", \"axisValue\": 0, \"label\": \" \", \"overrides\": {} }]", "point 'p0': label is required")]
    public void Load_RejectsInvalidPoints(string points, string expectedMessage)
    {
        using var fixture = new SweepFixture();
        fixture.WriteDefinition(points);

        Action act = () => SweepDefinition.Load("sweeps/test-sweep.json", fixture.Paths);

        act.Should().Throw<FormatException>().WithMessage($"*{expectedMessage}*");
    }

    [Fact]
    public void Load_RejectsMissingBaseline()
    {
        using var fixture = new SweepFixture();
        fixture.WriteDefinition("[]", baselineConfigPath: "scenarios/missing.json");

        Action act = () => SweepDefinition.Load("sweeps/test-sweep.json", fixture.Paths);

        act.Should().Throw<FormatException>()
            .WithMessage("*baseline config 'scenarios/missing.json' was not found*");
    }

    [Fact]
    public void Load_RejectsBlankName()
    {
        using var fixture = new SweepFixture();
        fixture.WriteDefinition(
            "[{ \"pointId\": \"p0\", \"axisValue\": 0, \"label\": \"Base\", \"overrides\": {} }]",
            name: " ");

        Action act = () => SweepDefinition.Load("sweeps/test-sweep.json", fixture.Paths);

        act.Should().Throw<FormatException>().WithMessage("*name is required*");
    }

    [Fact]
    public void Run_WritesStandaloneDeterministicConfigsWithProvenance()
    {
        using var fixture = new SweepFixture();
        fixture.WriteDefinition("""[{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }, { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": { "name": "Changed" } }]""");
        using var output = new StringWriter();
        var context = new CliContext(fixture.Paths, fixture.RootPath, output);

        SweepFanOutCommand.Run(context, "sweeps/test-sweep.json");
        string path = Path.Combine(fixture.RootPath, "sweeps", "test-sweep", "configs", "p0.json");
        string first = File.ReadAllText(path);
        SweepFanOutCommand.Run(context, "sweeps/test-sweep.json");

        File.ReadAllText(path).Should().Be(first);
        JsonObject config = JsonNode.Parse(first)!.AsObject();
        config["id"]!.GetValue<string>().Should().Be("test-sweep-p0");
        config["provenance"]!["sweepId"]!.GetValue<string>().Should().Be("test-sweep");
        ScenarioConfig.Load(path).Id.Should().Be("test-sweep-p0");
    }

    [Fact]
    public void Run_EmptyOverridePreservesBaselineConfigValues()
    {
        using var fixture = new SweepFixture();
        fixture.WriteDefinition("""[{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }]""");
        using var output = new StringWriter();
        var context = new CliContext(fixture.Paths, fixture.RootPath, output);

        SweepFanOutCommand.Run(context, "sweeps/test-sweep.json");

        JsonObject baseline = JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixture.RootPath, "scenarios", "baseline.json")))!.AsObject();
        JsonObject emitted = JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixture.RootPath, "sweeps", "test-sweep", "configs", "p0.json")))!.AsObject();
        baseline.Remove("id");
        emitted.Remove("id");
        emitted.Remove("provenance");

        JsonNode canonicalBaseline = JsonNode.Parse(JsonFile.SerializeExact(baseline))!;
        JsonNode.DeepEquals(emitted, canonicalBaseline).Should().BeTrue();
        JsonArray fleets = emitted["regions"]![0]!["generatingFleets"]!.AsArray();
        fleets[0]!["costParameters"]!["fuelPriceAudPerGj"]!.GetValue<double>().Should().Be(4.175);
        fleets[0]!["technologyProfile"]!["heatRateGjPerMwh"]!.GetValue<double>().Should().Be(8.547);
        fleets[1]!["costParameters"]!["fuelPriceAudPerGj"]!.GetValue<double>().Should().Be(15.313);
        fleets[1]!["technologyProfile"]!["heatRateGjPerMwh"]!.GetValue<double>().Should().Be(7.073);
    }

    [Fact]
    public void Run_KeyedArrayOverride_MergesOnlyNamedFleets()
    {
        using var fixture = new SweepFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p1", "axisValue": 0.1, "label": "10% uplift", "overrides": {
                "regions": [{ "regionId": "NSW1", "generatingFleets": [
                    { "technology": "Coal", "nameplateCapacityMw": 110 }
                ] }]
            } }]
            """);

        SweepFanOutCommand.Run(new CliContext(fixture.Paths, fixture.RootPath, TextWriter.Null), "sweeps/test-sweep.json");

        JsonArray fleets = JsonNode.Parse(File.ReadAllText(Path.Combine(
            fixture.RootPath,
            "sweeps",
            "test-sweep",
            "configs",
            "p1.json")))!["regions"]![0]!["generatingFleets"]!.AsArray();
        fleets.Select(fleet => fleet!["technology"]!.GetValue<string>()).Should().Equal(
            "Coal", "Gas");
        fleets[0]!["nameplateCapacityMw"]!.GetValue<double>().Should().Be(110);
        fleets[1]!["nameplateCapacityMw"]!.GetValue<double>().Should().Be(100);
    }

    private sealed class SweepFixture : IDisposable
    {
        public SweepFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"nemsim-sweep-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(RootPath, "sweeps"));
            Directory.CreateDirectory(Path.Combine(RootPath, "scenarios"));
            File.WriteAllText(Path.Combine(RootPath, "NemSim.slnx"), string.Empty);
            File.WriteAllText(Path.Combine(RootPath, "scenarios", "baseline.json"), """
            {
              "schemaVersion": 3, "id": "baseline", "name": "Baseline",
              "costBasis": { "year": 2026, "realDiscountRate": 0.07 },
              "storageSizing": { "maximumPowerMw": 100, "maximumEnergyMwh": 400 },
              "regions": [{
                                "regionId": "NSW1", "demandFile": "demand.json", "weatherFile": "weather.json", "generatingFleets": [
                                    { "technology": "Coal", "nameplateCapacityMw": 100, "costParameters": { "capitalCostAudPerMw": 0, "fixedOperatingCostAudPerMwYear": 0, "variableOperatingCostAudPerMwh": 0, "fuelPriceAudPerGj": 4.175 }, "technologyProfile": { "heatRateGjPerMwh": 8.547, "technicalLifeYears": 30 } },
                                    { "technology": "Gas", "nameplateCapacityMw": 100, "costParameters": { "capitalCostAudPerMw": 0, "fixedOperatingCostAudPerMwYear": 0, "variableOperatingCostAudPerMwh": 0, "fuelPriceAudPerGj": 15.313 }, "technologyProfile": { "heatRateGjPerMwh": 7.073, "technicalLifeYears": 30 } }
                                ],
                "storageFleets": [{ "technology": "Battery", "initialEnergyCapacityMwh": 0, "initialPowerCapacityMw": 0, "costParameters": { "powerCapitalCostAudPerMw": 0, "energyCapitalCostAudPerMwh": 0, "fixedOperatingCostAudPerMwYear": 0 }, "technologyProfile": { "technicalLifeYears": 15, "roundTripEfficiency": 0.87 } }]
              }]
            }
            """);
            Paths = RepositoryPaths.Discover(RootPath);
        }

        public string RootPath { get; }
        public RepositoryPaths Paths { get; }

        public void WriteDefinition(
            string points,
            string baselineConfigPath = "scenarios/baseline.json",
            string name = "Test sweep") =>
            File.WriteAllText(Path.Combine(RootPath, "sweeps", "test-sweep.json"), $$"""
            { "schemaVersion": 1, "sweepId": "test-sweep", "name": "{{name}}", "axis": { "label": "Capacity", "unit": "MW" }, "baselineConfigPath": "{{baselineConfigPath}}", "points": {{points}} }
            """);

        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}