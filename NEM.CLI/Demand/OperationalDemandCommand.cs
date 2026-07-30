using NEM.CLI.Application;
using NEM.Contracts;

namespace NEM.CLI.Demand;

internal static class OperationalDemandCommand
{
    public static int Run(CliContext context, string outputPath)
    {
        var settings = context.LoadSettings().OperationalDemand;
        string archiveDirectory = context.Paths.ResolveConfiguredPath(settings.ArchiveDirectory);
        OperationalDemandData demandData = OperationalDemandParser.ReadFinancialYear(
            archiveDirectory,
            settings.Region,
            settings.PeriodStart);
        ModelInputOutputDTO result = OperationalDemandExport.Create(demandData);
        OperationalDemandExport.WriteJson(result, outputPath);
        context.Output.WriteLine(
            $"Loaded {demandData.Demand.Length} half-hour operational-demand intervals "
            + $"for {demandData.Region} from {demandData.SourceArchives.Count} archives.");
        context.Output.WriteLine(
            $"Period: {demandData.Demand.Start:o} to "
            + $"{result.Scenario.PeriodEnd:o} (end exclusive).");
        context.Output.WriteLine($"Wrote demand data to: {Path.GetFullPath(outputPath)}");
        return 0;
    }
}