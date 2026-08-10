using System.Text.Json.Serialization;

namespace NEM.Contracts;

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
public sealed record StorageSizingOutcomeDTO(
    StorageSizingOutcome Outcome,
    double InitialEnergyMwh,
    double InitialPowerMw,
    double FinalEnergyMwh,
    double FinalPowerMw,
    double MaximumEnergyMwh,
    double MaximumPowerMw,
    int PassesUsed);

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
    string SourceFile,
    string LocationName,
    string Description);

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
