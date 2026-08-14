using System.Collections.ObjectModel;
using NEM.Model.Scenarios;

namespace NEM.Model.Grid;

public sealed record PowerSystemId
{
    public PowerSystemId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed class PowerSystem
{
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

    public PowerSystemId Id { get; }
    public ScenarioId DerivedFromScenario { get; }
    public IReadOnlyList<Region> Regions { get; }

    /// <summary>
    /// Directed transfer paths between regions. Empty means the regions are
    /// electrically independent, which reproduces single-region dispatch exactly.
    /// </summary>
    public IReadOnlyList<Interconnector> Interconnectors { get; }

    public PowerSystem WithRegions(IReadOnlyList<Region> regions) =>
        new(Id, DerivedFromScenario, regions, Interconnectors);

    public PowerSystem WithInterconnectors(IReadOnlyList<Interconnector>? interconnectors) =>
        new(Id, DerivedFromScenario, Regions, interconnectors);

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