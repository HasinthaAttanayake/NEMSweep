using System.Collections.ObjectModel;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Series;
using NEMSweep.Model.Simulation;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.StorageSizing;

/// <summary>
/// Whole-system generation-availability evidence. A positive shortfall means the total energy
/// available from generation across every region is below total demand, so storage cannot close
/// it. Energies are expressed in MWh; binding intervals identify hours where total available
/// generation power is below total demand power.
/// </summary>
public sealed class EnergyLimitedAssessment
{
    private EnergyLimitedAssessment(
        PowerSystemId powerSystemId,
        Energy availableEnergy,
        Energy demandEnergy,
        IReadOnlyList<int> bindingIntervalIndices)
    {
        PowerSystemId = powerSystemId;
        AvailableEnergy = availableEnergy;
        DemandEnergy = demandEnergy;
        ShortfallEnergy = Energy.Max(Energy.Zero, demandEnergy - availableEnergy);
        BindingIntervalIndices = new ReadOnlyCollection<int>(bindingIntervalIndices.ToArray());
    }

    /// <summary>Power system assessed against realised generation availability.</summary>
    public PowerSystemId PowerSystemId { get; }
    /// <summary>Maximum generation energy available over the dispatch period, in MWh.</summary>
    public Energy AvailableEnergy { get; }
    /// <summary>Total regional demand energy over the dispatch period, in MWh.</summary>
    public Energy DemandEnergy { get; }
    /// <summary>Positive period energy deficit that storage cannot supply, in MWh.</summary>
    public Energy ShortfallEnergy { get; }
    /// <summary>Whether available generation energy is below demand energy over the period.</summary>
    public bool IsEnergyLimited => ShortfallEnergy > Energy.Zero;
    /// <summary>Interval indices where available generation power is below demand power.</summary>
    public IReadOnlyList<int> BindingIntervalIndices { get; }

    /// <summary>
    /// Calculates total generator availability across the power system using the same renewable-
    /// resource and monthly generation-budget rules as dispatch. Storage is deliberately excluded
    /// because it shifts energy between intervals but cannot increase whole-period available energy.
    /// </summary>
    public static EnergyLimitedAssessment Assess(PowerSystem powerSystem)
    {
        ArgumentNullException.ThrowIfNull(powerSystem);

        FlowSeries demand = powerSystem.Regions[0].Demand.TotalDemand;
        var availableMw = new double[demand.Length];
        var demandMw = new double[demand.Length];
        foreach (Region region in powerSystem.Regions)
        {
            FlowSeries regionalDemand = region.Demand.TotalDemand;
            regionalDemand.RequireAligned(demand);
            for (int index = 0; index < demand.Length; index++)
            {
                demandMw[index] += regionalDemand[index].Megawatts;
            }

            foreach (GeneratingFleet fleet in region.GeneratingFleets)
            {
                FlowSeries available = fleet.AvailableCapacityFor(region.ResourceProfile, demand);
                var budget = new GenerationBudgetState(fleet);
                for (int index = 0; index < demand.Length; index++)
                {
                    DateTimeOffset instant = demand.InstantAt(index);
                    Power accepted = budget.Take(
                        budget.Headroom(available[index], Power.Zero, instant, demand.Resolution),
                        instant,
                        demand.Resolution);
                    availableMw[index] += accepted.Megawatts;
                }
            }
        }

        var bindingIndices = new List<int>();
        for (int index = 0; index < demand.Length; index++)
        {
            if (availableMw[index] < demandMw[index])
            {
                bindingIndices.Add(index);
            }
        }

        return new EnergyLimitedAssessment(
            powerSystem.Id,
            Energy.FromMegawattHours(availableMw.Sum() * demand.Resolution.TotalHours),
            Energy.FromMegawattHours(demandMw.Sum() * demand.Resolution.TotalHours),
            bindingIndices);
    }
}