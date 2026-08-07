using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Simulation;
using NEM.Model.StorageSizing;
using NEM.Model.Units;

namespace NEM.Model.Economics;

public static class PowerSystemCostCalculator
{
    public static PowerSystemCostBreakdown Calculate(
        Scenario scenario,
        StorageSizingRunResult runResult)
    {
        ArgumentNullException.ThrowIfNull(runResult);
        return Calculate(
            scenario,
            runResult.PowerSystem,
            runResult.Regions.Select(region => region.DispatchOutcome).ToArray());
    }

    public static PowerSystemCostBreakdown Calculate(
        Scenario scenario,
        PowerSystem powerSystem,
        IReadOnlyList<DispatchOutcome> dispatchOutcomes)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(powerSystem);
        ArgumentNullException.ThrowIfNull(dispatchOutcomes);

        ValidateCorrespondence(scenario, powerSystem, dispatchOutcomes);

        var outcomesByRegion = dispatchOutcomes.ToDictionary(
            outcome => outcome.RegionId,
            StringComparer.OrdinalIgnoreCase);
        Money totalAnnualisedGenerationCost = Money.Zero;
        Money totalAnnualisedStorageCost = Money.Zero;
        Energy totalDeliveredEnergy = Energy.Zero;

        foreach (ScenarioRegion scenarioRegion in scenario.Regions)
        {
            DispatchOutcome outcome = outcomesByRegion[scenarioRegion.RegionId];
            var fleetsByTechnology = scenarioRegion.GeneratingFleets.ToDictionary(
                fleet => fleet.Technology);

            totalDeliveredEnergy += outcome.DeliveredToLoad.Integrate();
            foreach ((GenerationTechnology technology, var generation) in outcome.PerFleetGeneration)
            {
                ScenarioGeneratingFleet fleet = fleetsByTechnology[technology];
                GenerationCostParameters costs = fleet.CostParameters;
                Energy generatedEnergy = generation.Integrate();
                Money annualisedCapex = LevelisedCostCalculator.Annuitise(
                    costs.CapitalCost * fleet.NameplateCapacity,
                    scenario.CostBasis.RealDiscountRate,
                    fleet.TechnologyProfile.TechnicalLifeYears);
                Money fixedOpex = costs.FixedOperatingCost.For(fleet.NameplateCapacity, 1);
                Money variableOpex = costs.VariableOperatingCost * generatedEnergy;
                Money fuelCost = costs.FuelPrice.ForHeatRate(fleet.TechnologyProfile.HeatRate)
                    * generatedEnergy;

                totalAnnualisedGenerationCost += annualisedCapex
                    + fixedOpex
                    + variableOpex
                    + fuelCost;
            }

            Region systemRegion = powerSystem.Regions.Single(region =>
                string.Equals(
                    region.RegionId,
                    scenarioRegion.RegionId,
                    StringComparison.OrdinalIgnoreCase));
            var storagePlansByTechnology = scenarioRegion.StorageFleets.ToDictionary(
                fleet => fleet.Technology);
            foreach (StorageFleet storageFleet in systemRegion.StorageFleets)
            {
                ScenarioStorageFleet storagePlan = storagePlansByTechnology[
                    storageFleet.StorageTechnology];
                StorageCostParameters costs = storagePlan.CostParameters;
                Money storageCapex = costs.PowerCapitalCost * storageFleet.PowerCapacity
                    + costs.EnergyCapitalCost * storageFleet.StorageCapacity;
                Money annualisedCapex = LevelisedCostCalculator.Annuitise(
                    storageCapex,
                    scenario.CostBasis.RealDiscountRate,
                    storagePlan.TechnologyProfile.TechnicalLifeYears);
                Money fixedOpex = costs.FixedOperatingCost.For(
                    storageFleet.PowerCapacity,
                    years: 1);

                totalAnnualisedStorageCost += annualisedCapex + fixedOpex;
            }
        }

        return new PowerSystemCostBreakdown(
            totalAnnualisedGenerationCost.Per(totalDeliveredEnergy),
            totalAnnualisedGenerationCost,
            totalAnnualisedStorageCost.Per(totalDeliveredEnergy),
            totalAnnualisedStorageCost,
            totalDeliveredEnergy);
    }

    private static void ValidateCorrespondence(
        Scenario scenario,
        PowerSystem powerSystem,
        IReadOnlyList<DispatchOutcome> dispatchOutcomes)
    {
        if (scenario.PeriodEnd != scenario.PeriodStart.AddYears(1))
        {
            throw new ArgumentException(
                "Power-system cost calculation requires an exact one-year scenario.",
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