using AwesomeAssertions;
using NEMSweep.CLI.Application;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.Contracts;
using System.Text.Json;

namespace NEMSweep.CLI.Tests.Application;

public sealed class CommandRouterTests
{
    [Fact]
    public void WorkspacePaths_ReadInputsFromDataRootAndWriteResultsToOutputRoot()
    {
        using var fixture = new CliFixture();
        WorkspacePaths paths = fixture.Paths;

        paths.WeatherDataPath("NSW1").Should().Be(
            Path.Combine(fixture.RootPath, "data", "weather-nsw1.json"));
        paths.DispatchResultsPath.Should().Be(
            Path.Combine(fixture.RootPath, "out", "results.json"));
        paths.ResolveConfiguredPath(Path.Combine("scenarios", "one.json")).Should().Be(
            Path.Combine(fixture.RootPath, "scenarios", "one.json"));
    }

    [Fact]
    public void WorkspacePaths_PreferCommandLineOverrideOverConfiguredRoot()
    {
        using var fixture = new CliFixture();

        WorkspacePaths paths = WorkspacePaths.Create(
            fixture.Settings,
            fixture.RootPath,
            dataRootOverride: "elsewhere",
            outputRootOverride: null);

        paths.DataRoot.Should().Be(Path.Combine(fixture.RootPath, "elsewhere"));
        paths.OutputRoot.Should().Be(Path.Combine(fixture.RootPath, "out"));
    }

