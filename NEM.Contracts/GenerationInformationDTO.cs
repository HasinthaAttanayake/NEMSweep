namespace NEM.Contracts;

public sealed record GenerationInformationDTO(
    int SchemaVersion,
    string SourceFile,
    DateTimeOffset GeneratedAt,
    GenerationInformationRow[] Rows);

public sealed record GenerationInformationRow(
    string AemoSurveyId,
    string SiteName,
    string? AemoKciId,
    string? SiteOwner,
    string? Custodian,
    string Region,
    double? MaxSiteCapacityAcMw,
    string GenInfoUnitId,
    string? UnitName,
    string TechnologyType,
    string? TechnologyDetail,
    string? GasTurbineFuelType,
    string? Duid,
    string? DispatchType,
    double? UnitCount,
    double? UnitCapacityDcMw,
    double? UnitCapacityAcMw,
    double? AggregateNameplateCapacityDcMw,
    double? AggregateNameplateCapacityAcMw,
    double? AggregateNameplateStorageCapacityMwh,
    string CommitmentStatus,
    DateOnly? FullCommercialUseDate,
    int? ExpectedClosureYear,
    DateOnly? ClosureDate,
    DateOnly? SurveyLastRequestedDate,
    DateOnly? SurveyLatestUpdateDate);