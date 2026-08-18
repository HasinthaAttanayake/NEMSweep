using NEM.CLI.Application;
using NEM.CLI.Configuration;
using NEM.CLI.Demand;
using NEM.CLI.Generation;
using NEM.CLI.Infrastructure;
using NEM.CLI.Weather;
using NEM.Contracts;
using NEM.Model.Weather;

namespace NEM.CLI.Ingest;

internal sealed record ValidatedInputs(
    InputBundle Bundle,
    IReadOnlyDictionary<string, OperationalDemandData> DemandByRegion,
    IReadOnlyDictionary<string, WeatherDataDTO> WeatherByRegion,
    GenerationInformationDTO GenerationInformation);

internal static class ValidateInputsCommand
{
    public static int Run(CliContext context, string? bundlePath = null)
    {
        ValidatedInputs validated = Validate(context, bundlePath);
        foreach (string warning in validated.Bundle.Warnings)
        {
            context.Output.WriteLine($"Warning: {warning}");
        }

        foreach (OperationalDemandData demand in validated.DemandByRegion.Values)
        {
            context.Output.WriteLine(
                $"Demand {demand.Region}: valid ({demand.Demand.Length} half-hour intervals)."
                + (demand.ClampedIntervals > 0
                    ? $" {demand.ClampedIntervals} negative interval"
                        + $"{(demand.ClampedIntervals == 1 ? "" : "s")} clamped to 0 MW."
                    : string.Empty));
        }

        foreach ((string region, WeatherDataDTO _) in validated.WeatherByRegion)
        {
            context.Output.WriteLine($"Weather {region}: valid.");
        }

        context.Output.WriteLine(
            $"Generation information: valid ({validated.GenerationInformation.Rows.Length} rows).");
        context.Output.WriteLine(
            $"Validated input bundle '{validated.Bundle.Manifest.BundleId}' for "
            + $"{validated.Bundle.Manifest.Regions.Length} regions; no files written.");
        return 0;
    }

    internal static ValidatedInputs Validate(CliContext context, string? bundlePath)
    {
        CliSettings settings = context.LoadSettings();
        string root = context.Paths.ResolveConfiguredPath(
            string.IsNullOrWhiteSpace(bundlePath) ? settings.InputBundleRoot : bundlePath);
        InputBundle bundle = InputBundle.Load(root);
        IReadOnlyDictionary<string, OperationalDemandData> demandByRegion = OperationalDemandParser.Read(
            bundle.DemandArchivePaths,
            bundle.Manifest.Regions,
            bundle.Manifest.Period.Start,
            bundle.Manifest.Period.End);

        var weatherByPath = new Dictionary<string, (EpwFile File, RegionalResourceProfile Series)>(
            StringComparer.OrdinalIgnoreCase);
        var weatherByRegion = new Dictionary<string, WeatherDataDTO>(StringComparer.OrdinalIgnoreCase);
        foreach ((string region, RegionWeatherSources sources) in bundle.WeatherByRegion)
        {
            (EpwFile solarFile, RegionalResourceProfile solarSeries) = ReadWeather(
                sources.SolarEpwPath, weatherByPath);
            (EpwFile windFile, RegionalResourceProfile windSeries) = ReadWeather(
                sources.WindEpwPath, weatherByPath);
            weatherByRegion.Add(region, EpwWeatherExport.Create(
                region,
                solarFile.Header,
                solarSeries,
                Path.GetFileName(sources.SolarEpwPath),
                windFile.Header,
                windSeries,
                Path.GetFileName(sources.WindEpwPath)));
        }

        IReadOnlyList<GenerationInformationRow> generationRows =
            GenerationInformationParser.Read(bundle.GenerationInformationPath);
        GenerationInformationDTO generation = GenerationInformationExport.Create(
            bundle.GenerationInformationPath,
            generationRows);
        return new ValidatedInputs(bundle, demandByRegion, weatherByRegion, generation);
    }

    private static (EpwFile File, RegionalResourceProfile Series) ReadWeather(
        string path,
        IDictionary<string, (EpwFile File, RegionalResourceProfile Series)> cache)
    {
        if (cache.TryGetValue(path, out (EpwFile File, RegionalResourceProfile Series) result))
        {
            return result;
        }

        EpwFile file = EpwParser.ReadValidated(path);
        result = (file, EpwParser.ReadTimeSeries(file));
        cache.Add(path, result);
        return result;
    }
}