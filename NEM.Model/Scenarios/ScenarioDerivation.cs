using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Weather;

namespace NEM.Model.Scenarios;

public static class ScenarioDerivation
{
    public static PowerSystem Derive(
        Scenario scenario,
        IReadOnlyDictionary<string, FlowSeries> baseDemandByRegion,
        IReadOnlyDictionary<string, RegionalResourceProfile?>? resourceProfilesByRegion = null)
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
            return new Region(
                plan.RegionId,
                plan.GeneratingFleets.Select(fleet => fleet.ToGeneratingFleet()).ToArray(),
                hourlyDemand,
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
            regions);
    }
}