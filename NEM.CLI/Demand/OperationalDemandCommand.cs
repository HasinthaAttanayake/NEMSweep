using NEM.CLI.Application;
using NEM.CLI.Configuration;
using NEM.Contracts;

namespace NEM.CLI.Demand;

internal static class OperationalDemandCommand
{
    public static int Run(CliContext context, string outputPath)
    {
        CliSettings settings = context.LoadSettings();
        string inputBundleRoot = context.Paths.ResolveConfiguredPath(settings.InputBundleRoot);
        InputBundle inputBundle = InputBundle.Load(inputBundleRoot);
        IReadOnlyDictionary<string, OperationalDemandData> demandByRegion = OperationalDemandParser.Read(
            inputBundle.DemandArchivePaths,
            inputBundle.Manifest.Regions,
            inputBundle.Manifest.Period.Start,
            inputBundle.Manifest.Period.End);
        string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath))
            ?? context.Paths.ResolveConfiguredPath(settings.OutputRoot);
        Directory.CreateDirectory(outputDirectory);

        foreach (OperationalDemandData demandData in demandByRegion.Values)
        {
            string regionOutputPath = Path.Combine(
                outputDirectory,
                $"demand-{demandData.Region.ToLowerInvariant()}.json");
            ModelInputOutputDTO result = OperationalDemandExport.Create(demandData);
            OperationalDemandExport.WriteJson(result, regionOutputPath);
            context.Output.WriteLine(
                $"Loaded {demandData.Demand.Length} half-hour operational-demand intervals "
                + $"for {demandData.Region} from {demandData.SourceArchives.Count} archives.");
            context.Output.WriteLine(
                $"Period: {demandData.Demand.Start:o} to "
                + $"{result.Scenario.PeriodEnd:o} (end exclusive).");
            if (demandData.ClampedIntervals > 0)
            {
                context.Output.WriteLine(
                    $"Clamped {demandData.ClampedIntervals} negative operational-demand "
                    + $"interval{(demandData.ClampedIntervals == 1 ? "" : "s")} to 0 MW for "
                    + $"{demandData.Region}.");
            }

            context.Output.WriteLine($"Wrote demand data to: {Path.GetFullPath(regionOutputPath)}");
        }

        return 0;
    }
}