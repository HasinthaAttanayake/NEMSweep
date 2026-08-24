using System.Collections.ObjectModel;
using NEMSweep.Model.Scenarios;
using NEMSweep.Model.Weather;

namespace NEMSweep.Model.Grid;

/// <summary>
/// Stable identity for a realised power system. Cited by dispatch evidence so a result can be
/// traced to the system it describes without serialising the object graph.
/// </summary>
public sealed record PowerSystemId
{
    /// <summary>Creates a power-system identity from a non-blank string.</summary>
    /// <param name="value">The identifier.</param>
    public PowerSystemId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>The identifier as supplied.</summary>
    public string Value { get; }

    /// <summary>Returns <see cref="Value"/>.</summary>
    public override string ToString() => Value;
}

/// <summary>
/// The realised grid: scenario intent turned into regions with demand, fleets and resources, plus
/// the directed links between them. This is what <see cref="NEMSweep.Model.Simulation.Dispatcher"/>
/// consumes, and it is deliberately scenario-blind beyond citing the scenario it came from.
/// </summary>
/// <remarks>
/// Interconnectors are owned here rather than by either endpoint, because a link belongs to
/// neither region alone. The type is immutable; storage sizing explores capacity by building new
/// systems with <see cref="WithRegions"/> rather than by mutating one.
/// </remarks>
public sealed class PowerSystem
{
    /// <summary>Validates and creates a power system.</summary>
    /// <param name="id">Stable identity for this realised system.</param>
    /// <param name="derivedFromScenario">The scenario this system was realised from.</param>
    /// <param name="regions">At least one region, with distinct IDs compared case-insensitively.</param>
    /// <param name="interconnectors">
    /// Directed links, at most one per exact direction, whose endpoints must both be regions of
    /// this system. Null or empty makes the regions electrically independent.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The regions are empty or duplicated, or a link names an unknown endpoint or repeats a
    /// direction.
    /// </exception>
    public PowerSystem(
        PowerSystemId id,
        ScenarioId derivedFromScenario,
        IReadOnlyList<Region> regions,
        IReadOnlyList<Interconnector>? interconnectors = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(derivedFromScenario);
        ArgumentNullException.ThrowIfNull(regions);
        if (regions.Count == 0 || regions.Any(region => region is null))
        {
            throw new ArgumentException(
                "A power system must contain at least one non-null region.",
                nameof(regions));
        }

        if (regions.DistinctBy(region => region.RegionId, StringComparer.OrdinalIgnoreCase).Count()
            != regions.Count)
        {
            throw new ArgumentException(
                "A power system cannot contain duplicate region IDs.",
                nameof(regions));
        }

        Interconnector[] links = interconnectors?.ToArray() ?? [];
        ValidateInterconnectors(regions, links);

        Id = id;
        DerivedFromScenario = derivedFromScenario;
        Regions = new ReadOnlyCollection<Region>(regions.ToArray());
        Interconnectors = new ReadOnlyCollection<Interconnector>(links);
    }

    /// <summary>Stable identity for this realised system.</summary>
    public PowerSystemId Id { get; }

    /// <summary>The scenario this system was realised from.</summary>
    public ScenarioId DerivedFromScenario { get; }

    /// <summary>The regions dispatched together, in a stable order.</summary>
    public IReadOnlyList<Region> Regions { get; }

    /// <summary>
    /// Directed transfer paths between regions. Empty means the regions are
    /// electrically independent, which reproduces single-region dispatch exactly.
    /// </summary>
    public IReadOnlyList<Interconnector> Interconnectors { get; }

    /// <summary>
    /// A copy of this system with different regions and the same links. Storage sizing rebuilds
    /// regions repeatedly, so the links are forwarded rather than defaulted; dropping them here
    /// would silently unlink the system partway through a search.
    /// </summary>
    /// <param name="regions">The replacement regions. Validated as on construction.</param>
    public PowerSystem WithRegions(IReadOnlyList<Region> regions) =>
        new(Id, DerivedFromScenario, regions, Interconnectors);

    /// <summary>A copy of this system with different links and the same regions.</summary>
    /// <param name="interconnectors">The replacement links, or null to unlink the system.</param>
    public PowerSystem WithInterconnectors(IReadOnlyList<Interconnector>? interconnectors) =>
        new(Id, DerivedFromScenario, Regions, interconnectors);

    /// <summary>
    /// A region's weather resource profile, the only source of regional location in the model.
    /// Shared by every reader that derives an interconnector endpoint's map coordinates from it, so
    /// a region missing weather data reports the same failure everywhere. Transmission cost does
    /// not depend on this: an interconnector's route length is declared on the scenario directly.
    /// </summary>
    public RegionalResourceProfile RequireResourceProfile(string regionId)
    {
        Region? region = Regions.FirstOrDefault(candidate =>
            string.Equals(candidate.RegionId, regionId, StringComparison.OrdinalIgnoreCase));
        if (region is null)
        {
            throw new InvalidOperationException(
                $"Region '{regionId}' was not found in the power system.");
        }

        return region.ResourceProfile
            ?? throw new InvalidOperationException(
                $"Region '{regionId}' requires a weather resource profile to derive its "
                + "interconnectors' endpoint location.");
    }

    private static void ValidateInterconnectors(
        IReadOnlyList<Region> regions,
        IReadOnlyList<Interconnector> interconnectors)
    {
        if (interconnectors.Any(interconnector => interconnector is null))
        {
            throw new ArgumentException(
                "A power system cannot contain a null interconnector.",
                nameof(interconnectors));
        }

        var regionIds = regions
            .Select(region => region.RegionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directions = new HashSet<(string From, string To)>();
        foreach (Interconnector interconnector in interconnectors)
        {
            RequireKnownRegion(regionIds, interconnector.FromRegionId);
            RequireKnownRegion(regionIds, interconnector.ToRegionId);

            var direction = (
                interconnector.FromRegionId.ToUpperInvariant(),
                interconnector.ToRegionId.ToUpperInvariant());
            if (!directions.Add(direction))
            {
                throw new ArgumentException(
                    "A power system cannot contain duplicate interconnectors from "
                    + $"'{interconnector.FromRegionId}' to '{interconnector.ToRegionId}'.",
                    nameof(interconnectors));
            }
        }
    }

    private static void RequireKnownRegion(HashSet<string> regionIds, string regionId)
    {
        if (!regionIds.Contains(regionId))
        {
            throw new ArgumentException(
                $"Interconnector endpoint '{regionId}' is not a region of this power system.",
                "interconnectors");
        }
    }
}