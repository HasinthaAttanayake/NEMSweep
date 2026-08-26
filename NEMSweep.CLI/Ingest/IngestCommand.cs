using NEMSweep.CLI.Application;
using NEMSweep.CLI.Configuration;
using NEMSweep.CLI.Demand;
using NEMSweep.CLI.Generation;
using NEMSweep.CLI.Infrastructure;

namespace NEMSweep.CLI.Ingest;

internal static class IngestCommand
{
    public static int Run(CliContext context, string? bundlePath = null)
    {
        ValidatedInputs validated = ValidateInputsCommand.Validate(context, bundlePath);

        // Ingest produces the artifacts scenarios read, so it writes to the data root rather than
        // the output root. Results are the only thing that belongs under the output root.
        string outputDirectory = context.Paths.DataRoot;
        Directory.CreateDirectory(outputDirectory);

        foreach (OperationalDemandData demand in validated.DemandByRegion.Values)
        {
            string path = Path.Combine(outputDirectory, $"demand-{demand.Region.ToLowerInvariant()}.json");
            OperationalDemandExport.WriteJson(OperationalDemandExport.Create(demand), path);
            context.Output.WriteLine($"Wrote demand {demand.Region}: {Path.GetFullPath(path)}");
        }

        foreach ((string region, NEMSweep.Contracts.WeatherDataDTO weather) in validated.WeatherByRegion)
        {
            string path = Path.Combine(outputDirectory, $"weather-{region.ToLowerInvariant()}.json");
            Weather.EpwWeatherExport.WriteJson(weather, path);
            context.Output.WriteLine($"Wrote weather {region}: {Path.GetFullPath(path)}");
        }

        string generationPath = Path.Combine(outputDirectory, "generation-information.json");
        GenerationInformationExport.WriteJson(validated.GenerationInformation, generationPath);
        context.Output.WriteLine($"Wrote generation information: {Path.GetFullPath(generationPath)}");
        context.Output.WriteLine($"Ingested input bundle '{validated.Bundle.Manifest.BundleId}'.");
        return 0;
    }
}