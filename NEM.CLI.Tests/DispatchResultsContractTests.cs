using FluentAssertions;
using NEM.Contracts;
using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.Units;
using System.Text.Json;

namespace NEM.CLI.Tests;

public sealed class DispatchResultsContractTests
{
    [Fact]
    public void V1_RoundTripsWithVersionAndExplicitUnits()
    {
        var start = new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));
        var result = new DispatchResultsDTO(
            1,
            new DispatchScenarioDTO(
                "nsw1-baseline-dispatch",
                "NSW1",
                start,
                start.AddHours(2),
                TimeSpan.FromHours(1)),
            DateTimeOffset.UtcNow,
            new DispatchSourcesDTO(["demand.zip"], "weather.epw"),
            new DispatchAssumptionsDTO(
                "Test fleet",
                [new DispatchFleetDTO("Solar", 100)]),
            new DispatchSeriesDTO(
                [80, 90],
                new Dictionary<string, double[]> { ["Solar"] = [80, 85] },
                [20, 15],
                [0, 5]),
            new DispatchMetricsDTO(170, 165, 35, 5, 1, 0.5),
            new DispatchCostDTO("pending NEM-018", null, null));

        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        DispatchResultsDTO? roundTripped = JsonSerializer.Deserialize<DispatchResultsDTO>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        roundTripped.Should().BeEquivalentTo(result);
        roundTripped!.SchemaVersion.Should().Be(1);
        json.Should().Contain("\"nameplateCapacityMw\"");
        json.Should().Contain("\"generationByTechnologyMw\"");
        json.Should().Contain("\"generationCostAud\"");
        json.Should().Contain("\"generationSlcoeAudPerMwh\"");
    }

    [Fact]
    public void Export_ReportsDeliveredGenerationSeparatelyFromCurtailment()
    {
        var start = new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));
        FlowSeries demand = Flow(start, 100, 100);
        FlowSeries zero = Flow(start, 0, 0);
        var outcome = new DispatchOutcome(
            "NSW1",
            new Dictionary<TechnologyKey, FlowSeries>
            {
                [TechnologyKey.Solar] = Flow(start, 120, 50),
                [TechnologyKey.Gas] = Flow(start, 0, 40),
            },
            new Dictionary<TechnologyKey, FlowSeries>
            {
                [TechnologyKey.Solar] = Flow(start, 20, 0),
                [TechnologyKey.Gas] = zero,
            },
            demand,
            Flow(start, 0, 10),
            zero,
            zero,
            zero,
            zero);
        GeneratingFleet[] fleets =
        [
            new(TechnologyKey.Solar, Power.FromMegawatts(120)),
            new(TechnologyKey.Gas, Power.FromMegawatts(40)),
        ];
        var demandData = new OperationalDemandData("NSW1", demand, ["demand.zip"]);

        DispatchResultsDTO result = DispatchResultsExport.Create(
            demandData,
            "weather.epw",
            "Test scenario",
            fleets,
            outcome);

        result.DataSeries.GenerationByTechnologyMw["Solar"].Should().Equal(100, 50);
        result.DataSeries.GenerationByTechnologyMw["Gas"].Should().Equal(0, 40);
        result.Metrics.Should().Be(new DispatchMetricsDTO(200, 190, 20, 10, 1, 0.5));
        result.Cost.GenerationCostAud.Should().BeNull();
        result.Cost.GenerationSlcoeAudPerMwh.Should().BeNull();
    }

    private static FlowSeries Flow(DateTimeOffset start, params double[] megawatts) =>
        new(start, TimeSpan.FromHours(1), megawatts);
}