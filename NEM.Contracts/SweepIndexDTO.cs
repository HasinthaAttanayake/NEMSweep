using System.Text.Json.Serialization;

namespace NEM.Contracts;

public sealed record SweepIndexDTO(
    int SchemaVersion,
    string SweepId,
    string Name,
    SweepAxisDTO Axis,
    SweepScopeDTO? Scope,
    SweepProvenanceDTO Provenance,
    SweepIndexPointDTO[] Points);

public sealed record SweepAxisDTO(
    string Label,
    string Unit);

/// <summary>
/// What the sweep's results describe: which regions, over which period, at which resolution, and
/// against which weather. Null when the sweep's points do not share one period and resolution —
/// a heterogeneous sweep has no single scope, and stating one would be false.
/// </summary>
public sealed record SweepScopeDTO(
    string[] RegionIds,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    TimeSpan Resolution,
    WeatherBasisDTO WeatherBasis);

public sealed record SweepProvenanceDTO(
    string GitCommitSha,
    bool WorkingTreeDirty,
    string ResolvedDefinitionSha256,
    SweepInputFileDTO[] InputFiles,
    Dictionary<string, int> SchemaVersions);

public sealed record SweepInputFileDTO(
    string Path,
    string Purpose,
    string Sha256);

public sealed record SweepIndexPointDTO(
    string PointId,
    string Label,
    double AxisValue,
    SweepPointStatus Status,
    string? DetailPath,
    string ConfigPath,
    SweepPointScalarResultsDTO? Scalars,
    ReliabilityBasisDTO? Reliability,
    StorageSizingOutcomeDTO? StorageSizing,
    IntervalPointersDTO? IntervalPointers,
    SweepPointFailureDTO? Failure,
    SweepPointRegionScalarsDTO[]? RegionScalars = null,
    SweepPointRegionDetailDTO[]? RegionDetails = null);

public sealed record SweepPointRegionScalarsDTO(
    string RegionId,
    SweepPointScalarResultsDTO Scalars);

public sealed record SweepPointRegionDetailDTO(
    string RegionId,
    string DetailPath);

/// <summary>Closed set of sweep point outcomes, so both sides fail at the boundary.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SweepPointStatus>))]
public enum SweepPointStatus
{
    [JsonStringEnumMemberName("succeeded")]
    Succeeded,

    [JsonStringEnumMemberName("failed")]
    Failed,
}

/// <summary>
/// Why a point did not produce results. <see cref="Stage"/> and <see cref="Code"/> are for
/// grouping and branching; <see cref="Message"/> is for reading.
/// </summary>
public sealed record SweepPointFailureDTO(
    SweepFailureStage Stage,
    string Code,
    string Message);

/// <summary>Stage of the run a sweep point failed in.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SweepFailureStage>))]
public enum SweepFailureStage
{
    /// <summary>The point's configuration or an input artifact could not be read or validated.</summary>
    [JsonStringEnumMemberName("input")]
    Input,

    /// <summary>Dispatch could not be simulated.</summary>
    [JsonStringEnumMemberName("dispatch")]
    Dispatch,

    /// <summary>The storage sizing loop did not reach the reliability target.</summary>
    [JsonStringEnumMemberName("sizing")]
    Sizing,

    /// <summary>System costs could not be calculated.</summary>
    [JsonStringEnumMemberName("costing")]
    Costing,

    /// <summary>The point ran but its artifacts could not be written.</summary>
    [JsonStringEnumMemberName("export")]
    Export,

    /// <summary>The failure could not be attributed to a stage.</summary>
    [JsonStringEnumMemberName("unknown")]
    Unknown,
}

/// <summary>
/// Per-point figures carried by the index so an all-runs table costs one fetch. Every field here
/// is a copy of a figure in the point's own detail artifact; the display label and unit for each
/// come from <see cref="SweepScalarCatalog"/>.
/// </summary>
public sealed record SweepPointScalarResultsDTO(
    // Money stays decimal deliberately: these are currency values, not measurements.
    decimal SlcoeAudPerMwh,
    decimal GenerationSlcoeAudPerMwh,
    decimal StorageSlcoeAudPerMwh,
    double DemandMwh,
    double EnergyServedMwh,
    double DeliveredGenerationMwh,
    double? AchievedRenewableShareGridScale,
    double? AchievedRenewableShareNative,
    double StoragePowerMw,
    double StorageEnergyMwh,
    double UnservedEnergyMwh,
    double UnservedEnergyPercentageOfDemand,
    int UnservedHours,
    double HoursServedFraction,
    double PeakUnservedPowerMw,
    double CurtailedEnergyMwh);

public sealed record RegularSeriesDTO(
    int SchemaVersion,
    DateTimeOffset Start,
    TimeSpan Resolution,
    double[] ValuesMw);
