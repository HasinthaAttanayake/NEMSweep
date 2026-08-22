using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Weather;

namespace NEM.Model.Scenarios;

/// <summary>
/// The pure transformation from scenario intent to a realised
/// <see cref="NEM.Model.Grid.PowerSystem"/>. It is the boundary between what a user asked for and
/// what the model will actually dispatch.
/// </summary>
/// <remarks>
/// The service has no I/O and no knowledge of files: the caller supplies demand and weather already
/// parsed. That is what keeps <see cref="NEM.Model.Simulation.Dispatcher"/> scenario-blind and lets
/// storage sizing rebuild candidate systems without becoming scenario-aware.
/// </remarks>
public static class ScenarioDerivation
{
    /// <summary>Realises a power system from scenario intent plus aligned regional inputs.</summary>
    /// <param name="scenario">The intent to realise.</param>
    /// <param name="baseDemandByRegion">
    /// Exactly one base demand series per scenario region, keyed case-insensitively, covering
    /// exactly the scenario period. Resampled to hourly resolution.
    /// </param>
    /// <param name="resourceProfilesByRegion">
    /// Optional weather resources per region, aligned to the same timeline. Required for any region
    /// with intermittent generation, and for any region that is an interconnector endpoint, because
    /// a region's resource profile is the model's only source of its location.
    /// </param>
    /// <param name="additiveDemandComponentsByRegion">
    /// Optional named demand flows added on top of base demand, for example a data-centre load.
    /// Each must be non-negative and exactly aligned with that region's base demand.
    /// </param>
    /// <returns>
    /// The realised system. Storage plans with positive capacity become fleets; zero-capacity plans
    /// are omitted, but their assumptions are retained on the region so storage sizing can use them.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The demand does not cover exactly the scenario period and regions, or an optional input is
    /// not aligned with base demand.
    /// </exception>
    public static PowerSystem Derive(
        Scenario scenario,
        IReadOnlyDictionary<string, FlowSeries> baseDemandByRegion,
        IReadOnlyDictionary<string, RegionalResourceProfile?>? resourceProfilesByRegion = null,
        IReadOnlyDictionary<string, IReadOnlyList<DemandComponent>>? additiveDemandComponentsByRegion = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(baseDemandByRegion);
        if (baseDemandByRegion.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null))
        {
            throw new ArgumentException(
                "Regional demand must have non-empty region IDs and non-null series.",
                nameof(baseDemandByRegion));
        }

        var demandByRegion = new Dictionary<string, FlowSeries>(
            baseDemandByRegion,
            StringComparer.OrdinalIgnoreCase);
        if (demandByRegion.Count != baseDemandByRegion.Count
            || !scenario.Regions.Select(region => region.RegionId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(demandByRegion.Keys))
        {
            throw new ArgumentException(
                "Regional demand must contain exactly one series for every scenario region.",
                nameof(baseDemandByRegion));
        }

        var resourcesByRegion = resourceProfilesByRegion is null
            ? new Dictionary<string, RegionalResourceProfile?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, RegionalResourceProfile?>(
                resourceProfilesByRegion,
                StringComparer.OrdinalIgnoreCase);
        if (resourceProfilesByRegion is not null
            && (resourcesByRegion.Count != resourceProfilesByRegion.Count
                || resourcesByRegion.Keys.Any(regionId => !demandByRegion.ContainsKey(regionId))))
        {
            throw new ArgumentException(
                "Resource profiles must identify distinct scenario regions.",
                nameof(resourceProfilesByRegion));
        }

        var componentsByRegion = additiveDemandComponentsByRegion is null
            ? new Dictionary<string, IReadOnlyList<DemandComponent>>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, IReadOnlyList<DemandComponent>>(
                additiveDemandComponentsByRegion,
                StringComparer.OrdinalIgnoreCase);
        if (additiveDemandComponentsByRegion is not null
            && (componentsByRegion.Count != additiveDemandComponentsByRegion.Count
                || !scenario.Regions.Select(region => region.RegionId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(componentsByRegion.Keys)
                || componentsByRegion.Any(entry => entry.Value is null)))
        {
            throw new ArgumentException(
                "Additive demand components must contain a non-null collection for every scenario region.",
                nameof(additiveDemandComponentsByRegion));
        }

        Region[] regions = scenario.Regions.Select(plan =>
        {
            FlowSeries hourlyDemand = demandByRegion[plan.RegionId].ResampleToHourly();
            DateTimeOffset demandEnd = hourlyDemand.Start.AddTicks(
                hourlyDemand.Resolution.Ticks * hourlyDemand.Length);
            if (hourlyDemand.Start != scenario.PeriodStart || demandEnd != scenario.PeriodEnd)
            {
                throw new ArgumentException(
                    "Demand must align exactly with the scenario period.",
                    nameof(baseDemandByRegion));
            }

            resourcesByRegion.TryGetValue(plan.RegionId, out RegionalResourceProfile? resourceProfile);
            componentsByRegion.TryGetValue(
                plan.RegionId,
                out IReadOnlyList<DemandComponent>? additiveDemandComponents);
            return new Region(
                plan.RegionId,
                plan.GeneratingFleets.Select(fleet => fleet.ToGeneratingFleet()).ToArray(),
                hourlyDemand,
                additiveDemandComponents,
                resourceProfile: resourceProfile,
                storageFleets: plan.StorageFleets
                    .Select(fleet => fleet.ToStorageFleet())
                    .OfType<StorageFleet>()
                    .ToArray(),
                storageTechnologyProfiles: plan.StorageFleets.ToDictionary(
                    fleet => fleet.Technology,
                    fleet => fleet.TechnologyProfile));
        }).ToArray();

        return new PowerSystem(
            new PowerSystemId($"{scenario.Id.Value}-system"),
            scenario.Id,
            regions,
            scenario.Interconnectors
                .Select(interconnector => interconnector.ToInterconnector())
                .ToArray());
    }
}