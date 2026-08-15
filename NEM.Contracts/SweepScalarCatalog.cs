using System.Collections.ObjectModel;
using System.Text.Json;

namespace NEM.Contracts;

/// <summary>
/// Display label and unit for every field of <see cref="SweepPointScalarResultsDTO"/>. This
/// describes the model's outputs, so it lives with them and is shared by every client rather than
/// being restated per consumer, and it is not repeated in each emitted artifact.
/// </summary>
public sealed record SweepScalarDescriptor(
    string Name,
    string Label,
    string Unit,
    string? Currency = null,
    bool Chartable = true);

/// <summary>
/// Catalogue of the sweep scalar descriptors, keyed by the JSON property name each scalar is
/// emitted under. A guard test keeps it in step with
/// <see cref="SweepPointScalarResultsDTO"/>.
/// </summary>
public static class SweepScalarCatalog
{
    private static readonly SweepScalarDescriptor[] All =
    [
        new("slcoeAudPerMwh", "System levelised cost", "AUD/MWh served", "AUD"),
        new("generationSlcoeAudPerMwh", "Generation levelised cost", "AUD/MWh served", "AUD"),
        new("storageSlcoeAudPerMwh", "Storage levelised cost", "AUD/MWh served", "AUD"),
        new("demandMwh", "Demand", "MWh"),
        new("energyServedMwh", "Energy served", "MWh"),
        new("deliveredGenerationMwh", "Delivered generation", "MWh"),
        new("achievedRenewableShareGridScale", "Achieved renewable share (grid scale)", "fraction"),
        new("achievedRenewableShareNative", "Achieved renewable share (native)", "fraction"),
        new("storagePowerMw", "Storage power capacity", "MW"),
        new("storageEnergyMwh", "Storage energy capacity", "MWh"),
        new("unservedEnergyMwh", "Unserved energy", "MWh"),
        new("unservedEnergyPercentageOfDemand", "Unserved energy", "% of demand"),
        new("unservedHours", "Unserved hours", "h"),
        new("hoursServedFraction", "Hours served", "fraction"),
        new("peakUnservedPowerMw", "Peak unserved power", "MW"),
        new("curtailedEnergyMwh", "Curtailed energy", "MWh"),
        new("transmissionSlcotAudPerMwh", "Transmission levelised cost", "AUD/MWh served", "AUD"),
        new("transmissionCostStatus", "Transmission cost status", "status", Chartable: false),
        new("netImportedEnergyMwh", "Net imported energy", "MWh"),
    ];

    private static readonly ReadOnlyDictionary<string, SweepScalarDescriptor> ByName =
        new(All.ToDictionary(descriptor => descriptor.Name, StringComparer.Ordinal));

    /// <summary>Every scalar descriptor, in the order the scalars are emitted.</summary>
    public static IReadOnlyList<SweepScalarDescriptor> Descriptors { get; } =
        new ReadOnlyCollection<SweepScalarDescriptor>(All);

    /// <summary>The JSON property names of every field of <see cref="SweepPointScalarResultsDTO"/>.</summary>
    public static IEnumerable<string> ScalarNames() => typeof(SweepPointScalarResultsDTO)
        .GetProperties()
        .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name));

    /// <summary>Descriptor for a scalar, or null when the name is not a known scalar.</summary>
    public static SweepScalarDescriptor? Find(string name) =>
        ByName.GetValueOrDefault(name);
}
