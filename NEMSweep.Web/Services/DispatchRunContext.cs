using NEMSweep.Contracts;

namespace NEMSweep.Web.Services;

public sealed record DispatchRunContext(
    SweepIndexDTO Sweep,
    SweepIndexPointDTO Point,
    SweepIndexPointDTO? PreviousPoint,
    SweepIndexPointDTO? NextPoint)
{
    public string SweepRootPath => $"data/sweeps/{Sweep.SweepId}";

    public string DetailArtifactPath => $"{SweepRootPath}/{Point.DetailPath}";

    public string ConfigArtifactPath => $"{SweepRootPath}/{Point.ConfigPath}";

    public string ConfigRepositoryUrl =>
        $"https://github.com/HasinthaAttanayake/NEMSweep/blob/{Sweep.Provenance.GitCommitSha}/{HistoricalWebProjectFolder}/wwwroot/{ConfigArtifactPath}";

    /// <summary>
    /// The web project's folder name as it existed at <see cref="SweepProvenanceDTO.GitCommitSha"/>,
    /// read from a data or weather input path rather than assumed, so a link into a sweep run from
    /// before the NEM.Web -&gt; NEMSweep.Web rename still resolves at that historical commit.
    /// </summary>
    private string HistoricalWebProjectFolder =>
        Sweep.Provenance.InputFiles
            .FirstOrDefault(file => file.Purpose is "demand-data" or "weather-data")
            ?.Path.Split('/', 2)[0]
        ?? "NEMSweep.Web";

    public string? ResolveReferencedArtifactPath(string? reference, string? originPath = null)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        Uri origin = new("https://artifact.invalid/");
        Uri detailUri = new(origin, originPath ?? DetailArtifactPath);
        if (!Uri.TryCreate(detailUri, reference, out Uri? referenceUri)
            || !referenceUri.Host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string path = referenceUri.AbsolutePath.TrimStart('/');
        return path.StartsWith($"{SweepRootPath}/", StringComparison.Ordinal)
            ? path
            : null;
    }
}

public static class DispatchRunContextResolver
{
    public static DispatchRunContext? Resolve(
        SweepIndexDTO index,
        string? sweepId,
        string? pointId)
    {
        ArgumentNullException.ThrowIfNull(index);

        if (!string.Equals(index.SweepId, sweepId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(pointId))
        {
            return null;
        }

        int pointIndex = Array.FindIndex(index.Points, point =>
            string.Equals(point.PointId, pointId, StringComparison.Ordinal));
        if (pointIndex < 0 || index.Points[pointIndex].Status != SweepPointStatus.Succeeded
            || string.IsNullOrWhiteSpace(index.Points[pointIndex].DetailPath))
        {
            return null;
        }

        SweepIndexPointDTO? previous = index.Points[..pointIndex]
            .LastOrDefault(IsViewablePoint);
        SweepIndexPointDTO? next = index.Points[(pointIndex + 1)..]
            .FirstOrDefault(IsViewablePoint);
        return new DispatchRunContext(index, index.Points[pointIndex], previous, next);
    }

    private static bool IsViewablePoint(SweepIndexPointDTO point) =>
        point.Status == SweepPointStatus.Succeeded && !string.IsNullOrWhiteSpace(point.DetailPath);
}