using AwesomeAssertions;
using NEM.CLI.Demand;
using NEM.CLI.Scenarios;
using NEM.Contracts;
using NEM.Model.Economics;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.StorageSizing;
using NEM.Model.Units;
using System.Text;
using System.Text.Json;
using DomainScenario = NEM.Model.Scenarios.Scenario;

namespace NEM.CLI.Tests.Scenarios;

public sealed class DispatchResultsContractTests
{
    [Fact]
    public void V5_RoundTripsWithVersionAndExplicitUnits()
    {
        var start = new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));
        var result = new DispatchResultsDTO(
            ArtifactSchemaVersions.DispatchResults,
            new DispatchScenarioDTO(
                "nsw1-baseline-dispatch",
                "NSW1 baseline dispatch",
                "NSW1",
                start,
                start.AddHours(2),
                TimeSpan.FromHours(1)),
            DateTimeOffset.UtcNow,
            new DispatchSourcesDTO(
                new DispatchInputArtifactDTO("demand-data.json", 2, new string('a', 64)),
                new DispatchInputArtifactDTO("weather-data.json", 5, new string('b', 64)),
                new WeatherBasisDTO(
                    WeatherBasisKind.TypicalMeteorologicalYear,
                    "sydney.epw",
                    "Sydney (WMO 947680)",
                    "Typical meteorological year from sydney.epw."),
                ["demand.zip"]),
            new DispatchPowerSystemDTO(
                "nsw1-baseline-dispatch-system",
                [new DispatchFleetDTO("Solar", 100)],
                [new DispatchStorageFleetDTO("Battery", 120, 30)]),
            new DispatchSeriesDTO(
                new DispatchDemandDTO(
                    [70, 80],
                    new Dictionary<string, double[]> { ["Data centres"] = [10, 10] },
                    [80, 90]),
                new Dictionary<string, double[]> { ["Solar"] = [80, 85] },
                [20, 15],
                [0, 5],
                [10, 0],
                [0, 5],
                new Dictionary<string, double[]> { ["Battery"] = [0, 8.7] }),
            new DispatchMetricsDTO(
                170,
                165,
                35,
                5,
                5.0 / 170 * 100,
                1,
                0.5,
                5,
                new IntervalPointersDTO(1, 0, 0)),
            new ReliabilityBasisDTO(0.002, 5.0 / 170 * 100, false, "NEM reliability standard"),
            new StorageSizingOutcomeDTO(
                StorageSizingOutcome.Resized,
                120,
                30,
                240,
                60,
                100_000,
                10_000,
                3),
            new DispatchCostDTO("calculated", 1000m, 200m, 1200m, 10m, 2m, 12m));

        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        DispatchResultsDTO? roundTripped = JsonSerializer.Deserialize<DispatchResultsDTO>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        roundTripped.Should().BeEquivalentTo(result);
    roundTripped!.SchemaVersion.Should().Be(5);
    json.Should().Contain("\"baseDemandMw\"");
    json.Should().Contain("\"additiveComponentsByNameMw\"");
    json.Should().Contain("\"totalDemandMw\"");
        json.Should().Contain("\"nameplateCapacityMw\"");
        json.Should().Contain("\"energyCapacityMwh\"");
        json.Should().Contain("\"powerCapacityMw\"");
        json.Should().Contain("\"deliveredGenerationByTechnologyMw\"");
        json.Should().Contain("\"stateOfChargeByTechnologyMwh\"");
        json.Should().Contain("\"peakUnservedPowerMw\"");
        json.Should().Contain("\"annualisedGenerationCostAud\"");
        json.Should().Contain("\"annualisedStorageCostAud\"");
        json.Should().Contain("\"totalAnnualisedCostAud\"");
        json.Should().Contain("\"generationSlcoeAudPerMwh\"");
        json.Should().Contain("\"storageSlcoeAudPerMwh\"");
        json.Should().Contain("\"slcoeAudPerMwh\"");
        json.Should().Contain("\"sha256\"");
        json.Should().Contain("\"weatherBasis\"");
        json.Should().Contain("\"kind\":\"typicalMeteorologicalYear\"");
        json.Should().Contain("\"targetUsePercentageOfDemand\"");
        json.Should().Contain("\"achievedUsePercentageOfDemand\"");
        json.Should().Contain("\"withinTarget\"");
        json.Should().Contain("\"outcome\":\"resized\"");
        json.Should().Contain("\"peakUnservedIntervalIndex\"");
    }

    [Fact]
    public void InputArtifact_IdentifiesTheExactParsedBytes()
    {
        byte[] contents = Encoding.UTF8.GetBytes("overwritable input");
        string path = Path.Combine("inputs", "demand-data.json");

        DispatchInputArtifactDTO artifact = ScenarioRunner.CreateArtifact(
            path,
            2,
            contents);

        artifact.Should().Be(new DispatchInputArtifactDTO(
            "demand-data.json",
            2,
            "25010854efed1ed4a47708a74f5c201dc04616acc95d7b3381e641ca0483ccaf"));
    }

    [Fact]
    public void Export_ReportsDeliveredGenerationSeparatelyFromCurtailment()
    {
        var start = new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));
        FlowSeries baseDemand = Flow(start, 100, 100);
        FlowSeries additiveDemand = Flow(start, 20, 10);
        FlowSeries totalDemand = Flow(start, 120, 110);
        FlowSeries zero = Flow(start, 0, 0);
        var outcome = new DispatchOutcome(
            "NSW1",
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = Flow(start, 140, 50),
                [GenerationTechnology.Gas] = Flow(start, 0, 60),
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = Flow(start, 20, 0),
                [GenerationTechnology.Gas] = zero,
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = Flow(start, 120, 50),
                [GenerationTechnology.Gas] = Flow(start, 0, 60),
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = zero,
                [GenerationTechnology.Gas] = zero,
            },
            totalDemand,
            zero,
            zero,
            zero,
            zero,
            zero,
            stateOfChargeByTechnology: new Dictionary<StorageTechnology, StockSeries>
            {
                [StorageTechnology.Battery] = new StockSeries(
                    start,
                    TimeSpan.FromHours(1),
                    AnnualValues(start, 0, 8.7)),
            });
        GeneratingFleet[] fleets =
        [
            new(GenerationTechnology.Coal, Power.FromMegawatts(140)),
            new(GenerationTechnology.Gas, Power.FromMegawatts(60)),
        ];
        var demandData = new OperationalDemandData("NSW1", baseDemand, ["demand.zip"]);
        var scenario = new DomainScenario(
            new ScenarioId("nsw1-baseline-dispatch"),
            "NSW1 baseline dispatch",
            start,
            start.AddYears(1),
            [new ScenarioRegion(
                "NSW1",
                [
                    new ScenarioGeneratingFleet(
                        GenerationTechnology.Coal,
                        Power.FromMegawatts(140),
                        CreateCostParameters(),
                        CreateTechnologyProfile()),
                    new ScenarioGeneratingFleet(
                        GenerationTechnology.Gas,
                        Power.FromMegawatts(60),
                        CreateCostParameters(),
                        CreateTechnologyProfile()),
                ],
                [new ScenarioStorageFleet(
                    StorageTechnology.Battery,
                    Energy.FromMegawattHours(120),
                    Power.FromMegawatts(30),
                    new StorageCostParameters(
                        PowerCapacityCost.FromAudPerMwCapacity(0),
                        EnergyCapacityCost.FromAudPerMwhCapacity(0),
                        AnnualPowerCapacityCost.FromAudPerMwYear(0)),
                    new StorageTechnologyProfile(15u, 0.87))])],
            new CostBasis(2026, 0.07m));
        var powerSystem = new PowerSystem(
            new PowerSystemId("nsw1-baseline-dispatch-system"),
            scenario.Id,
            [new Region(
                "NSW1",
                fleets,
                baseDemand,
                [new DemandComponent("Data centres", additiveDemand)],
                storageFleets:
                [
                    new StorageFleet(
                        StorageTechnology.Battery,
                        Energy.FromMegawattHours(120),
                        Power.FromMegawatts(30),
                        new StorageTechnologyProfile(15u, 0.87)),
                ])]);

        var installedCapacity = new RegionalBatterySizing(
            "NSW1",
            Energy.FromMegawattHours(120),
            Power.FromMegawatts(30),
            wasChanged: false);
        var sizingResult = new StorageSizingRunResult(
            powerSystem,
            [new RegionalSizingResult(
                outcome,
                installedCapacity,
                meetsTarget: true,
                StorageSizingStatus.TargetMet,
                "The installed Battery meets the reliability target.")],
            [new InstalledBatteryAssessment(
                outcome,
                installedCapacity,
                meetsTarget: true,
                "The installed Battery meets the reliability target.")],
            dispatchPassCount: 1,
            StorageSizingStatus.TargetMet,
            "The installed Battery meets the reliability target.");

        DispatchResultsDTO result = DispatchResultsExport.Create(new DispatchExportRequest(
            demandData,
            new DispatchInputArtifactDTO("demand.json", 2, new string('a', 64)),
            new DispatchInputArtifactDTO("weather.json", 5, new string('b', 64)),
            new WeatherBasisDTO(
                WeatherBasisKind.TypicalMeteorologicalYear,
                "sydney.epw",
                "Sydney (WMO 947680)",
                "Typical meteorological year from sydney.epw."),
            scenario,
            sizingResult,
            new StorageSizingOptions(
                Power.FromMegawatts(10_000),
                Energy.FromMegawattHours(100_000)),
            "NEM reliability standard",
            PowerSystemCostCalculator.Calculate(scenario, powerSystem, [outcome])));

        result.SchemaVersion.Should().Be(5);
        result.DataSeries.DeliveredGenerationByTechnologyMw["Coal"].Take(2)
            .Should().Equal(120, 50);
        result.DataSeries.DeliveredGenerationByTechnologyMw["Gas"].Take(2)
            .Should().Equal(0, 60);
        result.DataSeries.Demand.BaseDemandMw!.Take(2).Should().Equal(100, 100);
        result.DataSeries.Demand.AdditiveComponentsByNameMw["Data centres"].Take(2)
            .Should().Equal(20, 10);
        result.DataSeries.Demand.TotalDemandMw.Take(2).Should().Equal(120, 110);
        result.PowerSystem.StorageFleets.Should().ContainSingle().Which
            .Should().Be(new DispatchStorageFleetDTO("Battery", 120, 30));
        result.DataSeries.StateOfChargeByTechnologyMwh["Battery"].Take(2)
            .Should().Equal(0, 8.7);
        result.Metrics.Should().Be(new DispatchMetricsDTO(
            230,
            230,
            20,
            0,
            0,
            0,
            8760.0 / 8760,
            0,
            new IntervalPointersDTO(null, 0, 0)));
        result.Reliability.Should().Be(new ReliabilityBasisDTO(
            0.002,
            0,
            true,
            "NEM reliability standard"));
        result.StorageSizing.Should().Be(new StorageSizingOutcomeDTO(
            StorageSizingOutcome.NotRequired,
            120,
            30,
            120,
            30,
            100_000,
            10_000,
            1));
        result.DataSources.WeatherBasis.Kind.Should().Be(WeatherBasisKind.TypicalMeteorologicalYear);
        result.Cost.AnnualisedGenerationCostAud.Should().Be(0);
        result.Cost.AnnualisedStorageCostAud.Should().Be(0);
        result.Cost.SlcoeAudPerMwh.Should().Be(0);
    }

    private static FlowSeries Flow(DateTimeOffset start, params double[] initialMegawatts)
    {
        return new FlowSeries(start, TimeSpan.FromHours(1), AnnualValues(start, initialMegawatts));
    }

    private static double[] AnnualValues(DateTimeOffset start, params double[] initialValues)
    {
        int hours = (int)(start.AddYears(1) - start).TotalHours;
        var values = new double[hours];
        initialValues.CopyTo(values, 0);
        return values;
    }

    private static GenerationCostParameters CreateCostParameters() => new(
        PowerCapacityCost.FromAudPerMwCapacity(0),
        AnnualPowerCapacityCost.FromAudPerMwYear(0),
        GenerationEnergyCost.FromAudPerMwhGenerated(0),
        FuelPrice.FromAudPerGjThermal(0));

    private static GenerationTechnologyProfile CreateTechnologyProfile() => new(
        HeatRate.FromGigajoulesPerMegawattHour(0),
        technicalLifeYears: 30u);
}