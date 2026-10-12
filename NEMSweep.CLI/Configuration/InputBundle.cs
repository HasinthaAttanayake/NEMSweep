using System.Text.Json;
using NEMSweep.CLI.Infrastructure;
using NEMSweep.Contracts;
using NEMSweep.Model.Series;

namespace NEMSweep.CLI.Configuration;

/// <summary>Which EPW files play which role for one region.</summary>
internal sealed record RegionWeatherSources(string SolarEpwPath, string WindEpwPath);

/// <summary>A validated input bundle: the manifest plus every path discovered from the folder.</summary>
internal sealed record InputBundle(
    InputBundleManifestDTO Manifest,
    IReadOnlyList<string> DemandArchivePaths,
    IReadOnlyDictionary<string, RegionWeatherSources> WeatherByRegion,
    string? GenerationInformationPath,
    IReadOnlyList<string> Warnings)
{
    public const int LegacySchemaVersion = 1;
    public const int SupportedSchemaVersion = 2;

    public IReadOnlyList<string> RegionIds => Manifest.Regions
        .Select(region => region.RegionId)
        .ToArray();

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

        InputBundleManifestDTO manifest = ReadManifest(File.ReadAllBytes(manifestPath));
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
        foreach (InputBundleRegionDTO region in manifest.Regions)
        {
            string regionId = region.RegionId;
            string regionDirectory = Path.Combine(weatherDirectory, region.WeatherDirectory);
            if (!Directory.Exists(regionDirectory))
            {
                throw new FormatException(
                    $"Input bundle is missing weather folder 'weather/{region.WeatherDirectory}' for region '{regionId}'.");
            }

            weatherByRegion.Add(regionId, DiscoverWeatherSources(root, regionId, regionDirectory));
        }

        string? generationInformationPath = DiscoverGenerationInformation(root, manifest.SchemaVersion);
        return new InputBundle(
            manifest,
            demandArchivePaths,
            weatherByRegion,
            generationInformationPath,
            warnings);
    }

    private static InputBundleManifestDTO ReadManifest(byte[] bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Input bundle 'manifest.json' must contain a JSON object.");
            }

            JsonElement schemaProperty = document.RootElement.EnumerateObject()
                .FirstOrDefault(property => property.Name.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase))
                .Value;
            if (schemaProperty.ValueKind != JsonValueKind.Number
                || !schemaProperty.TryGetInt32(out int schemaVersion))
            {
                throw new FormatException(
                    "Input bundle manifest field 'schemaVersion' in 'manifest.json' is required and must be an integer.");
            }

            if (schemaVersion == LegacySchemaVersion)
            {
                LegacyInputBundleManifestDTO legacy = JsonSerializer.Deserialize<LegacyInputBundleManifestDTO>(
                    bytes,
                    JsonFile.ReadOptions)
                    ?? throw new FormatException("Input bundle 'manifest.json' is empty.");
                return new InputBundleManifestDTO(
                    legacy.SchemaVersion,
                    legacy.BundleId,
                    legacy.Name,
                    legacy.Period,
                    (legacy.Regions ?? [])
                        .Select(regionId => new InputBundleRegionDTO(regionId ?? string.Empty, regionId ?? string.Empty))
                        .ToArray());
            }

            if (schemaVersion == SupportedSchemaVersion)
            {
                try
                {
                    return JsonFile.ReadConfig<InputBundleManifestDTO>(bytes)
                        ?? throw new FormatException("Input bundle 'manifest.json' is empty.");
                }
                catch (JsonException exception)
                {
                    throw new FormatException(
                        "Input bundle schema version 2 'manifest.json' is invalid; check the $schema hint and remove unknown or malformed fields.",
                        exception);
                }
            }

            throw new FormatException(
                $"Input bundle manifest schema version {schemaVersion} in 'manifest.json' is not supported; expected 1 or {SupportedSchemaVersion}.");
        }
        catch (JsonException exception)
        {
            throw new FormatException("Input bundle 'manifest.json' is not valid JSON.", exception);
        }
    }

    private static string? DiscoverGenerationInformation(string root, int schemaVersion)
    {
        if (schemaVersion == SupportedSchemaVersion)
        {
            return null;
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

        return generationPaths[0];
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
        if (manifest.SchemaVersion is not LegacySchemaVersion and not SupportedSchemaVersion)
        {
            throw new FormatException(
                $"Input bundle manifest schema version {manifest.SchemaVersion} in 'manifest.json' is not supported; expected 1 or {SupportedSchemaVersion}.");
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

        if (manifest.Period.Start.Offset != manifest.Period.End.Offset)
        {
            throw new FormatException(
                "Input bundle manifest 'period.start' and 'period.end' in 'manifest.json' must carry the same "
                + $"market-time offset; got {manifest.Period.Start.Offset} and {manifest.Period.End.Offset}.");
        }

        if (!MarketTime.IsValidOffset(manifest.Period.Start.Offset))
        {
            throw new FormatException(
                $"Input bundle manifest 'period' offset {manifest.Period.Start.Offset} in 'manifest.json' is not a "
                + "usable market-time offset; it must be within [-12:00, +14:00] at quarter-hour granularity.");
        }

        if (manifest.Regions is null || manifest.Regions.Length == 0)
        {
            throw new FormatException("Input bundle manifest field 'regions' in 'manifest.json' must declare at least one region.");
        }

        var regionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var weatherDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (InputBundleRegionDTO? region in manifest.Regions)
        {
            if (region is null)
            {
                throw new FormatException("Input bundle manifest region in 'manifest.json' must be an object.");
            }

            string? regionId = region.RegionId;
            if (string.IsNullOrWhiteSpace(regionId))
            {
                throw new FormatException("Input bundle manifest regionId in 'manifest.json' must not be blank.");
            }

            if (manifest.SchemaVersion == SupportedSchemaVersion
                && !regionId.Equals(regionId.Trim(), StringComparison.Ordinal))
            {
                throw new FormatException(
                    $"Input bundle manifest regionId '{regionId}' in 'manifest.json' must not have surrounding whitespace; "
                    + "operational-demand parsing trims region IDs.");
            }

            if (manifest.SchemaVersion == LegacySchemaVersion && !NemRegions.IsKnown(regionId))
            {
                throw new FormatException($"Input bundle manifest region '{regionId}' in 'manifest.json' is not a known NEM region.");
            }

            if (!regionIds.Add(regionId))
            {
                throw new FormatException($"Input bundle manifest contains duplicate region '{regionId}' in 'manifest.json'.");
            }

            if (manifest.SchemaVersion == SupportedSchemaVersion)
            {
                try
                {
                    _ = RegionFileNames.For(regionId);
                }
                catch (ArgumentException exception)
                {
                    throw new FormatException(
                        $"Input bundle manifest regionId '{regionId}' in 'manifest.json' cannot be published safely: {exception.Message}",
                        exception);
                }

                string? weatherDirectory = region.WeatherDirectory;
                ValidateWeatherDirectory(weatherDirectory);
                if (!weatherDirectories.Add(weatherDirectory!))
                {
                    throw new FormatException(
                        $"Input bundle manifest contains colliding weatherDirectory '{weatherDirectory}' in 'manifest.json'.");
                }
            }
        }
    }

    private static void ValidateWeatherDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 255
            || value is "." or ".."
            || value.EndsWith(' ') || value.EndsWith('.')
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0
            || value.Any(char.IsControl))
        {
            throw new FormatException(
                $"Input bundle manifest weatherDirectory '{value}' in 'manifest.json' must be one safe directory name.");
        }

        string deviceName = value.Split('.')[0].TrimEnd(' ');
        if (deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)
            || IsNumberedDevice(deviceName, "COM")
            || IsNumberedDevice(deviceName, "LPT"))
        {
            throw new FormatException(
                $"Input bundle manifest weatherDirectory '{value}' in 'manifest.json' is a reserved Windows name.");
        }
    }

    private static bool IsNumberedDevice(string name, string prefix) =>
        name.Length == prefix.Length + 1
        && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && name[^1] is >= '1' and <= '9';

    private sealed record LegacyInputBundleManifestDTO(
        [property: System.Text.Json.Serialization.JsonPropertyName("schemaVersion")]
        int SchemaVersion,
        string BundleId,
        string Name,
        InputBundlePeriodDTO Period,
        string?[]? Regions);
}
