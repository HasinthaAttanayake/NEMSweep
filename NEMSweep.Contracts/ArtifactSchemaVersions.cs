namespace NEMSweep.Contracts;

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
    public const int DispatchResults = 16;

    /// <summary>Whole-system dispatch results artifact.</summary>
    public const int SystemDispatchResults = 18;

    /// <summary>Compact whole-system dispatch overview (<c>results-overview.json</c>).</summary>
    public const int SystemDispatchOverview = 7;

    /// <summary>Per-region dispatch results detail artifact.</summary>
    public const int RegionDispatchResults = 13;

    /// <summary>Compact per-region dispatch overview (<c>results-{regionId}-overview.json</c>).</summary>
    public const int RegionDispatchOverview = 6;

    /// <summary>Sweep index (<c>sweeps/{sweepId}/index.json</c>).</summary>
    public const int SweepIndex = 14;

    /// <summary>Sweep manifest (<c>sweeps/index.json</c>).</summary>
    public const int SweepManifest = 1;

    /// <summary>Sweep summary (<c>sweeps/{sweepId}/summary.json</c>).</summary>
    public const int SweepSummary = 1;

    /// <summary>Sweep definition, the CLI-side input a sweep run is resolved from.</summary>
    public const int SweepDefinition = 2;

    /// <summary>Scenario configuration, the CLI-side input a scenario run is resolved from.</summary>
    public const int ScenarioConfig = 7;

    /// <summary>Externalised regular series (<c>sweeps/{sweepId}/series/*.json</c>).</summary>
    public const int RegularSeries = 1;
}
