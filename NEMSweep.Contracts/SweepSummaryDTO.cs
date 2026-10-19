using System.Text.Json.Serialization;

namespace NEMSweep.Contracts;

/// <summary>
/// One row per point, carrying only the values that say what the sweep found. Published alongside
/// the sweep index, which already holds every one of these figures nested per point and per region:
/// this artifact exists because reading the shape of a sweep off that index means writing a
/// program, and the shape of the sweep is why it was run.
/// </summary>
/// <param name="SchemaVersion">Schema version of this artifact.</param>
/// <param name="SweepId">The sweep this summarises.</param>
/// <param name="Name">The sweep's human-readable name.</param>
/// <param name="Axis">How the swept quantity is captioned, so a chart can be drawn from this file alone.</param>
/// <param name="Points">Every point of the run, in the definition's order.</param>
public sealed record SweepSummaryDTO(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string SweepId,
    [property: JsonRequired] string Name,
    [property: JsonRequired] SweepAxisDTO Axis,
    [property: JsonRequired] SweepSummaryRowDTO[] Points);

/// <summary>
/// One point's headline result. Every value is copied from that point's index entry, so this is a
/// projection rather than a second opinion.
/// </summary>
/// <param name="PointId">The point's identifier, as the definition declared it.</param>
/// <param name="Label">The point's human-readable label.</param>
/// <param name="AxisValue">Where the point sits on the sweep's axis. A plotting coordinate only.</param>
/// <param name="Status">Whether the point's run completed.</param>
/// <param name="ReliabilityTargetMet">
/// Whether the point met its reliability target, or <see langword="null"/> when it never got far
/// enough to have one. Distinct from <paramref name="Status"/>: a point that dispatched a full
/// period and missed its target is <c>succeeded</c>.
/// </param>
/// <param name="UnservedEnergyPercentageOfDemand">
/// Unserved energy as a percentage of demand energy. Already a percentage, not a fraction.
/// </param>
/// <param name="TotalBatteryEnergyMwh">
/// Battery energy installed in the dispatched system, in MWh. Battery alone: it excludes pumped
/// hydro and any other storage technology the sizing search cannot grow.
/// </param>
/// <param name="SlcoeAudPerMwh">System levelised cost, in AUD per MWh served.</param>
/// <remarks>
/// Every result field is nullable, because a point that failed to run has none of them. An absent
/// value is published as null rather than as zero: zero is a measurement.
/// </remarks>
public sealed record SweepSummaryRowDTO(
    [property: JsonRequired] string PointId,
    [property: JsonRequired] string Label,
    [property: JsonRequired] double AxisValue,
    [property: JsonRequired] SweepPointStatus Status,
    bool? ReliabilityTargetMet,
    double? UnservedEnergyPercentageOfDemand,
    double? TotalBatteryEnergyMwh,
    decimal? SlcoeAudPerMwh);
