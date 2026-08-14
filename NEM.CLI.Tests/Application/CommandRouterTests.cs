using AwesomeAssertions;
using NEM.CLI.Application;
using NEM.CLI.Configuration;
using NEM.CLI.Infrastructure;
using System.Text.Json;

namespace NEM.CLI.Tests.Application;

public sealed class CommandRouterTests
{
    [Fact]
    public void RepositoryPaths_SeparatesOutputsAndResolvesInputsFromSolutionRoot()
    {
        using var fixture = new CliFixture();
        RepositoryPaths paths = fixture.Paths;

        paths.DemandDataPath.Should().EndWith(
            Path.Combine("NEM.Web", "wwwroot", "data", "demand-data.json"));
        paths.DispatchResultsPath.Should().EndWith(
            Path.Combine("NEM.Web", "wwwroot", "data", "results.json"));
        paths.DemandDataPath.Should().NotBe(paths.DispatchResultsPath);
        paths.ResolveConfiguredPath(Path.Combine("NEM.CLI", "data", "demand-zips")).Should().Be(
            Path.Combine(fixture.RootPath, "NEM.CLI", "data", "demand-zips"));
    }

    [Fact]
    public void Run_RejectsUnknownCommandWithoutLoadingSettings()
    {
        using var fixture = new CliFixture();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CommandRouter(fixture.Paths, fixture.RootPath, output, error);

        int exitCode = application.Run(["--unknown"]);

        exitCode.Should().Be(2);
        error.ToString().Should().Contain("Usage:");
        output.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Run_ReportsCommandFailuresWithoutLeakingExceptions()
    {
        using var fixture = new CliFixture();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CommandRouter(fixture.Paths, fixture.RootPath, output, error);

        int exitCode = application.Run(["--epw-header", "missing.epw"]);

        exitCode.Should().Be(1);
        error.ToString().Should().StartWith("EPW header report failed:");
    }

    [Theory]
    [InlineData("scenario", "regionId", "demandFile", "weatherFile", "dataCentreNameplateMw", "fromRegionId", "toRegionId", "capacityMw")]
    [InlineData("sweep", "overrides", "regions", "regionId", "$remove")]
    public void DescribeSchema_WritesDeterministicStrictSchema(
        string format,
        params string[] expectedProperties)
    {
        using var fixture = new CliFixture();
        using var firstOutput = new StringWriter();
        using var firstError = new StringWriter();
        using var secondOutput = new StringWriter();
        var first = new CommandRouter(fixture.Paths, fixture.RootPath, firstOutput, firstError);
        var second = new CommandRouter(fixture.Paths, fixture.RootPath, secondOutput, TextWriter.Null);

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

    [Fact]
    public void DescribeSchema_RejectsMissingOrUnknownFormat()
    {
        using var fixture = new CliFixture();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CommandRouter(fixture.Paths, fixture.RootPath, output, error);

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

        settings.InputBundleRoot.Should().Be("NEM.CLI/data/nemsim-inputs");
        settings.OutputRoot.Should().Be("NEM.Web/wwwroot/data");
        settings.DefaultScenarioPath.Should().Be("scenarios/Local scenario.json");
    }

    [Fact]
    public void RunScenario_WithoutPath_ResolvesConfiguredDefaultFromSolutionRoot()
    {
        using var fixture = new CliFixture();
        fixture.WriteSettings("appsettings.local.json", "Local scenario");
                Directory.CreateDirectory(Path.Combine(fixture.RootPath, "scenarios"));
                File.WriteAllText(
                        Path.Combine(fixture.RootPath, "scenarios", "Local scenario.json"),
                        """
                        {
                            "schemaVersion": 3,
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
                                    "technologyProfile": { "heatRateGjPerMwh": 7, "technicalLifeYears": 30 }
                                }]
                            }]
                        }
                        """);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CommandRouter(fixture.Paths, fixture.RootPath, output, error);

        int exitCode = application.Run(["--run-scenario"]);

        exitCode.Should().Be(1);
        error.ToString().Should().Contain(Path.Combine(fixture.RootPath, "missing-demand.json"));
    }

    private sealed class CliFixture : IDisposable
    {
        public CliFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"nemsim-cli-{Guid.NewGuid():N}");
            string nestedPath = Path.Combine(RootPath, "NEM.CLI", "bin", "Debug", "net10.0");
            Directory.CreateDirectory(nestedPath);
            File.WriteAllText(Path.Combine(RootPath, "NemSim.slnx"), string.Empty);
            Paths = RepositoryPaths.Discover(nestedPath);
        }

        public string RootPath { get; }
        public RepositoryPaths Paths { get; }

        public void WriteSettings(string fileName, string scenarioName)
        {
            var settings = new
            {
                inputBundleRoot = "NEM.CLI/data/nemsim-inputs",
                outputRoot = "NEM.Web/wwwroot/data",
                defaultScenarioPath = $"scenarios/{scenarioName}.json",
            };
            File.WriteAllText(
                Path.Combine(RootPath, fileName),
                JsonSerializer.Serialize(settings));
        }

        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}