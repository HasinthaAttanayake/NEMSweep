using NEM.Contracts;
using NEM.Model.Series;
using System.Text.Json;

namespace NEM.CLI;

internal static class OperationalDemandExport
{
    public static ModelInputOutputDTO Create(OperationalDemandData demandData)
    {
        FlowSeries demand = demandData.Demand;
        var demandMegawatts = new double[demand.Length];
        for (int index = 0; index < demand.Length; index++)
        {
            demandMegawatts[index] = demand[index].Megawatts;
        }

        return new ModelInputOutputDTO(
            2,
            new Scenario(
                $"{demandData.Region.ToLowerInvariant()}-operational-demand",
                demandData.Region,
                demand.Start,
                demand.Start.AddTicks(demand.Resolution.Ticks * demand.Length),
                demand.Resolution,
                "single region; no cross-region aggregation; identical overlaps deduplicated"),
            DateTimeOffset.UtcNow,
            new Sources(demandData.SourceArchives.ToArray()),
            new Series(demandMegawatts));
    }

    public static void WriteJson(ModelInputOutputDTO demandData, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(demandData, options));
    }
}