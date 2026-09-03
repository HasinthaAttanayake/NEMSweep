using NEMSweep.Model.Grid;
using NEMSweep.Model.Scenarios;
using NEMSweep.Model.Simulation;
using NEMSweep.Model.StorageSizing;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Economics;

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

        RealisedSystemCorrespondence.Validate(scenario, powerSystem, dispatchOutcomes);

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
                outcome.EnergyServed.Integrate(),
                outcome.Imports.Integrate() - outcome.Exports.Integrate(),
                generationCostContributions));
        }

        Money totalAnnualisedStorageCost = Money.Zero;
        Energy totalEnergyServed = Energy.Zero;
        var systemGenerationCosts = new Dictionary<GenerationTechnology, Money>();
        foreach (RegionCostBreakdown region in regionBreakdowns)
        {
            totalAnnualisedStorageCost += region.AnnualisedStorageCost;
            totalEnergyServed += region.EnergyServed;
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
            totalEnergyServed,
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
    /// here, and losses already raise cost implicitly by requiring more generation per MWh served.
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
}