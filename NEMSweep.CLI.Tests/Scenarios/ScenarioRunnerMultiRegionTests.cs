using System.Text.Json;
using AwesomeAssertions;
using NEMSweep.CLI.Application;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.CLI.Scenarios;
using NEMSweep.Contracts;
using NEMSweep.Model.Grid;
using NEMSweep.Model.StorageSizing;
using NEMSweep.Model.Units;
using DomainScenario = NEMSweep.Model.Scenarios.Scenario;
using ContractsScenario = NEMSweep.Contracts.Scenario;

namespace NEMSweep.CLI.Tests.Scenarios;

public sealed class ScenarioRunnerMultiRegionTests
{
    [Fact]
    public void RunDispatch_LoadsEachRegionalInputAndProducesTwoRegionalResults()
    {
        using var fixture = new RunnerFixture();
        ScenarioDispatchResult result = fixture.Run();

        result.PowerSystem.Regions.Select(region => region.RegionId)
            .Should().Equal("NSW1", "VIC1");
        result.SizingResult.Regions.Select(region => region.DispatchOutcome.RegionId)
            .Should().Equal("NSW1", "VIC1");
        result.DemandInputs.Keys.Should().BeEquivalentTo("NSW1", "VIC1");
        result.WeatherInputs.Keys.Should().BeEquivalentTo("NSW1", "VIC1");
        result.DemandInputs["NSW1"].Artifact.FileName.Should().Be("demand-nsw1.json");
        result.DemandInputs["VIC1"].Artifact.FileName.Should().Be("demand-vic1.json");
    }

    [Fact]
    public void RunDispatch_RejectsDemandArtifactForWrongRegionWithNamedCode()
    {
        using var fixture = new RunnerFixture { DemandRegionForNsw = "VIC1" };

        var act = () => fixture.Run();

        ScenarioRunException exception = act.Should().Throw<ScenarioRunException>().Which;
        exception.Stage.Should().Be(SweepFailureStage.Input);
        exception.Code.Should().Be("demandRegionMismatch");
        exception.Message.Should().Contain("NSW1").And.Contain("VIC1");
    }

    [Fact]
    public void Run_WeatherArtifactRegionMismatch_FailsWithNamedCode()
    {
        using var fixture = new RunnerFixture { WeatherRegionForNsw = "VIC1" };

        var act = () => fixture.Run();

        ScenarioRunException exception = act.Should().Throw<ScenarioRunException>().Which;
        exception.Stage.Should().Be(SweepFailureStage.Input);
        exception.Code.Should().Be("weatherRegionMismatch");
        exception.Message.Should().Contain("NSW1").And.Contain("VIC1");
    }

    [Fact]
    public void RunDispatch_RejectsMisalignedDemandTimelinesWithNamedCode()
    {
        using var fixture = new RunnerFixture { VicStart = fixtureStart.AddHours(1) };

        var act = () => fixture.Run();

        ScenarioRunException exception = act.Should().Throw<ScenarioRunException>().Which;
        exception.Stage.Should().Be(SweepFailureStage.Input);
        exception.Code.Should().Be("demandTimelineMismatch");
        exception.Message.Should().Contain("VIC1");
    }

    [Fact]
    public void RunDispatch_AppliesDataCentreOnlyToDeclaringRegion()
    {
        using var fixture = new RunnerFixture
        {
            NswDataCentreNameplateMw = 5,
            VicDataCentreNameplateMw = 0,
        };
        ScenarioDispatchResult result = fixture.Run();

        result.PowerSystem.Regions.Single(region => region.RegionId == "NSW1")
            .Demand.AdditiveComponents.Single().Demand[0].Megawatts.Should().Be(5);
        result.PowerSystem.Regions.Single(region => region.RegionId == "VIC1")
            .Demand.AdditiveComponents.Should().BeEmpty();
    }

