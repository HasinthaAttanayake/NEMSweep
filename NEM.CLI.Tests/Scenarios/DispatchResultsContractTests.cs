using FluentAssertions;
using NEM.CLI.Demand;
using NEM.CLI.Scenarios;
using NEM.Contracts;
using NEM.Model.Economics;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.Units;
using System.Text;
using System.Text.Json;
using DomainScenario = NEM.Model.Scenarios.Scenario;

namespace NEM.CLI.Tests.Scenarios;

public sealed class DispatchResultsContractTests
{
    [Fact]
    public void V3_RoundTripsWithVersionAndExplicitUnits()
    {
        var start = new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));
        var result = new DispatchResultsDTO(
            3,
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
                ["demand.zip"]),
            new DispatchPowerSystemDTO(
                "nsw1-baseline-dispatch-system",
                [new DispatchFleetDTO("Solar", 100)],
                [new DispatchStorageFleetDTO("Battery", 120, 30)]),
            new DispatchSeriesDTO(
                [80, 90],
                new Dictionary<string, double[]> { ["Solar"] = [80, 85] },
                [20, 15],
                [0, 5],
                [10, 0],
                [0, 5],
                new Dictionary<string, double[]> { ["Battery"] = [0, 8.7] }),
            new DispatchMetricsDTO(170, 165, 35, 5, 5.0 / 170 * 100, 1, 0.5, 5),
            new DispatchCostDTO("calculated", 1000m, 200m, 1200m, 10m, 2m, 12m));

        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        DispatchResultsDTO? roundTripped = JsonSerializer.Deserialize<DispatchResultsDTO>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        roundTripped.Should().BeEquivalentTo(result);
        roundTripped!.SchemaVersion.Should().Be(3);
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
        FlowSeries demand = Flow(start, 100, 100);
        FlowSeries zero = Flow(start, 0, 0);
        var outcome = new DispatchOutcome(
            "NSW1",
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = Flow(start, 120, 50),
                [GenerationTechnology.Gas] = Flow(start, 0, 40),
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = Flow(start, 20, 0),
                [GenerationTechnology.Gas] = zero,
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = Flow(start, 100, 50),
                [GenerationTechnology.Gas] = Flow(start, 0, 40),
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = zero,
                [GenerationTechnology.Gas] = zero,
            },
            demand,
            Flow(start, 0, 10),
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
            new(GenerationTechnology.Coal, Power.FromMegawatts(120)),
            new(GenerationTechnology.Gas, Power.FromMegawatts(40)),
        ];
        var demandData = new OperationalDemandData("NSW1", demand, ["demand.zip"]);
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
                        Power.FromMegawatts(120),
                        CreateCostParameters(),
                        CreateTechnologyProfile()),
                    new ScenarioGeneratingFleet(
                        GenerationTechnology.Gas,
                        Power.FromMegawatts(40),
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
                demand,
                storageFleets:
                [
                    new StorageFleet(
                        StorageTechnology.Battery,
                        Energy.FromMegawattHours(120),
                        Power.FromMegawatts(30),
                        new StorageTechnologyProfile(15u, 0.87)),
                ])]);

        DispatchResultsDTO result = DispatchResultsExport.Create(
            demandData,
            new DispatchInputArtifactDTO("demand.json", 2, new string('a', 64)),
            new DispatchInputArtifactDTO("weather.json", 5, new string('b', 64)),
            scenario,
            powerSystem,
            outcome,
            PowerSystemCostCalculator.Calculate(scenario, powerSystem, [outcome]));

        result.DataSeries.DeliveredGenerationByTechnologyMw["Coal"].Take(2)
            .Should().Equal(100, 50);
        result.DataSeries.DeliveredGenerationByTechnologyMw["Gas"].Take(2)
            .Should().Equal(0, 40);
        result.PowerSystem.StorageFleets.Should().ContainSingle().Which
            .Should().Be(new DispatchStorageFleetDTO("Battery", 120, 30));
        result.DataSeries.StateOfChargeByTechnologyMwh["Battery"].Take(2)
            .Should().Equal(0, 8.7);
        result.Metrics.Should().Be(new DispatchMetricsDTO(
            200,
            190,
            20,
            10,
            5,
            1,
            8759.0 / 8760,
            10));
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