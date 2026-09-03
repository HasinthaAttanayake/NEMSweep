using NEMSweep.Model.Grid;
using NEMSweep.Model.Scenarios;
using NEMSweep.Model.Series;
using NEMSweep.Model.Simulation;

namespace NEMSweep.Model.Economics;

/// <summary>
/// The correspondence a realised system, the scenario it came from, and the dispatch evidence
/// produced for it must satisfy before anything is accounted against them.
/// </summary>
/// <remarks>
/// Costing and emissions both read scenario assumptions through dispatch evidence, and both are
/// annual figures, so they answer to one definition of "these three describe the same run" rather
/// than to two that could drift apart.
/// </remarks>
internal static class RealisedSystemCorrespondence
{
    /// <summary>Directed endpoint identity normalised only for case-insensitive region matching.</summary>
    private static (string From, string To) Direction(string fromRegionId, string toRegionId) =>
        (fromRegionId.ToUpperInvariant(), toRegionId.ToUpperInvariant());

    internal static void Validate(
        Scenario scenario,
        PowerSystem powerSystem,
        IReadOnlyList<DispatchOutcome> dispatchOutcomes)
    {
        if (scenario.PeriodEnd != scenario.PeriodStart.AddYears(1))
        {
            throw new ArgumentException(
                "Annual accounting, of both cost and emissions, requires an exact one-year "
                + "scenario.",
                nameof(scenario));
        }

        if (powerSystem.DerivedFromScenario != scenario.Id)
        {
            throw new ArgumentException(
                "Power system must be derived from the supplied scenario.",
                nameof(powerSystem));
        }

        var scenarioRegions = scenario.Regions.ToDictionary(
            region => region.RegionId,
            StringComparer.OrdinalIgnoreCase);
        var systemRegions = powerSystem.Regions.ToDictionary(
            region => region.RegionId,
            StringComparer.OrdinalIgnoreCase);
        if (!scenarioRegions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(systemRegions.Keys))
        {
            throw new ArgumentException(
                "Scenario and power system must contain the same regions.",
                nameof(powerSystem));
        }

        // Transmission cost comes from scenario intent while flows come from the realised
        // system, so the two must describe the same links or the cost would be charged
        // against assets that were never dispatched.
        HashSet<(string, string)> scenarioLinks = scenario.Interconnectors
            .Select(link => Direction(link.FromRegionId, link.ToRegionId))
            .ToHashSet();
        HashSet<(string, string)> systemLinks = powerSystem.Interconnectors
            .Select(link => Direction(link.FromRegionId, link.ToRegionId))
            .ToHashSet();
        if (!scenarioLinks.SetEquals(systemLinks))
        {
            throw new ArgumentException(
                "Scenario and power system must contain the same interconnectors.",
                nameof(powerSystem));
        }

        if (dispatchOutcomes.Any(outcome => outcome is null))
        {
            throw new ArgumentException(
                "Dispatch outcomes cannot contain null.",
                nameof(dispatchOutcomes));
        }

        var outcomesByRegion = new Dictionary<string, DispatchOutcome>(
            StringComparer.OrdinalIgnoreCase);
        foreach (DispatchOutcome outcome in dispatchOutcomes)
        {
            if (!outcomesByRegion.TryAdd(outcome.RegionId, outcome))
            {
                throw new ArgumentException(
                    "Dispatch outcomes must contain one result per region.",
                    nameof(dispatchOutcomes));
            }
        }

        if (!systemRegions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(outcomesByRegion.Keys))
        {
            throw new ArgumentException(
                "Dispatch outcomes must contain exactly one result per power-system region.",
                nameof(dispatchOutcomes));
        }

        foreach ((string regionId, Region systemRegion) in systemRegions)
        {
            ScenarioRegion scenarioRegion = scenarioRegions[regionId];
            DispatchOutcome outcome = outcomesByRegion[regionId];
            var scenarioTechnologies = scenarioRegion.GeneratingFleets
                .Select(fleet => fleet.Technology)
                .ToHashSet();
            var systemTechnologies = systemRegion.GeneratingFleets
                .Select(fleet => fleet.GenerationTechnology)
                .ToHashSet();
            if (!scenarioTechnologies.SetEquals(systemTechnologies)
                || !systemTechnologies.SetEquals(outcome.PerFleetGeneration.Keys))
            {
                throw new ArgumentException(
                    $"Generation fleets do not correspond in region {regionId}.",
                    nameof(dispatchOutcomes));
            }

            var scenarioStorageTechnologies = scenarioRegion.StorageFleets
                .Select(fleet => fleet.Technology)
                .ToHashSet();
            StorageTechnology[] unpricedStorageTechnologies = systemRegion.StorageFleets
                .Select(fleet => fleet.StorageTechnology)
                .Where(technology => !scenarioStorageTechnologies.Contains(technology))
                .ToArray();
            if (unpricedStorageTechnologies.Length > 0)
            {
                throw new ArgumentException(
                    $"Storage fleets lack scenario cost assumptions in region {regionId}: "
                    + string.Join(", ", unpricedStorageTechnologies),
                    nameof(powerSystem));
            }

            systemRegion.Demand.TotalDemand.RequireAligned(outcome.Demand);
            DateTimeOffset outcomeEnd = outcome.Demand.Start.AddTicks(
                outcome.Demand.Resolution.Ticks * outcome.Demand.Length);
            if (outcome.Demand.Start != scenario.PeriodStart || outcomeEnd != scenario.PeriodEnd)
            {
                throw new ArgumentException(
                    $"Dispatch outcome for region {regionId} must span the scenario period.",
                    nameof(dispatchOutcomes));
            }
        }
    }
}
