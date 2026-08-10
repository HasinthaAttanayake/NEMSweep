using System.Net.Http.Json;
using System.Text.Json;
using NEM.Contracts;

namespace NEM.Web.Services;

public enum SweepIndexLoadStatus
{
    Loading,
    Ready,
    InvalidData,
    Failed,
}

public sealed record SweepIndexLoadState(
    SweepIndexLoadStatus Status,
    SweepIndexDTO? Index,
    string? Message)
{
    public static SweepIndexLoadState Loading() => new(SweepIndexLoadStatus.Loading, null, null);
}

public sealed class SweepIndexLoader
{
    public const int SupportedSchemaVersion = ArtifactSchemaVersions.SweepIndex;

    public async Task<SweepIndexLoadState> LoadAsync(
        HttpClient http,
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            SweepIndexDTO? index = await http.GetFromJsonAsync<SweepIndexDTO>(
                path,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);
            return Validate(index);
        }
        catch (JsonException)
        {
            return new SweepIndexLoadState(
                SweepIndexLoadStatus.InvalidData,
                null,
                "Sweep index is not valid JSON data.");
        }
        catch (HttpRequestException)
        {
            return new SweepIndexLoadState(
                SweepIndexLoadStatus.Failed,
                null,
                "Sweep index request failed.");
        }
    }

    public static SweepIndexLoadState Validate(SweepIndexDTO? index)
    {
        if (index is null)
        {
            return Invalid("Sweep index is missing.");
        }

        if (index.SchemaVersion != SupportedSchemaVersion)
        {
            return Invalid(
                $"Sweep index schema {index.SchemaVersion} is not supported; expected schema {SupportedSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(index.SweepId))
        {
            return Invalid("Sweep index sweep id is missing.");
        }

        if (string.IsNullOrWhiteSpace(index.Name))
        {
            return Invalid("Sweep index name is missing.");
        }

        if (index.Axis is null)
        {
            return Invalid("Sweep index axis is missing.");
        }

        if (string.IsNullOrWhiteSpace(index.Axis.Label))
        {
            return Invalid("Sweep index axis label is missing.");
        }

        if (string.IsNullOrWhiteSpace(index.Axis.Unit))
        {
            return Invalid("Sweep index axis unit is missing.");
        }

        if (index.Scope is not null && !IsValidScope(index.Scope))
        {
            return Invalid("Sweep index scope is invalid.");
        }

        if (index.Provenance is null)
        {
            return Invalid("Sweep index provenance is missing.");
        }

        if (string.IsNullOrWhiteSpace(index.Provenance.GitCommitSha))
        {
            return Invalid("Sweep index provenance git commit SHA is missing.");
        }

        if (string.IsNullOrWhiteSpace(index.Provenance.ResolvedDefinitionSha256))
        {
            return Invalid("Sweep index provenance resolved definition SHA-256 is missing.");
        }

        if (index.Provenance.InputFiles is null)
        {
            return Invalid("Sweep index provenance input files are missing.");
        }

        if (index.Provenance.InputFiles.Any(input => input is null
            || string.IsNullOrWhiteSpace(input.Path)
            || string.IsNullOrWhiteSpace(input.Purpose)
            || string.IsNullOrWhiteSpace(input.Sha256)))
        {
            return Invalid("Sweep index provenance input files are invalid.");
        }

        if (index.Provenance.SchemaVersions is null)
        {
            return Invalid("Sweep index provenance schema versions are missing.");
        }

        if (index.Provenance.SchemaVersions.Any(version => string.IsNullOrWhiteSpace(version.Key)
            || version.Value < 1))
        {
            return Invalid("Sweep index provenance schema versions are invalid.");
        }

        if (index.Points is null)
        {
            return Invalid("Sweep index points are missing.");
        }

        var pointIds = new HashSet<string>(StringComparer.Ordinal);
        var pointLabels = new HashSet<string>(StringComparer.Ordinal);
        foreach (SweepIndexPointDTO? point in index.Points)
        {
            if (point is null)
            {
                return Invalid("Sweep index contains a missing point.");
            }

            if (string.IsNullOrWhiteSpace(point.PointId))
            {
                return Invalid("Sweep index point id is missing.");
            }

            if (!pointIds.Add(point.PointId))
            {
                return Invalid($"Sweep index point id '{point.PointId}' is duplicated.");
            }

            if (string.IsNullOrWhiteSpace(point.Label))
            {
                return Invalid($"Sweep index point '{point.PointId}' label is missing.");
            }

            if (!pointLabels.Add(point.Label))
            {
                return Invalid($"Sweep index point label '{point.Label}' is duplicated.");
            }

            if (string.IsNullOrWhiteSpace(point.ConfigPath))
            {
                return Invalid($"Sweep index point '{point.PointId}' config path is missing.");
            }

            // A string outside the closed set fails when the index is deserialized, but the enum
            // converter also accepts bare numbers, so an undefined value can still arrive here.
            if (!Enum.IsDefined(point.Status))
            {
                return Invalid(
                    $"Sweep index point '{point.PointId}' has an unsupported status.");
            }

            switch (point.Status)
            {
                case SweepPointStatus.Succeeded when string.IsNullOrWhiteSpace(point.DetailPath):
                    return Invalid($"Succeeded sweep point '{point.PointId}' detail path is missing.");
                case SweepPointStatus.Succeeded when point.Scalars is null:
                    return Invalid($"Succeeded sweep point '{point.PointId}' scalars are missing.");
                case SweepPointStatus.Succeeded when point.Reliability is null:
                    return Invalid($"Succeeded sweep point '{point.PointId}' reliability basis is missing.");
                case SweepPointStatus.Succeeded when point.StorageSizing is null:
                    return Invalid($"Succeeded sweep point '{point.PointId}' storage sizing outcome is missing.");
                case SweepPointStatus.Succeeded when point.IntervalPointers is null:
                    return Invalid($"Succeeded sweep point '{point.PointId}' interval pointers are missing.");
                case SweepPointStatus.Succeeded when point.Failure is not null:
                    return Invalid($"Succeeded sweep point '{point.PointId}' cannot include a failure.");
                case SweepPointStatus.Failed when point.DetailPath is not null
                    || point.Scalars is not null
                    || point.Reliability is not null
                    || point.StorageSizing is not null
                    || point.IntervalPointers is not null:
                    return Invalid($"Failed sweep point '{point.PointId}' cannot include detail or results.");
                case SweepPointStatus.Failed when point.Failure is null
                    || string.IsNullOrWhiteSpace(point.Failure.Code)
                    || string.IsNullOrWhiteSpace(point.Failure.Message):
                    return Invalid($"Failed sweep point '{point.PointId}' failure is missing.");
                default:
                    break;
            }
        }

        return new SweepIndexLoadState(SweepIndexLoadStatus.Ready, index, null);
    }

    /// <summary>
    /// A scope is optional — a sweep whose points span different periods has none — but when it is
    /// present every part of it must be usable, because the site states scope wherever it states a
    /// number.
    /// </summary>
    private static bool IsValidScope(SweepScopeDTO scope) =>
        scope.RegionIds is { Length: > 0 }
        && !scope.RegionIds.Any(string.IsNullOrWhiteSpace)
        && scope.PeriodEnd > scope.PeriodStart
        && scope.Resolution > TimeSpan.Zero
        && scope.WeatherBasis is not null
        && !string.IsNullOrWhiteSpace(scope.WeatherBasis.SourceFile)
        && !string.IsNullOrWhiteSpace(scope.WeatherBasis.LocationName)
        && !string.IsNullOrWhiteSpace(scope.WeatherBasis.Description);

    private static SweepIndexLoadState Invalid(string message) =>
        new(SweepIndexLoadStatus.InvalidData, null, message);
}