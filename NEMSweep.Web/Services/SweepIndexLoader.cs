using NEMSweep.Contracts;

namespace NEMSweep.Web.Services;

public sealed class SweepIndexLoader(ArtifactLoader artifactLoader)
{
    public async Task<ArtifactLoadResult<SweepIndexDTO>> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArtifactLoadResult<SweepIndexDTO> result = await artifactLoader.LoadAsync<SweepIndexDTO>(
            path,
            cancellationToken);
        if (!result.IsSuccess)
        {
            return result;
        }

        string? validationMessage = SweepIndexValidator.Validate(result.Value);
        return validationMessage is null
            ? result
            : new(ArtifactLoadState.InvalidData(validationMessage), null);
    }
}

public static class SweepIndexValidator
{
    /// <summary>
    /// Returns null when the index is usable, or the reason it is not. The schema version is
    /// checked by <see cref="ArtifactLoader"/> against <see cref="ArtifactSchemaVersions"/>
    /// before this runs, so it is not re-checked here.
    /// </summary>
    public static string? Validate(SweepIndexDTO? index)
    {
        if (index is null)
        {
            return "Sweep index is missing.";
        }

        if (string.IsNullOrWhiteSpace(index.SweepId))
        {
            return "Sweep index sweep id is missing.";
        }

        if (string.IsNullOrWhiteSpace(index.Name))
        {
            return "Sweep index name is missing.";
        }

        if (index.Axis is null)
        {
            return "Sweep index axis is missing.";
        }

        if (string.IsNullOrWhiteSpace(index.Axis.Label))
        {
            return "Sweep index axis label is missing.";
        }

        if (string.IsNullOrWhiteSpace(index.Axis.Unit))
        {
            return "Sweep index axis unit is missing.";
        }

        if (index.Scope is not null && !IsUsableScope(index.Scope))
        {
            return "Sweep index scope is invalid.";
        }

        return ValidateProvenance(index.Provenance) ?? ValidatePoints(index.Points);
    }

    /// <summary>
    /// A scope is optional — a sweep whose points span different periods has none — but when it is
    /// present every part of it must be usable, because the site states scope wherever it states a
    /// number.
    /// </summary>
    private static bool IsUsableScope(SweepScopeDTO scope) =>
        scope.RegionIds is { Length: > 0 }
        && !scope.RegionIds.Any(string.IsNullOrWhiteSpace)
        && scope.PeriodEnd > scope.PeriodStart
        && scope.Resolution > TimeSpan.Zero
        && scope.WeatherBasis is not null
        && IsUsableWeatherSite(scope.WeatherBasis.Solar)
        && IsUsableWeatherSite(scope.WeatherBasis.Wind)
        && !string.IsNullOrWhiteSpace(scope.WeatherBasis.Description);

    private static bool IsUsableWeatherSite(WeatherSiteDTO? site) =>
        site is not null
        && !string.IsNullOrWhiteSpace(site.SourceFile)
        && !string.IsNullOrWhiteSpace(site.LocationName);

    private static string? ValidateProvenance(SweepProvenanceDTO? provenance)
    {
        if (provenance is null)
        {
            return "Sweep index provenance is missing.";
        }

        if (string.IsNullOrWhiteSpace(provenance.GitCommitSha))
        {
            return "Sweep index provenance git commit SHA is missing.";
        }

        if (string.IsNullOrWhiteSpace(provenance.ResolvedDefinitionSha256))
        {
            return "Sweep index provenance resolved definition SHA-256 is missing.";
        }

        if (provenance.InputFiles is null)
        {
            return "Sweep index provenance input files are missing.";
        }

        if (provenance.InputFiles.Any(input => input is null
            || string.IsNullOrWhiteSpace(input.Path)
            || string.IsNullOrWhiteSpace(input.Purpose)
            || string.IsNullOrWhiteSpace(input.Sha256)))
        {
            return "Sweep index provenance input files are invalid.";
        }

        if (provenance.SchemaVersions is null)
        {
            return "Sweep index provenance schema versions are missing.";
        }

        return provenance.SchemaVersions.Any(version => string.IsNullOrWhiteSpace(version.Key)
            || version.Value < 1)
            ? "Sweep index provenance schema versions are invalid."
            : null;
    }

    private static string? ValidatePoints(SweepIndexPointDTO[]? points)
    {
        if (points is null)
        {
            return "Sweep index points are missing.";
        }

        if (points.Length == 0)
        {
            return "Sweep index contains no points.";
        }

        var pointIds = new HashSet<string>(StringComparer.Ordinal);
        var pointLabels = new HashSet<string>(StringComparer.Ordinal);
        foreach (SweepIndexPointDTO? point in points)
        {
            if (point is null)
            {
                return "Sweep index contains a missing point.";
            }

            if (string.IsNullOrWhiteSpace(point.PointId))
            {
                return "Sweep index point id is missing.";
            }

            if (!pointIds.Add(point.PointId))
            {
                return $"Sweep index point id '{point.PointId}' is duplicated.";
            }

            if (string.IsNullOrWhiteSpace(point.Label))
            {
                return $"Sweep index point '{point.PointId}' label is missing.";
            }

            if (!pointLabels.Add(point.Label))
            {
                return $"Sweep index point label '{point.Label}' is duplicated.";
            }

            if (string.IsNullOrWhiteSpace(point.ConfigPath))
            {
                return $"Sweep index point '{point.PointId}' config path is missing.";
            }

            string? statusMessage = ValidatePointStatus(point);
            if (statusMessage is not null)
            {
                return statusMessage;
            }
        }

        return null;
    }

    private static string? ValidatePointStatus(SweepIndexPointDTO point)
    {
        // A string outside the closed set fails when the index is deserialized, but the enum
        // converter also accepts bare numbers, so an undefined value can still arrive here.
        if (!Enum.IsDefined(point.Status))
        {
            return $"Sweep index point '{point.PointId}' has an unsupported status.";
        }

        return point.Status switch
        {
            SweepPointStatus.Succeeded when string.IsNullOrWhiteSpace(point.DetailPath) =>
                $"Succeeded sweep point '{point.PointId}' detail path is missing.",
            SweepPointStatus.Succeeded when point.Scalars is null =>
                $"Succeeded sweep point '{point.PointId}' scalars are missing.",
            SweepPointStatus.Succeeded when point.Reliability is null =>
                $"Succeeded sweep point '{point.PointId}' reliability basis is missing.",
            SweepPointStatus.Succeeded when point.StorageSizing is null =>
                $"Succeeded sweep point '{point.PointId}' storage sizing outcome is missing.",
            SweepPointStatus.Succeeded when point.IntervalPointers is null =>
                $"Succeeded sweep point '{point.PointId}' interval pointers are missing.",
            SweepPointStatus.Succeeded when point.Failure is not null =>
                $"Succeeded sweep point '{point.PointId}' cannot include a failure.",
            SweepPointStatus.Failed when point.DetailPath is not null
                || point.Scalars is not null
                || point.Reliability is not null
                || point.StorageSizing is not null
                || point.IntervalPointers is not null =>
                $"Failed sweep point '{point.PointId}' cannot include detail or results.",
            SweepPointStatus.Failed when point.Failure is null
                || string.IsNullOrWhiteSpace(point.Failure.Code)
                || string.IsNullOrWhiteSpace(point.Failure.Message) =>
                $"Failed sweep point '{point.PointId}' failure is missing.",
            _ => null,
        };
    }
}
