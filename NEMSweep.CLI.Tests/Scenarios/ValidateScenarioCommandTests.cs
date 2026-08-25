using System.Text.Json;
using AwesomeAssertions;
using NEMSweep.CLI.Application;

namespace NEMSweep.CLI.Tests.Scenarios;

/// <summary>
/// Validation exists so a bad config costs a parse rather than a dispatch, and so a caller
/// correcting one can read a field instead of a sentence. Both are what these cover.
/// </summary>
public sealed class ValidateScenarioCommandTests
{
    [Fact]
    public void ValidateScenario_ReportsAValidConfigAndSucceeds()
    {
        using var fixture = new ValidateFixture();
        fixture.WriteScenario("good.json", extraRegionField: null);

        (int exitCode, string output, _) = fixture.Run(["--validate-scenario", "good.json"]);

        exitCode.Should().Be(0);
        output.Should().Contain("is valid").And.Contain("NSW1");
    }

    [Fact]
    public void ValidateScenario_RejectsAnInventedFieldWithoutDispatching()
    {
        using var fixture = new ValidateFixture();
        fixture.WriteScenario("bad.json", extraRegionField: "\"invented\": true,");

        (int exitCode, _, string error) = fixture.Run(["--validate-scenario", "bad.json"]);

        exitCode.Should().Be(1);
        error.Should().Contain("invented");
    }

    [Fact]
    public void ValidateScenario_InJsonReportsAFailureACallerCanActOn()
    {
        using var fixture = new ValidateFixture();
        fixture.WriteScenario("bad.json", extraRegionField: "\"invented\": true,");

        (int exitCode, string output, _) = fixture.Run(
            ["--validate-scenario", "bad.json", "--format", "json"]);

        exitCode.Should().Be(1);
        using JsonDocument report = JsonDocument.Parse(output);
        report.RootElement.GetProperty("valid").GetBoolean().Should().BeFalse();
        JsonElement failure = report.RootElement.GetProperty("error");
        failure.GetProperty("code").GetString().Should().Be("invalidConfig");
        failure.GetProperty("stage").GetString().Should().Be("Input");
        failure.GetProperty("message").GetString().Should().Contain("invented");
    }

    [Fact]
    public void ValidateScenario_InJsonDescribesAValidConfigWithoutAnErrorBlock()
    {
        using var fixture = new ValidateFixture();
        fixture.WriteScenario("good.json", extraRegionField: null);

        (int exitCode, string output, _) = fixture.Run(
            ["--validate-scenario", "good.json", "--format", "json"]);

        exitCode.Should().Be(0);
        using JsonDocument report = JsonDocument.Parse(output);
        report.RootElement.GetProperty("valid").GetBoolean().Should().BeTrue();
        report.RootElement.TryGetProperty("error", out _).Should().BeFalse();
        report.RootElement.GetProperty("regions").EnumerateArray()
            .Select(region => region.GetString()).Should().Equal("NSW1");
    }

    [Fact]
    public void ValidateScenario_AcceptsASchemaHintSoAnEditorCanValidateTheFile()
    {
        using var fixture = new ValidateFixture();
        fixture.WriteScenario(
            "hinted.json",
            extraRegionField: null,
            schemaHint: "\"$schema\": \"https://example.invalid/scenario.json\",");

        (int exitCode, string output, _) = fixture.Run(["--validate-scenario", "hinted.json"]);

        exitCode.Should().Be(0);
        output.Should().Contain("is valid");
    }

    private sealed class ValidateFixture : IDisposable
    {
        public ValidateFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"nemsweep-validate-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
            File.WriteAllText(
                Path.Combine(RootPath, "appsettings.local.json"),
                """{"inputBundleRoot":"bundle","dataRoot":"data","outputRoot":"out","defaultScenarioPath":"good.json"}""");
        }

        public string RootPath { get; }

        public (int ExitCode, string Output, string Error) Run(string[] args)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exitCode = new CommandRouter(RootPath, RootPath, output, error).Run(args);
            return (exitCode, output.ToString(), error.ToString());
        }

        public void WriteScenario(string name, string? extraRegionField, string? schemaHint = null) =>
            File.WriteAllText(
                Path.Combine(RootPath, name),
                $$"""
                {
                  {{schemaHint}}
                  "schemaVersion": 5,
                  "id": "validate-fixture",
                  "name": "Validate fixture",
                  "costBasis": { "year": 2026, "realDiscountRate": 0.07 },
                  "storageSizing": { "maximumPowerMw": 100, "maximumEnergyMwh": 400 },
                  "regions": [{
                    {{extraRegionField}}
                    "regionId": "NSW1",
                    "demandFile": "demand.json",
                    "weatherFile": "weather.json",
                    "generatingFleets": [{
                      "technology": "Gas",
                      "nameplateCapacityMw": 100,
                      "costParameters": { "capitalCostAudPerMw": 0, "fixedOperatingCostAudPerMwYear": 0, "variableOperatingCostAudPerMwh": 0, "fuelPriceAudPerGj": 0 },
                      "technologyProfile": { "heatRateGjPerMwh": 7, "technicalLifeYears": 30 }
                    }],
                    "storageFleets": [{
                      "technology": "Battery",
                      "initialEnergyCapacityMwh": 0,
                      "initialPowerCapacityMw": 0,
                      "costParameters": { "powerCapitalCostAudPerMw": 0, "energyCapitalCostAudPerMwh": 0, "fixedOperatingCostAudPerMwYear": 0 },
                      "technologyProfile": { "technicalLifeYears": 15, "roundTripEfficiency": 0.87 }
                    }]
                  }]
                }
                """);

        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}
