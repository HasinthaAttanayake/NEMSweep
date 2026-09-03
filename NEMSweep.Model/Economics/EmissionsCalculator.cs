using NEMSweep.Model.Grid;
using NEMSweep.Model.Scenarios;
using NEMSweep.Model.Simulation;
using NEMSweep.Model.StorageSizing;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Economics;

/// <summary>
/// Accounts the operational carbon dioxide a dispatched system released over one modelled year:
/// each fleet's gross generation multiplied by its scenario emissions intensity, aggregated by
/// region and by technology.
/// </summary>
/// <remarks>
/// <para>
/// This mirrors the fuel term of <see cref="PowerSystemCostCalculator"/> deliberately. Both are a
/// per-MWh-generated rate applied to the same gross generation series, so an accounting change made
/// to one and not the other would be an inconsistency rather than a difference of scope.
/// </para>
/// <para>
/// The scope is combustion during generation. Fuel extraction and delivery, plant construction and
/// decommissioning are outside it, and so is anything attributable to storage or transmission
/// assets, which burn no fuel of their own here: the generation that charged a battery is already
/// counted where it was generated.
/// </para>
/// <para>
/// The generation series it reads is available generation, before curtailment is subtracted. That
/// is exact for the fleets that emit, because dispatch only ever constrains off Solar and Wind and
/// records zero curtailment for a dispatchable fleet. A scenario that gave Solar or Wind a non-zero
/// intensity would be charged for output that was constrained off, which is a reason to keep those
/// intensities at zero rather than a licence to read this as accounting for curtailed emissions.
/// </para>
/// </remarks>
public static class EmissionsCalculator
{
    /// <summary>
    /// Accounts emissions for the final system a storage sizing run settled on, using that run's
    /// own dispatch evidence.
    /// </summary>
    /// <param name="scenario">The scenario supplying emissions intensities.</param>
    /// <param name="runResult">A completed sizing run, including the capacity it introduced.</param>
    /// <returns>System and per-region annual emissions and emissions intensities.</returns>
    public static EmissionsSummary Calculate(
        Scenario scenario,
        StorageSizingRunResult runResult)
    {
        ArgumentNullException.ThrowIfNull(runResult);
        return Calculate(
            scenario,
            runResult.PowerSystem,
            runResult.Regions.Select(region => region.DispatchOutcome).ToArray());
    }

    /// <summary>
    /// Accounts emissions for a realised system against the dispatch evidence produced for it.
    /// </summary>
    /// <param name="scenario">
    /// The scenario supplying emissions intensities. Must cover exactly one year, and must carry
    /// matching assumptions for every realised fleet.
    /// </param>
    /// <param name="powerSystem">The realised system whose generation is being accounted.</param>
    /// <param name="dispatchOutcomes">
    /// Exactly one outcome per system region, aligned to that region's demand.
    /// </param>
    /// <returns>System and per-region annual emissions and emissions intensities.</returns>
    /// <exception cref="ArgumentException">
    /// The scenario, system and outcomes do not correspond.
    /// </exception>
    public static EmissionsSummary Calculate(
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
        var regionSummaries = new List<RegionEmissionsSummary>();

        foreach (ScenarioRegion scenarioRegion in scenario.Regions)
        {
            DispatchOutcome outcome = outcomesByRegion[scenarioRegion.RegionId];
            var fleetsByTechnology = scenarioRegion.GeneratingFleets.ToDictionary(
                fleet => fleet.Technology);

            var contributions = new List<GenerationEmissionsContribution>();
            Emissions regionalEmissions = Emissions.Zero;
            foreach ((GenerationTechnology technology, var generation) in outcome.PerFleetGeneration)
            {
                ScenarioGeneratingFleet fleet = fleetsByTechnology[technology];
                Emissions technologyEmissions = fleet.TechnologyProfile.EmissionsIntensity
                    .For(generation.Integrate());
                regionalEmissions += technologyEmissions;
                contributions.Add(new GenerationEmissionsContribution(
                    technology,
                    technologyEmissions));
            }

            regionSummaries.Add(new RegionEmissionsSummary(
                scenarioRegion.RegionId,
                regionalEmissions,
                outcome.EnergyServed.Integrate(),
                contributions));
        }

        Energy totalEnergyServed = Energy.Zero;
        var systemEmissions = new Dictionary<GenerationTechnology, Emissions>();
        foreach (RegionEmissionsSummary region in regionSummaries)
        {
            totalEnergyServed += region.EnergyServed;
            foreach (GenerationEmissionsContribution contribution in
                region.GenerationEmissionsContributions)
            {
                systemEmissions[contribution.Technology] =
                    systemEmissions.GetValueOrDefault(contribution.Technology, Emissions.Zero)
                    + contribution.Emissions;
            }
        }

        // Sum by technology across regions, so the total reconciles to the published
        // per-technology contributions regardless of region iteration order.
        Emissions totalEmissions = systemEmissions.Values.Aggregate(
            Emissions.Zero,
            (total, contribution) => total + contribution);

        return new EmissionsSummary(
            totalEmissions,
            totalEnergyServed,
            regionSummaries,
            systemEmissions
                .OrderBy(entry => entry.Key)
                .Select(entry => new GenerationEmissionsContribution(entry.Key, entry.Value))
                .ToArray());
    }
}
