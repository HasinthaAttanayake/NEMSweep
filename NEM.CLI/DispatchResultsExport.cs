using NEM.Contracts;
using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Simulation;
using System.Text.Json;

namespace NEM.CLI;

internal static class DispatchResultsExport
{
    public static DispatchResultsDTO Create(
        OperationalDemandData demandData,
        string weatherSourceFile,
        string scenarioDescription,
        IReadOnlyList<GeneratingFleet> fleets,
        DispatchOutcome outcome)
    {
        var generationByTechnologyMw = new Dictionary<string, double[]>();
        foreach ((TechnologyKey technology, FlowSeries availableGeneration) in
                 outcome.PerFleetGeneration.OrderBy(entry => entry.Key))
        {
            FlowSeries deliveredGeneration = availableGeneration.Subtract(
                outcome.PerFleetCurtailment[technology]);
            generationByTechnologyMw.Add(technology.ToString(), ValuesOf(deliveredGeneration));
        }

        ReliabilityMetrics reliability = ReliabilityMetrics.FromOutcome(outcome);
        double deliveredGenerationMwh = generationByTechnologyMw.Values
            .Sum(values => values.Sum() * outcome.Demand.Resolution.TotalHours);

        return new DispatchResultsDTO(
            1,
            new DispatchScenarioDTO(
                $"{demandData.Region.ToLowerInvariant()}-baseline-dispatch",
                demandData.Region,
                outcome.Demand.Start,
                outcome.Demand.Start.AddTicks(outcome.Demand.Resolution.Ticks * outcome.Demand.Length),
                outcome.Demand.Resolution),
            DateTimeOffset.UtcNow,
            new DispatchSourcesDTO(
                demandData.SourceArchives.ToArray(),
                Path.GetFileName(weatherSourceFile)),
            new DispatchAssumptionsDTO(
                scenarioDescription,
                fleets.Select(fleet => new DispatchFleetDTO(
                    fleet.TechnologyKey.ToString(),
                    fleet.NameplateCapacity.Megawatts)).ToArray()),
            new DispatchSeriesDTO(
                ValuesOf(outcome.Demand),
                generationByTechnologyMw,
                ValuesOf(outcome.Curtailment),
                ValuesOf(outcome.Unserved)),
            new DispatchMetricsDTO(
                outcome.Demand.Integrate().MegawattHours,
                deliveredGenerationMwh,
                outcome.Curtailment.Integrate().MegawattHours,
                reliability.UnservedEnergy.MegawattHours,
                reliability.UnservedHours,
                reliability.HoursServedFraction),
            new DispatchCostDTO(
                "pending NEM-018",
                null,
                null));
    }

    public static void WriteJson(DispatchResultsDTO result, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }));
    }

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