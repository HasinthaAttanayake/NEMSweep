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

    [Fact]
    public void Settings_PreferLocalFileOverExample()
    {
        using var fixture = new CliFixture();
        fixture.WriteSettings("appsettings.example.json", "Example scenario");
        fixture.WriteSettings("appsettings.local.json", "Local scenario");

        CliSettings settings = CliSettings.Load(fixture.RootPath);

        settings.Scenario.Name.Should().Be("Local scenario");
        settings.Scenario.Regions.Single().StorageFleets.Single().TechnologyProfile
            .Should().Be(new StorageTechnologyProfileSettings(15u, 0.87));
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
                operationalDemand = new
                {
                    archiveDirectory = "NEM.CLI/data/demand-zips",
                    region = "NSW1",
                    periodStart = "2025-07-01T00:00:00+10:00",
                },
                scenario = new
                {
                    id = "test-scenario",
                    name = scenarioName,
                    demandFile = "demand.json",
                    weatherFile = "weather.json",
                    costBasis = new
                    {
                        year = 2026,
                        realDiscountRate = 0.07,
                    },
                    storageSizing = new
                    {
                        maximumPowerMw = 100,
                        maximumEnergyMwh = 400,
                    },
                    regions = new[]
                    {
                        new
                        {
                            regionId = "NSW1",
                            storageFleets = new[]
                            {
                                new
                                {
                                    technology = "Battery",
                                    initialEnergyCapacityMwh = 0,
                                    initialPowerCapacityMw = 0,
                                    costParameters = new
                                    {
                                        powerCapitalCostAudPerMw = 0m,
                                        energyCapitalCostAudPerMwh = 0m,
                                        fixedOperatingCostAudPerMwYear = 0m,
                                    },
                                    technologyProfile = new
                                    {
                                        technicalLifeYears = 15u,
                                        roundTripEfficiency = 0.87,
                                    },
                                },
                            },
                            generatingFleets = new[]
                            {
                                new
                                {
                                    technology = "Gas",
                                    nameplateCapacityMw = 100,
                                    costParameters = new
                                    {
                                        capitalCostAudPerMw = 0m,
                                        fixedOperatingCostAudPerMwYear = 0m,
                                        variableOperatingCostAudPerMwh = 0m,
                                        fuelPriceAudPerGj = 0m,
                                    },
                                    technologyProfile = new
                                    {
                                        heatRateGjPerMwh = 7,
                                        technicalLifeYears = 30,
                                    },
                                },
                            },
                        },
                    },
                },
            };
            File.WriteAllText(
                Path.Combine(RootPath, fileName),
                JsonSerializer.Serialize(settings));
        }

        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}