namespace NEM.Contracts;

public sealed record SweepIndexDTO(
    int SchemaVersion,
    string SweepId,
    string Name,
    SweepAxisDTO Axis,
    SweepProvenanceDTO Provenance,
    SweepIndexPointDTO[] Points);

public sealed record SweepAxisDTO(
    string Label,
    string Unit);

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
    string Status,
    string? DetailPath,
    string ConfigPath,
    SweepPointScalarResultsDTO? Scalars,
    string? Failure);

public sealed record SweepPointScalarResultsDTO(
    decimal SlcoeAudPerMwh,
    decimal GenerationSlcoeAudPerMwh,
    decimal StorageSlcoeAudPerMwh,
    double EnergyServedMwh,
    double? AchievedRenewableShareGridScale,
    double? AchievedRenewableShareNative,
    double StoragePowerMw,
    double StorageEnergyMwh,
    double UnservedEnergyMwh,
    double UnservedEnergyPercentageOfDemand,
    double CurtailedEnergyMwh);

public sealed record RegularSeriesDTO(
    DateTimeOffset Start,
    TimeSpan Resolution,
    double[] ValuesMw);