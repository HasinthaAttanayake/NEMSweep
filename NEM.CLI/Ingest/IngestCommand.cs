using NEM.CLI.Application;
using NEM.CLI.Configuration;
using NEM.CLI.Demand;
using NEM.CLI.Generation;
using NEM.CLI.Infrastructure;

namespace NEM.CLI.Ingest;

internal static class IngestCommand
{
    public static int Run(CliContext context, string? bundlePath = null)
    {
        ValidatedInputs validated = ValidateInputsCommand.Validate(context, bundlePath);
        CliSettings settings = context.LoadSettings();
        string outputDirectory = context.Paths.ResolveConfiguredPath(settings.OutputRoot);
        Directory.CreateDirectory(outputDirectory);

        foreach (OperationalDemandData demand in validated.DemandByRegion.Values)
        {
            string path = Path.Combine(outputDirectory, $"demand-{demand.Region.ToLowerInvariant()}.json");
            OperationalDemandExport.WriteJson(OperationalDemandExport.Create(demand), path);
            context.Output.WriteLine($"Wrote demand {demand.Region}: {Path.GetFullPath(path)}");
        }

        foreach ((string region, NEM.Contracts.WeatherDataDTO weather) in validated.WeatherByRegion)
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