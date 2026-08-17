using NEM.Contracts;

namespace NEM.Web.Services;

/// <summary>
/// Discovers which regions a dispatch run covers, and each region's weather-artifact path, from
/// the system dispatch overview. Weather-trace pages read this so they follow however many
/// regions the current baseline covers instead of a single hardcoded file.
/// </summary>
public static class RegionArtifactResolver
{
    /// <summary>
    /// Resolves whichever regions have a usable weather artifact reference, rather than failing
    /// the whole page over one bad region. Weather traces are an input a reader wants to check
    /// even while a baseline is mid-regeneration, when it is normal for one region's dispatch
    /// results to be missing, stale, or briefly out of step with the rest.
    /// </summary>
    public static bool TryResolveWeatherPaths(
        string[]? regionIds,
        Dictionary<string, DispatchSourcesDTO>? dataSourcesByRegion,
        out IReadOnlyList<string> orderedRegionIds,
        out IReadOnlyDictionary<string, string> weatherPathsByRegion,
        out string? validationMessage)
    {
        orderedRegionIds = [];
        weatherPathsByRegion = new Dictionary<string, string>();
        validationMessage = null;

        if (regionIds is null || regionIds.Length == 0)
        {
            validationMessage = "System dispatch results do not define any regions.";
            return false;
        }

        if (dataSourcesByRegion is null)
        {
            validationMessage = "System dispatch results do not define regional data sources.";
            return false;
        }

        var pathsByRegion = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? regionId in regionIds)
        {
            if (string.IsNullOrWhiteSpace(regionId))
            {
                continue;
            }

            // pathsByRegion is case-insensitive, so a case-variant duplicate ("nsw1" and "NSW1")
            // would otherwise silently overwrite the earlier entry rather than being caught, and
            // the region selector would render one fewer option than the run actually has. This is
            // a malformed topology declaration rather than a missing artifact, so it fails outright
            // instead of being skipped like the cases below.
            if (pathsByRegion.ContainsKey(regionId.ToUpperInvariant()))
            {
                validationMessage =
                    $"System dispatch results declare region '{regionId}' more than once.";
                return false;
            }

            if (!dataSourcesByRegion.TryGetValue(regionId, out DispatchSourcesDTO? sources)
                || sources.WeatherInput is null
                || !TryGetDataArtifactPath(sources.WeatherInput.FileName, out string weatherPath))
            {
                // Missing or unreadable for this one region only - resolve the rest rather than
                // failing the whole page.
                continue;
            }

            pathsByRegion[regionId.ToUpperInvariant()] = weatherPath;
        }

        if (pathsByRegion.Count == 0)
        {
            validationMessage =
                "None of the system's regions have a valid weather artifact reference.";
            return false;
        }

        orderedRegionIds = pathsByRegion.Keys.Order(StringComparer.Ordinal).ToArray();
        weatherPathsByRegion = pathsByRegion;
        return true;
    }

    /// <summary>
    /// Normalises an artifact-relative file name (as published in provenance, e.g.
    /// "weather-nsw1.json") to the "data/..." path the front end fetches from.
    /// </summary>
    private static bool TryGetDataArtifactPath(string fileName, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string normalized = fileName.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["data/".Length..];
        }

        if (normalized.Length == 0
            || normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            return false;
        }

        path = $"data/{normalized}";
        return true;
    }
}
