namespace NEM.Contracts;

/// <summary>
/// Current schema version of every published artifact, and of every input artifact the model
/// reads. Emitters stamp these values and clients check against them, so the supported-version
/// table has one definition rather than one per side.
/// </summary>
public static class ArtifactSchemaVersions
{
    /// <summary>Operational demand (<c>demand-data.json</c>).</summary>
    public const int OperationalDemand = 2;

    /// <summary>Weather resources (<c>weather-data.json</c>).</summary>
    public const int Weather = 6;

    /// <summary>Generation information (<c>generation-information.json</c>).</summary>
    public const int GenerationInformation = 1;

    /// <summary>Dispatch results (<c>results.json</c> and each sweep point detail).</summary>
    public const int DispatchResults = 12;

    /// <summary>Whole-system dispatch results artifact.</summary>
    public const int SystemDispatchResults = 11;

    /// <summary>Compact whole-system dispatch overview (<c>results-overview.json</c>).</summary>
    public const int SystemDispatchOverview = 2;

    /// <summary>Per-region dispatch results detail artifact.</summary>
    public const int RegionDispatchResults = 8;

    /// <summary>Compact per-region dispatch overview (<c>results-{regionId}-overview.json</c>).</summary>
    public const int RegionDispatchOverview = 1;

    /// <summary>Sweep index (<c>sweeps/{sweepId}/index.json</c>).</summary>
    public const int SweepIndex = 9;

    /// <summary>Sweep manifest (<c>sweeps/index.json</c>).</summary>
    public const int SweepManifest = 1;

    /// <summary>Sweep definition, the CLI-side input a sweep run is resolved from.</summary>
    public const int SweepDefinition = 1;

    /// <summary>Externalised regular series (<c>sweeps/{sweepId}/series/*.json</c>).</summary>
    public const int RegularSeries = 1;
}
