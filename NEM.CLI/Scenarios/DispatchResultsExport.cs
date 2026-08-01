using NEM.Contracts;
using NEM.CLI.Demand;
using NEM.CLI.Infrastructure;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Simulation;
using DomainScenario = NEM.Model.Scenarios.Scenario;

namespace NEM.CLI.Scenarios;

internal static class DispatchResultsExport
{
    public static DispatchResultsDTO Create(
        OperationalDemandData demandData,
        DispatchInputArtifactDTO demandInput,
        DispatchInputArtifactDTO weatherInput,
        DomainScenario scenario,
        PowerSystem powerSystem,
        DispatchOutcome outcome)
    {
        var deliveredGenerationByTechnology = new Dictionary<string, FlowSeries>();
        foreach ((GenerationTechnology technology, FlowSeries availableGeneration) in
                 outcome.PerFleetGeneration.OrderBy(entry => entry.Key))
        {
            FlowSeries deliveredGeneration = availableGeneration.Subtract(
                outcome.PerFleetCurtailment[technology]);
            deliveredGenerationByTechnology.Add(technology.ToString(), deliveredGeneration);
        }

        ReliabilityMetrics reliability = ReliabilityMetrics.FromOutcome(outcome);
        double deliveredGenerationMwh = deliveredGenerationByTechnology.Values
            .Sum(series => series.Integrate().MegawattHours);
        Region region = powerSystem.Regions.Single(region => region.RegionId == outcome.RegionId);

        return new DispatchResultsDTO(
            1,
            new DispatchScenarioDTO(
                scenario.Id.Value,
                scenario.Name,
                demandData.Region,
                outcome.Demand.Start,
                outcome.Demand.Start.AddTicks(outcome.Demand.Resolution.Ticks * outcome.Demand.Length),
                outcome.Demand.Resolution),
            DateTimeOffset.UtcNow,
            new DispatchSourcesDTO(
                demandInput,
                weatherInput,
                demandData.SourceArchives.ToArray()),
            new DispatchPowerSystemDTO(
                powerSystem.Id.Value,
                region.GeneratingFleets.Select(fleet => new DispatchFleetDTO(
                    fleet.GenerationTechnology.ToString(),
                    fleet.NameplateCapacity.Megawatts)).ToArray()),
            new DispatchSeriesDTO(
                ValuesOf(outcome.Demand),
                deliveredGenerationByTechnology.ToDictionary(
                    entry => entry.Key,
                    entry => ValuesOf(entry.Value)),
                ValuesOf(outcome.Curtailment),
                ValuesOf(outcome.Unserved)),
            new DispatchMetricsDTO(
                outcome.Demand.Integrate().MegawattHours,
                deliveredGenerationMwh,
                outcome.Curtailment.Integrate().MegawattHours,
                reliability.UnservedEnergy.MegawattHours,
                reliability.UnservedEnergyPercentageOfDemand,
                reliability.UnservedHours,
                reliability.HoursServedFraction),
            new DispatchCostDTO(
                "pending NEM-018",
                null,
                null));
    }

    public static void WriteJson(DispatchResultsDTO result, string path)
        => JsonFile.Write(result, path);

    private static double[] ValuesOf(FlowSeries series)
    {
        var values = new double[series.Length];
        for (int index = 0; index < series.Length; index++)
        {
            values[index] = series[index].Megawatts;
        }

        return values;
    }
}