    [Theory]
    [InlineData("--data-root")]
    [InlineData("--output")]
    public void Run_RejectsAnOverrideMissingItsValueAsInvalidUsage(string flag)
    {
        using var fixture = new CliFixture();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CommandRouter(fixture.RootPath, fixture.RootPath, output, error);

        int exitCode = application.Run(["--run-scenario", flag]);

        exitCode.Should().Be(2);
        error.ToString().Should().Contain($"{flag} requires a directory.");
        error.ToString().Should().Contain("Usage:");
        output.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Run_RejectsUnknownCommandWithoutLoadingSettings()
    {
        using var fixture = new CliFixture();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CommandRouter(fixture.RootPath, fixture.RootPath, output, error);

        int exitCode = application.Run(["--unknown"]);

        exitCode.Should().Be(2);
        error.ToString().Should().Contain("Usage:");
        output.ToString().Should().BeEmpty();
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Run_PrintsUsageToOutputAndSucceedsWhenHelpIsRequested(string flag)
    {
        using var fixture = new CliFixture();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CommandRouter(fixture.RootPath, fixture.RootPath, output, error);

        int exitCode = application.Run([flag]);

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("Usage:").And.Contain("--run-scenario");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Run_ListsEveryRoutedCommandInUsage()
    {
        using var fixture = new CliFixture();
        using var output = new StringWriter();
        var application = new CommandRouter(fixture.RootPath, fixture.RootPath, output, TextWriter.Null);

        application.Run(["--help"]).Should().Be(0);

        string usage = output.ToString();
        string[] routed =
        [
            "--version", "--run-scenario", "--fan-out-sweep", "--run-sweep", "--describe-schema",
            "--validate-inputs", "--ingest", "--import-demand", "--generation-information",
            "--epw-report",
        ];
        foreach (string command in routed)
        {
            usage.Should().Contain(command);
        }
    }

    [Fact]
    public void Run_ReportsAVersion()
    {
        using var fixture = new CliFixture();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CommandRouter(fixture.RootPath, fixture.RootPath, output, error);

        application.Run(["--version"]).Should().Be(0);

        output.ToString().Should().StartWith("NEMSweep.CLI ");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Run_RejectsABarePathInsteadOfSilentlyImportingDemand()
    {
        using var fixture = new CliFixture();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CommandRouter(fixture.RootPath, fixture.RootPath, output, error);

        int exitCode = application.Run(["scenarios/nem-fy2026-all-regions.json"]);

        exitCode.Should().Be(2);
        error.ToString().Should().Contain("Usage:");
        output.ToString().Should().BeEmpty();
    }

    [Fact]
    public void EpwReport_RejectsARegionThatIsNotANemRegion()
    {
        using var fixture = new CliFixture();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CommandRouter(fixture.RootPath, fixture.RootPath, output, error);

        int exitCode = application.Run(["--epw-report", "NSW", "solar.epw"]);

        exitCode.Should().Be(1);
        error.ToString().Should().Contain("is not a NEM region");
    }

    [Theory]
    [InlineData("scenario", ArtifactSchemaVersions.ScenarioConfig)]
    [InlineData("sweep", ArtifactSchemaVersions.SweepDefinition)]
    public void DescribeSchema_PublishesTheSchemaVersionTheValidatorAccepts(
        string format,
        int expectedVersion)
    {
        using var fixture = new CliFixture();
        using var output = new StringWriter();
        var application = new CommandRouter(fixture.RootPath, fixture.RootPath, output, TextWriter.Null);

        application.Run(["--describe-schema", format]).Should().Be(0);

        using JsonDocument document = JsonDocument.Parse(output.ToString());
        document.RootElement
            .GetProperty("properties")
            .GetProperty("schemaVersion")
            .GetProperty("const")
            .GetInt32()
            .Should().Be(expectedVersion);
    }

    [Fact]
    public void Run_ReportsCommandFailuresWithoutLeakingExceptions()
    {
        using var fixture = new CliFixture();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CommandRouter(fixture.RootPath, fixture.RootPath, output, error);

        int exitCode = application.Run(["--epw-report", "NSW1", "missing.epw"]);

        exitCode.Should().Be(1);
        error.ToString().Should().StartWith("EPW report failed:");
    }

    [Theory]
    [InlineData("scenario", "regionId", "demandFile", "weatherFile", "dataCentreNameplateMw", "fromRegionId", "toRegionId", "capacityMw")]
    [InlineData("sweep", "overrides", "regions", "regionId", "interconnectors", "fromRegionId", "toRegionId", "$remove")]
    public void DescribeSchema_WritesDeterministicStrictSchema(
        string format,
        params string[] expectedProperties)
    {
        using var fixture = new CliFixture();
        using var firstOutput = new StringWriter();
        using var firstError = new StringWriter();
        using var secondOutput = new StringWriter();
        var first = new CommandRouter(fixture.RootPath, fixture.RootPath, firstOutput, firstError);
        var second = new CommandRouter(fixture.RootPath, fixture.RootPath, secondOutput, TextWriter.Null);

        first.Run(["--describe-schema", format]).Should().Be(0);
        second.Run(["--describe-schema", format]).Should().Be(0);

        firstOutput.ToString().Should().Be(secondOutput.ToString());
        using JsonDocument document = JsonDocument.Parse(firstOutput.ToString());
        document.RootElement.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        foreach (string property in expectedProperties)
        {
            firstOutput.ToString().Should().Contain($"\"{property}\"");
        }
        firstError.ToString().Should().BeEmpty();
    }

    // A keyed-array element is found by its key, and JsonMergePatch rejects a null key outright, so
    // the published schema must not validate an override point the merge can never apply.
    [Theory]
    [InlineData("regionOverride", "regionId")]
    [InlineData("interconnectorOverride", "fromRegionId")]
    [InlineData("interconnectorOverride", "toRegionId")]
    [InlineData("generatingFleetOverride", "technology")]
    [InlineData("storageFleetOverride", "technology")]
    [InlineData("monthlyCapacityFactorOverride", "month")]
    public void DescribeSchema_SweepKeyedArrayKeyFieldsAreNotNullable(string definition, string keyField)
    {
        using var fixture = new CliFixture();
        using var output = new StringWriter();
        var application = new CommandRouter(fixture.RootPath, fixture.RootPath, output, TextWriter.Null);

        application.Run(["--describe-schema", "sweep"]).Should().Be(0);

        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement type = document.RootElement
            .GetProperty("$defs").GetProperty(definition)
            .GetProperty("properties").GetProperty(keyField)
            .GetProperty("type");
        type.ValueKind.Should().Be(JsonValueKind.String);
        type.GetString().Should().Be("string");
    }

    [Fact]
    public void DescribeSchema_RejectsMissingOrUnknownFormat()
    {
        using var fixture = new CliFixture();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CommandRouter(fixture.RootPath, fixture.RootPath, output, error);

        application.Run(["--describe-schema"]).Should().Be(2);
        application.Run(["--describe-schema", "contract"]).Should().Be(2);
        error.ToString().Should().Contain("--describe-schema <scenario|sweep>");
        output.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Settings_PreferLocalFileOverExample()
    {
        using var fixture = new CliFixture();
        fixture.WriteSettings("appsettings.example.json", "Example scenario");
        fixture.WriteSettings("appsettings.local.json", "Local scenario");

        CliSettings settings = CliSettings.Load(fixture.RootPath);

        settings.InputBundleRoot.Should().Be("data/nemsweep-inputs");
        settings.DataRoot.Should().Be("data");
        settings.OutputRoot.Should().Be("out");
        settings.DefaultScenarioPath.Should().Be("scenarios/Local scenario.json");
    }

    [Fact]
    public void RunScenario_WithoutPath_ResolvesConfiguredDefaultFromWorkingRoot()
    {
        using var fixture = new CliFixture();
        fixture.WriteSettings("appsettings.local.json", "Local scenario");
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "scenarios"));
        File.WriteAllText(
                Path.Combine(fixture.RootPath, "scenarios", "Local scenario.json"),
                """
                        {
                            "schemaVersion": 6,
                            "id": "test-scenario",
                            "name": "Test scenario",
                            "costBasis": { "year": 2026, "realDiscountRate": 0.07 },
                            "storageSizing": { "maximumPowerMw": 100, "maximumEnergyMwh": 400 },
                            "regions": [{
                                "regionId": "NSW1",
                                "demandFile": "missing-demand.json",
                                "weatherFile": "missing-weather.json",
                                "storageFleets": [{
                                    "technology": "Battery",
                                    "initialEnergyCapacityMwh": 0,
                                    "initialPowerCapacityMw": 0,
                                    "costParameters": { "powerCapitalCostAudPerMw": 0, "energyCapitalCostAudPerMwh": 0, "fixedOperatingCostAudPerMwYear": 0 },
                                    "technologyProfile": { "technicalLifeYears": 15, "roundTripEfficiency": 0.87 }
                                }],
                                "generatingFleets": [{
                                    "technology": "Gas",
                                    "nameplateCapacityMw": 100,
                                    "costParameters": { "capitalCostAudPerMw": 0, "fixedOperatingCostAudPerMwYear": 0, "variableOperatingCostAudPerMwh": 0, "fuelPriceAudPerGj": 0 },
                                    "technologyProfile": { "heatRateGjPerMwh": 7, "technicalLifeYears": 30, "emissionsIntensityTonnesPerMwh": 0.4 }
                                }]
                            }]
                        }
                        """);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CommandRouter(fixture.RootPath, fixture.RootPath, output, error);

        int exitCode = application.Run(["--run-scenario"]);

        exitCode.Should().Be(1);
        error.ToString().Should().Contain(
            Path.Combine(fixture.RootPath, "data", "missing-demand.json"));
    }

    private sealed class CliFixture : IDisposable
    {
        public CliFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"nemsweep-cli-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public CliSettings Settings { get; } =
            new("data/nemsweep-inputs", "data", "out", "scenarios/unused.json");

        public WorkspacePaths Paths =>
            WorkspacePaths.Create(Settings, RootPath, null, null);

        public void WriteSettings(string fileName, string scenarioName)
        {
            var settings = new
            {
                inputBundleRoot = "data/nemsweep-inputs",
                dataRoot = "data",
                outputRoot = "out",
                defaultScenarioPath = $"scenarios/{scenarioName}.json",
            };
            File.WriteAllText(
                Path.Combine(RootPath, fileName),
                JsonSerializer.Serialize(settings));
        }

        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}