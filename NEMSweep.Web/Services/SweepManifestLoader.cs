using NEMSweep.Contracts;

namespace NEMSweep.Web.Services;

/// <summary>
/// Which sweeps exist, read from the manifest the sweep run emits. This replaced a hand-maintained
/// site-side list: a sweep now appears on the site as soon as it is published, and disappears when
/// it is removed, without a second place to keep in step.
/// </summary>
public sealed class SweepManifestLoader(ArtifactLoader artifactLoader)
{
    public const string ManifestPath = "data/sweeps/index.json";

    public async Task<ArtifactLoadResult<SweepManifestDTO>> LoadAsync(
        string path = ManifestPath,
        CancellationToken cancellationToken = default)
    {
        ArtifactLoadResult<SweepManifestDTO> result = await artifactLoader.LoadAsync<SweepManifestDTO>(
            path,
            cancellationToken);
        if (!result.IsSuccess)
        {
            return result;
        }

        string? validationMessage = SweepManifestValidator.Validate(result.Value!);
        return validationMessage is null
            ? result
            : new(ArtifactLoadState.InvalidData(validationMessage), null);
    }
}

public static class SweepManifestValidator
{
    public static string? Validate(SweepManifestDTO manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.Sweeps is null)
        {
            return "Sweep manifest entries are missing.";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SweepManifestEntryDTO? entry in manifest.Sweeps)
        {
            if (entry is null
                || string.IsNullOrWhiteSpace(entry.SweepId)
                || string.IsNullOrWhiteSpace(entry.Name)
                || string.IsNullOrWhiteSpace(entry.IndexPath))
            {
                return "A sweep manifest entry is incomplete.";
            }

            if (!seen.Add(entry.SweepId))
            {
                return $"Sweep manifest entry '{entry.SweepId}' is duplicated.";
            }
        }

        return null;
    }
}

public static class SweepPaths
{
    private const string SweepRoot = "data/sweeps";

    /// <summary>Resolves a manifest entry's index path, which is relative to the manifest.</summary>
    public static string IndexPath(SweepManifestEntryDTO entry) => $"{SweepRoot}/{entry.IndexPath}";

    public static string IndexPath(string sweepId) =>
        $"{SweepRoot}/{Uri.EscapeDataString(sweepId)}/index.json";

    public static string DetailPath(string sweepId, string detailPath) =>
        $"{SweepRoot}/{Uri.EscapeDataString(sweepId)}/{detailPath}";

    public static string PageRoute(string sweepId) => $"/sweeps/{Uri.EscapeDataString(sweepId)}";

    public static string RunRoute(string sweepId, string pointId, string? regionId = null) =>
        string.IsNullOrWhiteSpace(regionId)
            ? $"/runs/{Uri.EscapeDataString(sweepId)}/{Uri.EscapeDataString(pointId)}"
            : $"/runs/{Uri.EscapeDataString(sweepId)}/{Uri.EscapeDataString(pointId)}?region={Uri.EscapeDataString(regionId)}";
}
