using System.Net;
using System.Text.Json;
using NEM.Contracts;

namespace NEM.Web.Services;

public enum ArtifactLoadStatus
{
    Loading,
    Success,
    NotFound,
    InvalidData,
    Failed,
    Empty,
}

public sealed record ArtifactLoadState(ArtifactLoadStatus Status, string? Message)
{
    public static ArtifactLoadState Loading() => new(ArtifactLoadStatus.Loading, null);

    public static ArtifactLoadState NotFound(string message) => new(ArtifactLoadStatus.NotFound, message);

    public static ArtifactLoadState Empty(string message) => new(ArtifactLoadStatus.Empty, message);

    public static ArtifactLoadState InvalidData(string message) => new(ArtifactLoadStatus.InvalidData, message);

    public static ArtifactLoadState Failed(string message) => new(ArtifactLoadStatus.Failed, message);
}

public sealed record ArtifactLoadResult<T>(ArtifactLoadState State, T? Value)
{
    public bool IsSuccess => State.Status == ArtifactLoadStatus.Success;
}

public sealed class ArtifactLoader(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<ArtifactLoadResult<T>> LoadAsync<T>(
        string path,
        CancellationToken cancellationToken = default)
        where T : class => await LoadCoreAsync<T>(path, validateSchema: true, cancellationToken);

    public async Task<ArtifactLoadResult<T>> LoadUnversionedAsync<T>(
        string path,
        CancellationToken cancellationToken = default)
        where T : class => await LoadCoreAsync<T>(path, validateSchema: false, cancellationToken);

    private async Task<ArtifactLoadResult<T>> LoadCoreAsync<T>(
        string path,
        bool validateSchema,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(path, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new(new(ArtifactLoadStatus.NotFound, "Artifact was not found."), null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new(new(ArtifactLoadStatus.Failed, "Artifact request failed."), null);
            }

            await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
            T? artifact = await JsonSerializer.DeserializeAsync<T>(content, JsonOptions, cancellationToken);
            if (artifact is null)
            {
                return new(ArtifactLoadState.InvalidData("Artifact is missing."), null);
            }

            if (validateSchema
                && !ArtifactSchemaRegistry.IsSupported(artifact, out int schemaVersion, out string expectedVersions))
            {
                return new(
                    ArtifactLoadState.InvalidData(
                        $"Artifact schema {schemaVersion} is not supported; expected schema {expectedVersions}."),
                    null);
            }

            return new(new(ArtifactLoadStatus.Success, null), artifact);
        }
        catch (JsonException)
        {
            return new(ArtifactLoadState.InvalidData("Artifact is not valid JSON data."), null);
        }
        catch (HttpRequestException)
        {
            return new(ArtifactLoadState.Failed("Artifact request failed."), null);
        }
    }
}

public static class ArtifactSchemaRegistry
{
    private static readonly IReadOnlyDictionary<Type, (Func<object, int> GetVersion, IReadOnlySet<int> SupportedVersions)> Definitions =
        new Dictionary<Type, (Func<object, int>, IReadOnlySet<int>)>
        {
            [typeof(ModelInputOutputDTO)] = (artifact => ((ModelInputOutputDTO)artifact).SchemaVersion, new HashSet<int> { ArtifactSchemaVersions.OperationalDemand }),
            [typeof(DispatchResultsDTO)] = (artifact => ((DispatchResultsDTO)artifact).SchemaVersion, new HashSet<int> { ArtifactSchemaVersions.DispatchResults }),
            [typeof(GenerationInformationDTO)] = (artifact => ((GenerationInformationDTO)artifact).SchemaVersion, new HashSet<int> { ArtifactSchemaVersions.GenerationInformation }),
            [typeof(RegularSeriesDTO)] = (artifact => ((RegularSeriesDTO)artifact).SchemaVersion, new HashSet<int> { ArtifactSchemaVersions.RegularSeries }),
            [typeof(SweepIndexDTO)] = (artifact => ((SweepIndexDTO)artifact).SchemaVersion, new HashSet<int> { ArtifactSchemaVersions.SweepIndex }),
            [typeof(SweepManifestDTO)] = (artifact => ((SweepManifestDTO)artifact).SchemaVersion, new HashSet<int> { ArtifactSchemaVersions.SweepManifest }),
            [typeof(WeatherDataDTO)] = (artifact => ((WeatherDataDTO)artifact).SchemaVersion, new HashSet<int> { ArtifactSchemaVersions.Weather }),
        };

    public static bool IsSupported(object artifact, out int schemaVersion, out string expectedVersions)
    {
        if (!Definitions.TryGetValue(artifact.GetType(), out var definition))
        {
            throw new InvalidOperationException($"No schema support has been declared for {artifact.GetType().Name}.");
        }

        schemaVersion = definition.GetVersion(artifact);
        expectedVersions = string.Join(" or ", definition.SupportedVersions.Order());
        return definition.SupportedVersions.Contains(schemaVersion);
    }
}