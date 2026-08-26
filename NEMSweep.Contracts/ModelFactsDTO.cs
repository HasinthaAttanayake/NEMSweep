using System.Text.Json.Serialization;

namespace NEMSweep.Contracts;

// Blocks shared by the dispatch-results and sweep-index artifacts. A target, an outcome and an
// interval index are facts the model establishes; clients read them rather than deriving them.

/// <summary>
/// The reliability target a run was sized against, the reliability it achieved, and the verdict.
/// Percentages are percentages of demand energy, matching
/// <see cref="DispatchMetricsDTO.UnservedEnergyPercentageOfDemand"/>.
/// </summary>
public sealed record ReliabilityBasisDTO(
    double TargetUsePercentageOfDemand,
    double AchievedUsePercentageOfDemand,
    bool WithinTarget,
    string? StandardName);

/// <summary>
/// What the storage sizing loop did. Distinguishes "the installed fleet already met the target"
/// from "the fleet was grown", so a flat storage series can be labelled as a result.
/// </summary>
/// <remarks>
/// Every capacity here is at the scope of the artifact carrying it. On a region artifact these are
/// that region's figures against the limit the loop was given for it; on a whole-system artifact
/// they are summed across the regions, and <see cref="MaximumEnergyMwh"/> and
/// <see cref="MaximumPowerMw"/> are summed with them. The limit the loop actually enforces is a
/// per-region one, so a system artifact that passed it through unsummed reported a total against a
/// ceiling a fifth of its size, and a fleet inside its limit read as one past it.
/// </remarks>
public sealed record StorageSizingOutcomeDTO(
    StorageSizingOutcome Outcome,
    double InitialEnergyMwh,
    double InitialPowerMw,
    double FinalEnergyMwh,
    double FinalPowerMw,
    double MaximumEnergyMwh,
    double MaximumPowerMw,
    int PassesUsed,
    EnergyLimitedEvidenceDTO? EnergyLimitedEvidence = null,
    StorageSizingPassDTO[]? Trajectory = null);

/// <summary>One capacity and reliability result attempted during storage sizing.</summary>
public sealed record StorageSizingPassDTO(
    int Pass,
    double EnergyCapacityMwh,
    double PowerCapacityMw,
    double UnservedEnergyMwh,
    int UnservedHours);

/// <summary>
/// System-wide evidence that storage cannot meet the reliability target because available
/// generation energy is below demand energy over the dispatch period. Energies are expressed in
/// GWh; interval indices identify hours where total available generation power is below total
/// demand power.
/// </summary>
public sealed record EnergyLimitedEvidenceDTO(
    double AvailableEnergyGwh,
    double DemandEnergyGwh,
    double ShortfallEnergyGwh,
    int[] BindingIntervalIndices);

/// <summary>Closed set of storage sizing outcomes an emitted artifact can carry.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StorageSizingOutcome>))]
public enum StorageSizingOutcome
{
    /// <summary>The installed storage fleet met the reliability target unchanged.</summary>
    [JsonStringEnumMemberName("notRequired")]
    NotRequired,

    /// <summary>The sizing loop grew the storage fleet to meet the reliability target.</summary>
    [JsonStringEnumMemberName("resized")]
    Resized,

    /// <summary>
    /// Available generation energy is below demand energy, so additional generation rather than
    /// additional storage is required.
    /// </summary>
    [JsonStringEnumMemberName("energyLimited")]
    EnergyLimited,

    /// <summary>
    /// Every feasible larger Battery power, energy, and combined-growth probe failed to materially
    /// reduce unserved energy before the configured capacity limits were reached.
    /// </summary>
    [JsonStringEnumMemberName("storageNoLongerImprovesReliability")]
    StorageNoLongerImprovesReliability,

    /// <summary>The configured Battery capacity limit was reached before the target was met.</summary>
    [JsonStringEnumMemberName("batteryCapacityLimitReached")]
    BatteryCapacityLimitReached,

    /// <summary>The configured dispatch-pass limit was reached before the target was met.</summary>
    [JsonStringEnumMemberName("passLimitReached")]
    PassLimitReached,
}

/// <summary>
/// Indices of the intervals worth opening a run at. Indices rather than timestamps so they survive
/// a resolution change; convert with <c>periodStart + resolution * index</c>. Null when the
/// corresponding series is flat at zero (or, for state of charge, when there is no storage).
/// </summary>
public sealed record IntervalPointersDTO(
    int? PeakUnservedIntervalIndex,
    int? PeakCurtailmentIntervalIndex,
    int? MinimumStateOfChargeIntervalIndex);

/// <summary>
/// What the weather input represents. A typical meteorological year excludes the tail events that
/// drive storage and reliability results, so a reader needs this stated rather than inferred.
/// </summary>
public sealed record WeatherBasisDTO(
    WeatherBasisKind Kind,
    WeatherSiteDTO Solar,
    WeatherSiteDTO Wind,
    string Description);

/// <summary>Source provenance for one weather role in a dispatch result.</summary>
public sealed record WeatherSiteDTO(
    string SourceFile,
    string LocationName);

/// <summary>Closed set of weather bases a run can be simulated against.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WeatherBasisKind>))]
public enum WeatherBasisKind
{
    /// <summary>
    /// A typical meteorological year: a composite of representative months from several observed
    /// years, applied to the dispatch period by calendar hour.
    /// </summary>
    [JsonStringEnumMemberName("typicalMeteorologicalYear")]
    TypicalMeteorologicalYear,
}
