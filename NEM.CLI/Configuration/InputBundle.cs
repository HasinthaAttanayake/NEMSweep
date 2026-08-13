using System.Text.Json;
using NEM.CLI.Infrastructure;
using NEM.Contracts;

namespace NEM.CLI.Configuration;

/// <summary>Which EPW files play which role for one region.</summary>
internal sealed record RegionWeatherSources(string SolarEpwPath, string WindEpwPath);

/// <summary>A validated input bundle: the manifest plus every path discovered from the folder.</summary>
internal sealed record InputBundle(
    InputBundleManifestDTO Manifest,
    IReadOnlyList<string> DemandArchivePaths,
    IReadOnlyDictionary<string, RegionWeatherSources> WeatherByRegion,
    string GenerationInformationPath,
    IReadOnlyList<string> Warnings)
{
    public const int SupportedSchemaVersion = 1;

    public static InputBundle Load(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        string root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            throw new FormatException($"Input bundle root '{root}' does not exist.");
        }

        string manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FormatException("Input bundle is missing 'manifest.json'.");
        }

        InputBundleManifestDTO manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<InputBundleManifestDTO>(
                File.ReadAllBytes(manifestPath),
                JsonFile.ReadOptions)
                ?? throw new FormatException("Input bundle 'manifest.json' is empty.");
        }
        catch (JsonException exception)
        {
            throw new FormatException("Input bundle 'manifest.json' is not valid JSON.", exception);
        }
        ValidateManifest(manifest);

        var warnings = new List<string>();
        if (!Path.GetFileName(root).Equals(manifest.BundleId, StringComparison.Ordinal))
        {
            warnings.Add($"Input bundle folder '{Path.GetFileName(root)}' differs from bundleId '{manifest.BundleId}'.");
        }

        string demandDirectory = Path.Combine(root, "demand", "operational-demand-hh");
        if (!Directory.Exists(demandDirectory))
        {
            throw new FormatException("Input bundle is missing 'demand/operational-demand-hh'.");
        }

        string[] demandArchivePaths = Directory.EnumerateFiles(demandDirectory, "*.zip", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(demandDirectory, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part.Equals("reference", StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (demandArchivePaths.Length == 0)
        {
            throw new FormatException("Input bundle 'demand/operational-demand-hh' contains no .zip archives.");
        }

        string weatherDirectory = Path.Combine(root, "weather");
        var weatherByRegion = new Dictionary<string, RegionWeatherSources>(StringComparer.OrdinalIgnoreCase);
        foreach (string regionId in manifest.Regions)
        {
            string regionDirectory = Path.Combine(weatherDirectory, regionId);
            if (!Directory.Exists(regionDirectory))
            {
                throw new FormatException($"Input bundle is missing weather folder 'weather/{regionId}'.");
            }

            weatherByRegion.Add(regionId, DiscoverWeatherSources(root, regionId, regionDirectory));
        }

        string generationDirectory = Path.Combine(root, "generation", "generation-information");
        if (!Directory.Exists(generationDirectory))
        {
            throw new FormatException("Input bundle is missing 'generation/generation-information'.");
        }

        string[] generationPaths = Directory.EnumerateFiles(generationDirectory, "*.xlsx", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (generationPaths.Length != 1)
        {
            throw new FormatException(
                $"Input bundle 'generation/generation-information' must contain exactly one .xlsx file; found {generationPaths.Length}.");
        }

        return new InputBundle(manifest, demandArchivePaths, weatherByRegion, generationPaths[0], warnings);
    }

    private static RegionWeatherSources DiscoverWeatherSources(string root, string regionId, string regionDirectory)
    {
        string relativeDirectory = Path.GetRelativePath(root, regionDirectory).Replace('\\', '/');
        FileSystemInfo[] entries = new DirectoryInfo(regionDirectory).GetFileSystemInfos();
        FileInfo[] rootEpwFiles = entries.OfType<FileInfo>()
            .Where(file => file.Extension.Equals(".epw", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (entries.All(entry => entry is FileInfo) && rootEpwFiles.Length == 1 && entries.Length == 1)
        {
            return new RegionWeatherSources(rootEpwFiles[0].FullName, rootEpwFiles[0].FullName);
        }

        DirectoryInfo[] directories = entries.OfType<DirectoryInfo>().ToArray();
        bool hasExactRoleDirectories = entries.Length == 2
            && directories.Length == 2
            && directories.Any(directory => directory.Name.Equals("solar", StringComparison.OrdinalIgnoreCase))
            && directories.Any(directory => directory.Name.Equals("wind", StringComparison.OrdinalIgnoreCase));
        if (hasExactRoleDirectories)
        {
            string solarPath = DiscoverSingleRoleFile(root, regionId, directories.Single(directory =>
                directory.Name.Equals("solar", StringComparison.OrdinalIgnoreCase)));
            string windPath = DiscoverSingleRoleFile(root, regionId, directories.Single(directory =>
                directory.Name.Equals("wind", StringComparison.OrdinalIgnoreCase)));
            return new RegionWeatherSources(solarPath, windPath);
        }

        throw new FormatException(
            $"Input bundle weather region '{regionId}' has invalid shape at '{relativeDirectory}'; expected one .epw file or solar/ and wind/ folders each containing one .epw file.");
    }

    private static string DiscoverSingleRoleFile(string root, string regionId, DirectoryInfo roleDirectory)
    {
        FileSystemInfo[] entries = roleDirectory.GetFileSystemInfos();
        if (entries.Length == 1
            && entries[0] is FileInfo file
            && file.Extension.Equals(".epw", StringComparison.OrdinalIgnoreCase))
        {
            return file.FullName;
        }

        string relativeDirectory = Path.GetRelativePath(root, roleDirectory.FullName).Replace('\\', '/');
        throw new FormatException(
            $"Input bundle weather region '{regionId}' role folder '{relativeDirectory}' must contain exactly one .epw file.");
    }

    private static void ValidateManifest(InputBundleManifestDTO manifest)
    {
        if (manifest.SchemaVersion != SupportedSchemaVersion)
        {
            throw new FormatException(
                $"Input bundle manifest schema version {manifest.SchemaVersion} in 'manifest.json' is not supported; expected {SupportedSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(manifest.BundleId))
        {
            throw new FormatException("Input bundle manifest field 'bundleId' in 'manifest.json' must not be blank.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new FormatException("Input bundle manifest field 'name' in 'manifest.json' must not be blank.");
        }

        if (manifest.Period is null)
        {
            throw new FormatException("Input bundle manifest field 'period' in 'manifest.json' is required.");
        }

        if (manifest.Period.End <= manifest.Period.Start)
        {
            throw new FormatException("Input bundle manifest field 'period.end' in 'manifest.json' must be after 'period.start'.");
        }

        if (manifest.Regions is null || manifest.Regions.Length == 0)
        {
            throw new FormatException("Input bundle manifest field 'regions' in 'manifest.json' must declare at least one region.");
        }

        var regionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? regionId in manifest.Regions)
        {
            if (string.IsNullOrWhiteSpace(regionId) || !NemRegions.IsKnown(regionId))
            {
                throw new FormatException($"Input bundle manifest region '{regionId}' in 'manifest.json' is not a known NEM region.");
            }

            if (!regionIds.Add(regionId))
            {
                throw new FormatException($"Input bundle manifest contains duplicate region '{regionId}' in 'manifest.json'.");
            }
        }
    }
}