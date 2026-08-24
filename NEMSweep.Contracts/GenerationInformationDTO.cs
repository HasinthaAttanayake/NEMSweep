namespace NEMSweep.Contracts;

/// <summary>
/// AEMO's Generation Information survey, carried through unchanged from the "Generator
/// Information" worksheet of the published workbook. One row per generating unit; the model reads
/// this to place existing and committed plant, not to describe anything it dispatched itself.
/// </summary>
/// <param name="SchemaVersion">Schema version of this artifact; see <see cref="ArtifactSchemaVersions.GenerationInformation"/>.</param>
/// <param name="SourceFile">Name of the workbook file the rows were read from.</param>
/// <param name="GeneratedAt">When this artifact was generated, in UTC. It is stamped fresh on every import, so it differs between two imports of identical source data.</param>
/// <param name="Rows">One entry per generating unit in the workbook, in the order the worksheet lists them.</param>
public sealed record GenerationInformationDTO(
    int SchemaVersion,
    string SourceFile,
    DateTimeOffset GeneratedAt,
    GenerationInformationRow[] Rows);

/// <summary>
/// One row of AEMO's Generation Information survey, mapped column-for-column from the "Generator
/// Information" worksheet. A row describes a generating unit, not a whole power station; a station
/// with several units appears as several rows sharing <paramref name="AemoSurveyId"/> and
/// <paramref name="SiteName"/>. Nullable fields are exactly the workbook columns AEMO leaves blank
/// for some units (for example a non-thermal unit has no <paramref name="GasTurbineFuelType"/>).
/// </summary>
/// <param name="AemoSurveyId">AEMO's identifier for the survey response, shared by every unit at the site.</param>
/// <param name="SiteName">Name of the power station or facility.</param>
/// <param name="AemoKciId">AEMO's key connection identifier for the site, when the site has one.</param>
/// <param name="SiteOwner">Registered owner of the site.</param>
/// <param name="Custodian">Custodian responsible for operating the site, when different from the owner.</param>
/// <param name="Region">NEM region the site sits in (for example <c>NSW1</c>).</param>
/// <param name="MaxSiteCapacityAcMw">Site's total AC nameplate capacity, in MW, across all its units.</param>
/// <param name="GenInfoUnitId">AEMO's identifier for this unit; unique within the workbook.</param>
/// <param name="UnitName">Name of this unit, when the site names its units individually.</param>
/// <param name="TechnologyType">Broad technology category (for example <c>Wind</c>, <c>Battery Storage</c>).</param>
/// <param name="TechnologyDetail">Finer-grained technology description within <paramref name="TechnologyType"/>.</param>
/// <param name="GasTurbineFuelType">Fuel burned, for a gas turbine unit; null for every other technology.</param>
/// <param name="Duid">Dispatchable Unit Identifier used in AEMO market systems, when the unit has one.</param>
/// <param name="DispatchType">How the unit is dispatched (for example <c>Scheduled</c>, <c>Semi-Scheduled</c>).</param>
/// <param name="UnitCount">Number of physical units this row aggregates, when the row represents more than one.</param>
/// <param name="UnitCapacityDcMw">
/// This unit's DC nameplate capacity, in MW. Populated for DC-rated technologies such as solar.
/// </param>
/// <param name="UnitCapacityAcMw">This unit's AC nameplate capacity, in MW.</param>
/// <param name="AggregateNameplateCapacityDcMw">
/// Aggregate DC nameplate capacity across the units this row represents, in MW.
/// </param>
/// <param name="AggregateNameplateCapacityAcMw">
/// Aggregate AC nameplate capacity across the units this row represents, in MW.
/// </param>
/// <param name="AggregateNameplateStorageCapacityMwh">
/// Aggregate storage energy capacity across the units this row represents, in MWh.
/// </param>
/// <param name="CommitmentStatus">
/// Where the unit sits in AEMO's commitment lifecycle (for example <c>Committed</c>,
/// <c>Anticipated</c>, <c>Existing</c>).
/// </param>
/// <param name="FullCommercialUseDate">Date the unit reached full commercial operation, if it has.</param>
/// <param name="ExpectedClosureYear">Calendar year the unit is expected to close, if AEMO has one on record.</param>
/// <param name="ClosureDate">Date the unit actually closed, if it has.</param>
/// <param name="SurveyLastRequestedDate">Date AEMO last requested a survey update from the respondent for this unit.</param>
/// <param name="SurveyLatestUpdateDate">Date the respondent last updated this unit's survey entry.</param>
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
