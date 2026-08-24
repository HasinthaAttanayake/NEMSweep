using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Simulation;
using NEM.Model.StorageSizing;
using NEM.Model.Units;

namespace NEM.Model.Economics;

/// <summary>
/// Prices a dispatched system for one modelled year: annuitised capital plus one year of operating
/// cost for every generation, storage and transmission asset, divided by the energy actually served.
/// </summary>
/// <remarks>
/// <para>
/// This is the cost of building and running the system, not a retail electricity bill. It excludes
/// retail margin, network distribution beyond the modelled interconnectors, taxes, and market
/// settlement. The figures are modelled estimates, not audited accounts.
/// </para>
/// <para>
/// Storage asset cost does not add charging energy, because gross-generation variable operating
/// cost and fuel already price the generation used to charge, and therefore already include
/// round-trip losses. The storage component is annualised storage asset cost over the same served
/// energy denominator; it is not a standalone levelised cost of storage.
/// </para>
/// <para>
/// Transmission is annuitised from each directed interconnector's cost assumptions and charged once
/// at system level, so regional costs deliberately do not sum to the system total. Route length is
/// declared directly on the scenario interconnector rather than derived from anything else, so
/// transmission cost does not depend on regional weather data at all.
/// </para>
/// </remarks>
public static class PowerSystemCostCalculator
{
    /// <summary>
    /// Prices the final system a storage sizing run settled on, using that run's own dispatch
    /// evidence.
    /// </summary>
    /// <param name="scenario">The scenario supplying cost assumptions and the cost basis.</param>
    /// <param name="runResult">A completed sizing run, including the capacity it introduced.</param>
    /// <returns>System and per-region annual costs and their levelised costs.</returns>
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

    /// <summary>Prices a realised system against the dispatch evidence produced for it.</summary>
    /// <param name="scenario">
    /// The scenario supplying cost assumptions and the cost basis. Must cover exactly one year, and
    /// must carry matching assumptions for every realised storage fleet, including capacity storage
    /// sizing introduced.
    /// </param>
    /// <param name="powerSystem">The realised system whose assets are being priced.</param>
    /// <param name="dispatchOutcomes">
    /// Exactly one outcome per system region, aligned to that region's demand.
    /// </param>
    /// <returns>System and per-region annual costs and their levelised costs.</returns>
    /// <exception cref="ArgumentException">
    /// The scenario, system and outcomes do not correspond, or a realised storage fleet has no
    /// matching scenario assumptions.
    /// </exception>
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
        var regionBreakdowns = new List<RegionCostBreakdown>();

        foreach (ScenarioRegion scenarioRegion in scenario.Regions)
        {
            DispatchOutcome outcome = outcomesByRegion[scenarioRegion.RegionId];
            var fleetsByTechnology = scenarioRegion.GeneratingFleets.ToDictionary(
                fleet => fleet.Technology);

            Money annualisedGenerationCost = Money.Zero;
            var generationCostContributions = new List<GenerationCostContribution>();
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

                Money technologyAnnualisedCost = annualisedCapex
                    + fixedOpex
                    + variableOpex
                    + fuelCost;
                annualisedGenerationCost += technologyAnnualisedCost;
                generationCostContributions.Add(new GenerationCostContribution(
                    technology,
                    technologyAnnualisedCost));
            }

            Region systemRegion = powerSystem.Regions.Single(region =>
                string.Equals(
                    region.RegionId,
                    scenarioRegion.RegionId,
                    StringComparison.OrdinalIgnoreCase));
            var storagePlansByTechnology = scenarioRegion.StorageFleets.ToDictionary(
                fleet => fleet.Technology);
            Money annualisedStorageCost = Money.Zero;
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

                annualisedStorageCost += annualisedCapex + fixedOpex;
            }

            regionBreakdowns.Add(new RegionCostBreakdown(
                scenarioRegion.RegionId,
                annualisedGenerationCost,
                annualisedStorageCost,
                outcome.DeliveredToLoad.Integrate(),
                outcome.Imports.Integrate() - outcome.Exports.Integrate(),
                generationCostContributions));
        }

        Money totalAnnualisedStorageCost = Money.Zero;
        Energy totalDeliveredEnergy = Energy.Zero;
        var systemGenerationCosts = new Dictionary<GenerationTechnology, Money>();
        foreach (RegionCostBreakdown region in regionBreakdowns)
        {
            totalAnnualisedStorageCost += region.AnnualisedStorageCost;
            totalDeliveredEnergy += region.DeliveredEnergy;
            foreach (GenerationCostContribution contribution in region.GenerationCostContributions)
            {
                systemGenerationCosts[contribution.Technology] =
                    systemGenerationCosts.GetValueOrDefault(contribution.Technology, Money.Zero)
                    + contribution.AnnualisedCost;
            }
        }

        // Sum by technology across regions, so the total reconciles exactly to the
        // published per-technology contributions regardless of region iteration order.
        Money totalAnnualisedGenerationCost = systemGenerationCosts.Values.Aggregate(
            Money.Zero,
            (total, contribution) => total + contribution);

        return new PowerSystemCostBreakdown(
            totalAnnualisedGenerationCost,
            totalAnnualisedStorageCost,
            AnnualisedTransmissionCost(scenario),
            totalDeliveredEnergy,
            regionBreakdowns,
            systemGenerationCosts
                .OrderBy(entry => entry.Key)
                .Select(entry => new GenerationCostContribution(entry.Key, entry.Value))
                .ToArray(),
            scenario.Interconnectors.Count > 0);
    }

    /// <summary>
    /// Interconnector capital and fixed operating cost, charged against its declared route length
    /// and its directed capacity. There is no variable term: transmission has no marginal fuel cost
    /// here, and losses already raise cost implicitly by requiring more generation per MWh delivered.
    /// </summary>
    private static Money AnnualisedTransmissionCost(Scenario scenario)
    {
        Money total = Money.Zero;
        foreach (ScenarioInterconnector interconnector in scenario.Interconnectors)
        {
            TransmissionCostParameters costs = interconnector.CostParameters;
            Distance distance = interconnector.RouteLength;
            total += LevelisedCostCalculator.Annuitise(
                    costs.CapitalCost.For(distance, interconnector.Capacity),
                    scenario.CostBasis.RealDiscountRate,
                    interconnector.TechnicalLifeYears)
                + costs.FixedOperatingCost.For(distance, interconnector.Capacity, years: 1);
        }

        return total;
    }

    /// <summary>Directed endpoint identity normalised only for case-insensitive region matching.</summary>
    private static (string From, string To) Direction(string fromRegionId, string toRegionId) =>
        (fromRegionId.ToUpperInvariant(), toRegionId.ToUpperInvariant());

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