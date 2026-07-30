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
        IReadOnlyList<Region> regions)
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

        Id = id;
        DerivedFromScenario = derivedFromScenario;
        Regions = new ReadOnlyCollection<Region>(regions.ToArray());
    }

    public PowerSystemId Id { get; }
    public ScenarioId DerivedFromScenario { get; }
    public IReadOnlyList<Region> Regions { get; }
}