    [Fact]
    public void Run_PublishesSystemAndRegionalResultsWithOneRunId()
    {
        using var fixture = new RunnerFixture { IncludeInterconnector = true };
        fixture.WriteScenario();
        var context = new CliContext(fixture.Paths, fixture.Settings, TextWriter.Null);

        ScenarioCommand.Run(context, "scenario.json").Should().Be(0);

        SystemDispatchResultsDTO system = JsonSerializer.Deserialize<SystemDispatchResultsDTO>(
            File.ReadAllBytes(fixture.Paths.DispatchResultsPath),
            JsonFile.ReadOptions)!;
        RegionDispatchResultsDTO nsw = JsonSerializer.Deserialize<RegionDispatchResultsDTO>(
            File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(fixture.Paths.DispatchResultsPath)!, "results-nsw1.json")),
            JsonFile.ReadOptions)!;
        RegionDispatchResultsDTO vic = JsonSerializer.Deserialize<RegionDispatchResultsDTO>(
            File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(fixture.Paths.DispatchResultsPath)!, "results-vic1.json")),
            JsonFile.ReadOptions)!;

        system.RegionIds.Should().Equal("NSW1", "VIC1");
        system.RunId.Should().NotBeNullOrWhiteSpace();
        nsw.RunId.Should().Be(system.RunId);
        vic.RunId.Should().Be(system.RunId);
        system.DataSourcesByRegion["NSW1"].DemandInput.FileName.Should().Be("demand-nsw1.json");
        system.DataSourcesByRegion["VIC1"].WeatherInput.FileName.Should().Be("weather-vic1.json");
        system.RegionSummariesById["NSW1"].DetailPath.Should().Be("results-nsw1.json");
        system.RegionSummariesById["NSW1"].DeliveredGenerationByTechnologyMwh.Values.Sum()
            .Should().Be(nsw.Metrics.DeliveredGenerationMwh);
        system.RegionSummariesById["VIC1"].DeliveredGenerationByTechnologyMwh.Values.Sum()
            .Should().Be(vic.Metrics.DeliveredGenerationMwh);
        nsw.RegionId.Should().Be("NSW1");
        vic.RegionId.Should().Be("VIC1");
        system.Topology.RegionIds.Should().Equal("NSW1", "VIC1");
        system.Topology.Links.Should().ContainSingle().Which.Should().Be(
            new DispatchTopologyLinkDTO("NSW1->VIC1", "NSW1", "VIC1", 30));
        DispatchInterconnectorDTO link = system.Interconnectors.Should().ContainSingle().Subject;
        link.Id.Should().Be("NSW1->VIC1");
        link.FromRegionId.Should().Be("NSW1");
        link.ToRegionId.Should().Be("VIC1");
        link.CapacityMw.Should().Be(30);
        link.FlowMw.Should().HaveCount(8_760);
        link.LossesMw.Should().HaveCount(8_760);
        nsw.DataSeries.TransmissionLossesMw.Should().OnlyContain(value => value == 0);
        vic.DataSeries.TransmissionLossesMw.Should().Equal(system.DataSeries.TransmissionLossesMw);
        nsw.DataSeries.TransmissionLossesMw
            .Zip(vic.DataSeries.TransmissionLossesMw)
            .Select(losses => losses.First + losses.Second)
            .Should().Equal(system.DataSeries.TransmissionLossesMw);
    }

    [Fact]
    public void RunDispatch_RealisesConfiguredInterconnectorAndRetainsSolverEvidence()
    {
        using var fixture = new RunnerFixture { IncludeInterconnector = true };

        ScenarioDispatchResult result = fixture.Run();

        Interconnector link = result.PowerSystem.Interconnectors.Should().ContainSingle().Subject;
        link.FromRegionId.Should().Be("NSW1");
        link.ToRegionId.Should().Be("VIC1");
        link.Capacity.Should().Be(Power.FromMegawatts(30));
        result.SizingResult.InterconnectorFlows.Should().ContainSingle();
        result.SizingResult.InterconnectorFlows[0].Flow.Length.Should().Be(8_760);
        result.SizingResult.InterconnectorFlows[0].Losses.Length.Should().Be(8_760);
    }

    [Fact]
    public void Run_PublishesNonCompliantResultInsteadOfThrowingWhenSizingCannotMeetTarget()
    {
        double[] spikyDemand = Enumerable.Repeat(10d, 8_760).ToArray();
        for (int hour = 0; hour < spikyDemand.Length; hour += 400)
        {
            spikyDemand[hour] = 300;
        }

        using var fixture = new RunnerFixture
        {
            NswDemandSeries = spikyDemand,
            MaximumPowerMw = 50,
            MaximumEnergyMwh = 200,
        };
        fixture.WriteScenario();
        var context = new CliContext(fixture.Paths, fixture.Settings, TextWriter.Null);

        ScenarioCommand.Run(context, "scenario.json").Should().Be(0);

        SystemDispatchResultsDTO system = JsonSerializer.Deserialize<SystemDispatchResultsDTO>(
            File.ReadAllBytes(fixture.Paths.DispatchResultsPath),
            JsonFile.ReadOptions)!;
        system.Reliability.WithinTarget.Should().BeFalse();
        system.StorageSizing.Outcome.Should().BeOneOf(
            StorageSizingOutcome.StorageNoLongerImprovesReliability,
            StorageSizingOutcome.BatteryCapacityLimitReached,
            StorageSizingOutcome.PassLimitReached);
    }

    /// <summary>
    /// The sizing loop enforces its limit on each region separately, so a system artifact whose
    /// capacities are summed across the regions has to sum the ceiling with them. Publishing the
    /// per-region limit beside a two-region total put a fleet inside its limit past its ceiling.
    /// </summary>
    [Fact]
    public void Run_SumsTheStorageSizingCeilingAcrossRegionsOnTheSystemArtifact()
    {
        using var fixture = new RunnerFixture();
        fixture.WriteScenario();
        var context = new CliContext(fixture.Paths, fixture.Settings, TextWriter.Null);

        ScenarioCommand.Run(context, "scenario.json").Should().Be(0);

        SystemDispatchResultsDTO system = JsonSerializer.Deserialize<SystemDispatchResultsDTO>(
            File.ReadAllBytes(fixture.Paths.DispatchResultsPath),
            JsonFile.ReadOptions)!;
        system.RegionIds.Should().Equal("NSW1", "VIC1");
        system.StorageSizing.MaximumPowerMw.Should().Be(200);
        system.StorageSizing.MaximumEnergyMwh.Should().Be(800);
        system.StorageSizing.FinalPowerMw.Should().BeLessThanOrEqualTo(system.StorageSizing.MaximumPowerMw);
        system.StorageSizing.FinalEnergyMwh.Should().BeLessThanOrEqualTo(system.StorageSizing.MaximumEnergyMwh);

        RegionDispatchResultsDTO region = JsonSerializer.Deserialize<RegionDispatchResultsDTO>(
            File.ReadAllBytes(Path.Combine(
                Path.GetDirectoryName(fixture.Paths.DispatchResultsPath)!,
                "results-nsw1.json")),
            JsonFile.ReadOptions)!;
        region.StorageSizing.MaximumPowerMw.Should().Be(100);
        region.StorageSizing.MaximumEnergyMwh.Should().Be(400);
    }

    [Fact]
    public void WritePublication_DoesNotPublishPartialResultsWhenAWriteFails()
    {
        using var fixture = new RunnerFixture();
        ScenarioDispatchResult dispatch = fixture.Run();
        string resultsPath = Path.Combine(fixture.OutputRoot, "results.json");
        string regionalPath = Path.Combine(fixture.OutputRoot, "results-nsw1.json");
        File.WriteAllText(resultsPath, "existing-system");
        File.WriteAllText(regionalPath, "existing-region");

        var act = () => DispatchResultsExport.WritePublication(
            fixture.CreatePublicationRequest(dispatch),
            resultsPath,
            (path, contents) =>
            {
                if (path.EndsWith("results-vic1.json", StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("injected write failure");
                }

                File.WriteAllText(path, contents);
            });

        act.Should().Throw<IOException>();
        File.ReadAllText(resultsPath).Should().Be("existing-system");
        File.ReadAllText(regionalPath).Should().Be("existing-region");
        Directory.GetDirectories(fixture.OutputRoot, ".dispatch-results-*", SearchOption.TopDirectoryOnly)
            .Should().BeEmpty();
    }

    private static readonly DateTimeOffset fixtureStart =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(10));

    private sealed class RunnerFixture : IDisposable
    {
        public RunnerFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"nemsweep-runner-{Guid.NewGuid():N}");
            OutputRoot = Path.Combine(RootPath, "output");
            Directory.CreateDirectory(OutputRoot);
            Directory.CreateDirectory(Path.Combine(RootPath, "NEMSweep.CLI"));
            File.WriteAllText(Path.Combine(RootPath, "NEMSweep.slnx"), string.Empty);
            File.WriteAllText(
                Path.Combine(RootPath, "NEMSweep.CLI", "appsettings.local.json"),
                "{\"inputBundleRoot\":\"unused\",\"dataRoot\":\"output\",\"outputRoot\":\"results\",\"defaultScenarioPath\":\"unused\"}");
            WriteDemand("nsw1", "NSW1", fixtureStart, 10);
            WriteDemand("vic1", "VIC1", VicStart, 20);
            WriteWeather("nsw1", "NSW1");
            WriteWeather("vic1", "VIC1");
        }

        public string RootPath { get; }
        public string OutputRoot { get; }
        public WorkspacePaths Paths =>
            WorkspacePaths.FromRoots(RootPath, OutputRoot, Path.Combine(RootPath, "results"));

        public CliSettings Settings { get; } =
            new("bundle", "output", "results", "unused.json");
        public string DemandRegionForNsw { get; init; } = "NSW1";
        public string WeatherRegionForNsw { get; init; } = "NSW1";
        public DateTimeOffset VicStart { get; init; } = fixtureStart;
        public double NswDataCentreNameplateMw { get; init; }
        public double VicDataCentreNameplateMw { get; init; }
        public bool IncludeInterconnector { get; init; }
        public double[]? NswDemandSeries { get; init; }
        public double MaximumPowerMw { get; init; } = 100;
        public double MaximumEnergyMwh { get; init; } = 400;
        public int MaximumPasses { get; init; } = 4;

        public ScenarioDispatchResult Run()
        {
            if (DemandRegionForNsw != "NSW1")
            {
                WriteDemand("nsw1", DemandRegionForNsw, fixtureStart, 10);
            }

            if (WeatherRegionForNsw != "NSW1")
            {
                WriteWeather("nsw1", WeatherRegionForNsw);
            }

            if (VicStart != fixtureStart)
            {
                WriteDemand("vic1", "VIC1", VicStart, 20);
            }

            return ScenarioRunner.RunDispatch(CreateSettings(), Paths);
        }

        public DispatchPublicationRequest CreatePublicationRequest(ScenarioDispatchResult dispatch) =>
            new(
                dispatch,
                new StorageSizingOptions(
                    Power.FromMegawatts(100),
                    Energy.FromMegawattHours(400),
                    0.002,
                    4),
                null);

        public void WriteScenario()
        {
            if (NswDemandSeries is not null)
            {
                WriteDemand("nsw1", "NSW1", fixtureStart, NswDemandSeries);
            }

            string interconnectors = IncludeInterconnector
                ? "\"interconnectors\": [{ \"fromRegionId\": \"NSW1\", \"toRegionId\": \"VIC1\", \"capacityMw\": 30, \"routeLengthKm\": 714.2, \"capitalCostAudPerKmPerMw\": 1000, \"fixedOperatingCostAudPerKmPerMwYear\": 10, \"technicalLifeYears\": 50 }],"
                : string.Empty;
            File.WriteAllText(
            Path.Combine(RootPath, "scenario.json"),
            $$"""
            { "schemaVersion": 6, "id": "two-region", "name": "Two region", "costBasis": { "year": 2026, "realDiscountRate": 0.07 }, {{interconnectors}} "regions": [
              { "regionId": "NSW1", "demandFile": "demand-nsw1.json", "weatherFile": "weather-nsw1.json", "generatingFleets": [{ "technology": "Gas", "nameplateCapacityMw": 100, "costParameters": { "capitalCostAudPerMw": 0, "fixedOperatingCostAudPerMwYear": 0, "variableOperatingCostAudPerMwh": 0, "fuelPriceAudPerGj": 0 }, "technologyProfile": { "heatRateGjPerMwh": 7, "technicalLifeYears": 30, "emissionsIntensityTonnesPerMwh": 0.4 } }], "storageFleets": [{ "technology": "Battery", "initialEnergyCapacityMwh": 0, "initialPowerCapacityMw": 0, "costParameters": { "powerCapitalCostAudPerMw": 0, "energyCapitalCostAudPerMwh": 0, "fixedOperatingCostAudPerMwYear": 0 }, "technologyProfile": { "technicalLifeYears": 15, "roundTripEfficiency": 0.87 } }] },
              { "regionId": "VIC1", "demandFile": "demand-vic1.json", "weatherFile": "weather-vic1.json", "generatingFleets": [{ "technology": "Gas", "nameplateCapacityMw": 100, "costParameters": { "capitalCostAudPerMw": 0, "fixedOperatingCostAudPerMwYear": 0, "variableOperatingCostAudPerMwh": 0, "fuelPriceAudPerGj": 0 }, "technologyProfile": { "heatRateGjPerMwh": 7, "technicalLifeYears": 30, "emissionsIntensityTonnesPerMwh": 0.4 } }], "storageFleets": [{ "technology": "Battery", "initialEnergyCapacityMwh": 0, "initialPowerCapacityMw": 0, "costParameters": { "powerCapitalCostAudPerMw": 0, "energyCapitalCostAudPerMwh": 0, "fixedOperatingCostAudPerMwYear": 0 }, "technologyProfile": { "technicalLifeYears": 15, "roundTripEfficiency": 0.87 } }] }
            ], "storageSizing": { "maximumPowerMw": {{MaximumPowerMw}}, "maximumEnergyMwh": {{MaximumEnergyMwh}}, "maximumPasses": {{MaximumPasses}} } }
            """);
        }

        private ScenarioSettings CreateSettings() => new(
                3,
                "two-region",
                "Two region",
                new CostBasisSettings(2026, 0.07m),
                [Region("NSW1", "demand-nsw1.json", "weather-nsw1.json", NswDataCentreNameplateMw),
                 Region("VIC1", "demand-vic1.json", "weather-vic1.json", VicDataCentreNameplateMw)],
                new StorageSizingSettings(100, 400, MaximumPasses: 4),
                Interconnectors: IncludeInterconnector ? [InterconnectorSettings()] : null);

        private static ScenarioInterconnectorSettings InterconnectorSettings() => new(
            "NSW1",
            "VIC1",
            30,
            714.2,
            1_000,
            10,
            50);

        public void Dispose() => Directory.Delete(RootPath, recursive: true);

        private static ScenarioRegionSettings Region(
            string regionId,
            string demandFile,
            string weatherFile,
            double dataCentreNameplateMw) =>
            new(
                regionId,
                [new GeneratingFleetSettings(
                    "Gas",
                    100,
                    new CostParametersSettings(0, 0, 0, 0),
                    new GenerationTechnologyProfileSettings(7, 30, 0.36))],
                [new StorageFleetSettings(
                    "Battery",
                    0,
                    0,
                    new StorageCostParametersSettings(0, 0, 0),
                    new StorageTechnologyProfileSettings(15, 0.87))],
                demandFile,
                weatherFile,
                dataCentreNameplateMw);

        private void WriteDemand(string name, string region, DateTimeOffset start, double value) =>
            WriteDemand(name, region, start, Enumerable.Repeat(value, 8_760).ToArray());

        private void WriteDemand(string name, string region, DateTimeOffset start, double[] values)
        {
            File.WriteAllText(
                Path.Combine(OutputRoot, $"demand-{name}.json"),
                JsonSerializer.Serialize(new ModelInputOutputDTO(
                    2,
                    new ContractsScenario("test", region, start, start.AddYears(1), TimeSpan.FromHours(1), "hourly"),
                    start.ToUniversalTime(),
                    new Sources(["source.zip"]),
                    new Series(values))));
        }

        private void WriteWeather(string name, string region)
        {
            double[] zeroes = new double[8_760];
            File.WriteAllText(
                Path.Combine(OutputRoot, $"weather-{name}.json"),
                JsonSerializer.Serialize(new WeatherDataDTO(
                    6,
                    region,
                    new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.FromHours(10)),
                    TimeSpan.FromHours(1),
                    new SolarWeatherData(
                        "solar.epw",
                        new WeatherLocation("Test", "00000", -33.9, 151.2),
                        zeroes,
                        zeroes,
                        zeroes,
                        zeroes,
                        Enumerable.Repeat(20d, 8_760).ToArray(),
                        zeroes),
                    new WindWeatherData(
                        "wind.epw",
                        new WeatherLocation("Test", "00000", -33.9, 151.2),
                        Enumerable.Repeat(5d, 8_760).ToArray(),
                        10,
                        zeroes))));
        }
    }

}
