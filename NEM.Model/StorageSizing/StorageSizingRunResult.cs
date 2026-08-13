using System.Collections.ObjectModel;
using NEM.Model.Grid;
using NEM.Model.Simulation;

namespace NEM.Model.StorageSizing;

/// <summary>
/// Final result of a whole-system Battery sizing search, including the selected system and
/// regional dispatch evidence.
/// </summary>
public sealed class StorageSizingRunResult
{
    public StorageSizingRunResult(
        PowerSystem powerSystem,
        IReadOnlyList<RegionalSizingResult> regions,
        IReadOnlyList<InstalledBatteryAssessment> installedBatteryAssessments,
        int dispatchPassCount,
        StorageSizingStatus status,
        string terminationEvidence,
        EnergyLimitedAssessment? energyLimitedAssessment = null)
    {
        ArgumentNullException.ThrowIfNull(powerSystem);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(installedBatteryAssessments);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminationEvidence);
        if (regions.Any(region => region is null))
        {
            throw new ArgumentException("Regional results cannot contain null.", nameof(regions));
        }

        if (installedBatteryAssessments.Any(assessment => assessment is null))
        {
            throw new ArgumentException(
                "Installed Battery assessments cannot contain null.",
                nameof(installedBatteryAssessments));
        }

        if (dispatchPassCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dispatchPassCount));
        }

        if (energyLimitedAssessment is not null
            && energyLimitedAssessment.PowerSystemId != powerSystem.Id)
        {
            throw new ArgumentException(
                "Energy-limited assessment must describe the result power system.",
                nameof(energyLimitedAssessment));
        }

        var systemRegionsById = powerSystem.Regions.ToDictionary(
            region => region.RegionId,
            StringComparer.OrdinalIgnoreCase);
        var resultsByRegionId = new Dictionary<string, RegionalSizingResult>(
            StringComparer.OrdinalIgnoreCase);
        foreach (RegionalSizingResult regionResult in regions)
        {
            if (!resultsByRegionId.TryAdd(regionResult.DispatchOutcome.RegionId, regionResult))
            {
                throw new ArgumentException(
                    "Regional results must contain one result per region.",
                    nameof(regions));
            }
        }

        if (!systemRegionsById.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(resultsByRegionId.Keys))
        {
            throw new ArgumentException(
                "Regional results must correspond exactly to the final power system regions.",
                nameof(regions));
        }

        foreach ((string regionId, Region systemRegion) in systemRegionsById)
        {
            DispatchOutcome outcome = resultsByRegionId[regionId].DispatchOutcome;
            var systemTechnologies = systemRegion.GeneratingFleets
                .Select(fleet => fleet.GenerationTechnology)
                .ToHashSet();
            if (!systemTechnologies.SetEquals(outcome.PerFleetGeneration.Keys))
            {
                throw new ArgumentException(
                    $"Dispatch generation fleets do not match final system region {regionId}.",
                    nameof(regions));
            }

            try
            {
                systemRegion.Demand.TotalDemand.RequireAligned(outcome.Demand);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException(
                    $"Dispatch timeline does not match final system region {regionId}.",
                    nameof(regions),
                    exception);
            }
        }

        PowerSystem = powerSystem;
        Regions = new ReadOnlyCollection<RegionalSizingResult>(regions.ToArray());
        InstalledBatteryAssessments = new ReadOnlyCollection<InstalledBatteryAssessment>(
            installedBatteryAssessments.ToArray());
        DispatchPassCount = dispatchPassCount;
        Status = status;
        TerminationEvidence = terminationEvidence;
        EnergyLimitedAssessment = energyLimitedAssessment;
    }

    /// <summary>Final candidate power system evaluated by the search.</summary>
    public PowerSystem PowerSystem { get; }
    /// <summary>Final sizing and dispatch evidence for each region.</summary>
    public IReadOnlyList<RegionalSizingResult> Regions { get; }
    /// <summary>Assessment of the Battery capacities installed before the search began.</summary>
    public IReadOnlyList<InstalledBatteryAssessment> InstalledBatteryAssessments { get; }
    /// <summary>Number of whole-system dispatch passes performed by the search.</summary>
    public int DispatchPassCount { get; }
    /// <summary>Terminal status of the sizing search.</summary>
    public StorageSizingStatus Status { get; }
    /// <summary>Human-readable evidence explaining the terminal status.</summary>
    public string TerminationEvidence { get; }
    /// <summary>
    /// Whole-system generation-availability evidence when the result is energy-limited; otherwise
    /// null.
    /// </summary>
    public EnergyLimitedAssessment? EnergyLimitedAssessment { get; }
}