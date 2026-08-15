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

    /// <summary>
    /// Parsed artifacts, kept so navigating between pages does not re-read them. The dispatch
    /// results are megabytes of interval series, and deserializing them dominates a page change:
    /// returning to the comparison page cost about 1.7 seconds of parsing against 14 milliseconds
    /// of transfer, because the browser's own cache serves the bytes but not the objects.
    ///
    /// Published artifacts are immutable — a rerun writes new files and the page is reloaded — so a
    /// cached instance cannot go stale within a session. Nothing mutates a loaded artifact: the
    /// pages copy with <c>with</c> expressions rather than assigning into one.
    /// </summary>
    private readonly Dictionary<string, object> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Fetches already in flight, keyed the same way as <see cref="_cache"/>. Without this, two
    /// concurrent requests for a key the cache has not populated yet — the sweep manifest loop
    /// firing before the first request lands, a fast repeat navigation — would each see a cache
    /// miss and issue their own HTTP request for bytes the other is already fetching. Held only for
    /// the request's lifetime; nothing here survives to the next fetch.
    /// </summary>
    private readonly Dictionary<string, object> _inFlight = new(StringComparer.Ordinal);

    /// <summary>
    /// Cached paths in the order they were last read, oldest first. Kept beside the cache so a long
    /// session moving through many sweep points cannot grow without bound.
    /// </summary>
    private readonly List<string> _cacheOrder = [];

    /// <summary>
    /// How many parsed artifacts to keep. A dispatch result holds several 8,760-element series, so
    /// this is tens of megabytes at worst; it is sized to cover a system result, its regions, and a
    /// couple of sweep points at once, rather than a whole sweep.
    /// </summary>
    private const int MaximumCachedArtifacts = 6;

    public async Task<ArtifactLoadResult<T>> LoadAsync<T>(
        string path,
        CancellationToken cancellationToken = default)
        where T : class => await LoadCoreAsync<T>(path, validateSchema: true, cancellationToken);

    public async Task<ArtifactLoadResult<T>> LoadUnversionedAsync<T>(
        string path,
        CancellationToken cancellationToken = default)
        where T : class => await LoadCoreAsync<T>(path, validateSchema: false, cancellationToken);

    /// <summary>
    /// Loads a regional detail belonging to a system result already in hand, refusing one that
    /// carries a different run id. Regional details are megabytes each and the system artifact is
    /// larger still, so the system is passed in rather than fetched again every time a reader
    /// switches region.
    /// </summary>
    public async Task<ArtifactLoadResult<RegionDispatchResultsDTO>> LoadRegionForAsync(
        SystemDispatchResultsDTO system,
        string regionPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(system);

        ArtifactLoadResult<RegionDispatchResultsDTO> regionResult =
            await LoadAsync<RegionDispatchResultsDTO>(regionPath, cancellationToken);
        if (!regionResult.IsSuccess)
        {
            return regionResult;
        }

        return string.Equals(system.RunId, regionResult.Value!.RunId, StringComparison.Ordinal)
            ? regionResult
            : new(ArtifactLoadState.InvalidData("System and regional artifact run IDs do not match."), null);
    }

    private async Task<ArtifactLoadResult<T>> LoadCoreAsync<T>(
        string path,
        bool validateSchema,
        CancellationToken cancellationToken)
        where T : class
    {
        // The requested type is part of the identity: the same path read as two different artifact
        // types would otherwise hand back the wrong one. So is the validation mode — an artifact
        // first read unversioned is cached unchecked, and a later checked read of the same path
        // would otherwise be handed that instance as though it had passed.
        string key = $"{typeof(T).FullName}|{(validateSchema ? "checked" : "unchecked")}|{path}";
        if (TryTakeCached(key, out T? cached))
        {
            return new(new(ArtifactLoadStatus.Success, null), cached);
        }

        if (_inFlight.TryGetValue(key, out object? pending) && pending is Task<ArtifactLoadResult<T>> pendingFetch)
        {
            return await pendingFetch;
        }

        Task<ArtifactLoadResult<T>> fetch = FetchAsync<T>(key, path, validateSchema, cancellationToken);
        _inFlight[key] = fetch;
        try
        {
            return await fetch;
        }
        finally
        {
            // A failed fetch is removed too, so the next caller retries rather than replaying a
            // fault to every request that arrived while this one was in flight.
            _inFlight.Remove(key);
        }
    }

    private async Task<ArtifactLoadResult<T>> FetchAsync<T>(
        string key,
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

            if (validateSchema && DispatchArtifactValidator.Validate(artifact) is { } validationMessage)
            {
                return new(ArtifactLoadState.InvalidData(validationMessage), null);
            }

            // Only artifacts that passed every check are kept, so a cache hit is always a result
            // the caller could have got from a fresh read.
            Cache(key, artifact);
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

    private bool TryTakeCached<T>(string key, out T? artifact)
        where T : class
    {
        if (_cache.TryGetValue(key, out object? stored) && stored is T typed)
        {
            _cacheOrder.Remove(key);
            _cacheOrder.Add(key);
            artifact = typed;
            return true;
        }

        artifact = null;
        return false;
    }

    private void Cache(string key, object artifact)
    {
        _cache[key] = artifact;
        _cacheOrder.Remove(key);
        _cacheOrder.Add(key);
        while (_cacheOrder.Count > MaximumCachedArtifacts)
        {
            _cache.Remove(_cacheOrder[0]);
            _cacheOrder.RemoveAt(0);
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
            [typeof(SystemDispatchResultsDTO)] = (artifact => ((SystemDispatchResultsDTO)artifact).SchemaVersion, new HashSet<int> { ArtifactSchemaVersions.SystemDispatchResults }),
            [typeof(SystemDispatchOverviewDTO)] = (artifact => ((SystemDispatchOverviewDTO)artifact).SchemaVersion, new HashSet<int> { ArtifactSchemaVersions.SystemDispatchOverview }),
            [typeof(RegionDispatchResultsDTO)] = (artifact => ((RegionDispatchResultsDTO)artifact).SchemaVersion, new HashSet<int> { ArtifactSchemaVersions.RegionDispatchResults }),
            [typeof(RegionDispatchOverviewDTO)] = (artifact => ((RegionDispatchOverviewDTO)artifact).SchemaVersion, new HashSet<int> { ArtifactSchemaVersions.RegionDispatchOverview }),
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