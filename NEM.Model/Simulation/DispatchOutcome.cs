using System.Collections.ObjectModel;
using NEM.Model.Grid;
using NEM.Model.Series;

namespace NEM.Model.Simulation;

/// <summary>
/// Immutable hourly dispatch evidence for one region, including energy-balance series and
/// reliability measures derived from unserved demand. Per-fleet delivered and charge flows
/// are bookkeeping allocations recorded as generation is diverted from curtailment or produced
/// incrementally for charging; they are not physical attributions of co-mingled electricity.
/// </summary>
public sealed record DispatchOutcome
{
    private const double BalanceTolerance = 1e-9;

    /// <summary>Identifies the region that was dispatched.</summary>
    public string RegionId { get; }
    /// <summary>Available generation by technology before curtailment.</summary>
    public IReadOnlyDictionary<GenerationTechnology, FlowSeries> PerFleetGeneration { get; }
    /// <summary>Available generation constrained off by technology.</summary>
    public IReadOnlyDictionary<GenerationTechnology, FlowSeries> PerFleetCurtailment { get; }
    /// <summary>
    /// Generator output delivered to the grid by technology after curtailment and charging,
    /// including load and exports. This is a consistent bookkeeping allocation, not a
    /// physical attribution.
    /// </summary>
    public IReadOnlyDictionary<GenerationTechnology, FlowSeries> PerFleetDelivered { get; }
    /// <summary>
    /// Storage charging booked to the technology whose curtailment was reduced or whose
    /// generation was increased. This is a consistent allocation, not a physical attribution.
    /// </summary>
    public IReadOnlyDictionary<GenerationTechnology, FlowSeries> PerFleetCharge { get; }
    /// <summary>Total regional demand.</summary>
    public FlowSeries Demand { get; }
    /// <summary>Demand before additive components, used as the native-demand denominator.</summary>
    public FlowSeries NativeDemand { get; }
    /// <summary>Demand served by generation, storage discharge, and imports.</summary>
    public FlowSeries DeliveredToLoad { get; }
    /// <summary>Total power drawn from the grid to charge storage, in MW.</summary>
    public FlowSeries Charge { get; }
    /// <summary>Power discharged from storage to serve demand, in MW.</summary>
    public FlowSeries Discharge { get; }
    /// <summary>Power imported into the region, in MW, net of transmission losses.</summary>
    public FlowSeries Imports { get; }
    /// <summary>Power exported from the region, in MW, metered at the sending end.</summary>
    public FlowSeries Exports { get; }
    /// <summary>Total non-negative magnitude of available generation constrained off.</summary>
    public FlowSeries Curtailment { get; }
    /// <summary>Demand that remains unserved after generation, storage, and imports.</summary>
    public FlowSeries Unserved { get; }
    /// <summary>Storage energy level by technology at the start of each dispatch interval.</summary>
    public IReadOnlyDictionary<StorageTechnology, StockSeries> StateOfChargeByTechnology { get; }
    /// <summary>Reliability measures calculated from <see cref="Unserved"/> and <see cref="Demand"/>.</summary>
    public ReliabilityMetrics Reliability { get; }
    /// <summary>Delivered-generation renewable shares calculated from typed fleet technologies.</summary>
    public RenewableShareMetrics RenewableShare { get; }
    private DemandProfile? DemandProfile { get; }

    /// <summary>Validates and creates immutable dispatch evidence for one region.</summary>
    /// <param name="regionId">Identifies the region that was dispatched.</param>
    /// <param name="perFleetGeneration">Available generation by technology before curtailment.</param>
    /// <param name="perFleetCurtailment">Available generation constrained off by technology; must share the same technology keys as <paramref name="perFleetGeneration"/>.</param>
    /// <param name="perFleetDelivered">Generator output delivered to the grid by technology, covering both local load and exports, as a bookkeeping allocation rather than a physical attribution.</param>
    /// <param name="perFleetCharge">Storage charging booked to the technology it was allocated against.</param>
    /// <param name="demand">Total regional demand at hourly resolution.</param>
    /// <param name="unserved">Demand that remains unserved after generation, storage, and imports.</param>
    /// <param name="charge">Total power drawn from the grid to charge storage, in MW.</param>
    /// <param name="discharge">Power discharged from storage to serve demand, in MW.</param>
    /// <param name="imports">Power imported into the region, in MW, net of transmission losses.</param>
    /// <param name="exports">Power exported from the region, in MW, metered at the sending end.</param>
    /// <param name="stateOfChargeByTechnology">Storage energy level by technology at the start of each interval, or null for a region without storage.</param>
    /// <param name="demandProfile">
    /// The originating demand profile, used to cross-check composed demand against
    /// <paramref name="demand"/>. Optional for backward-compatible construction paths.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A per-fleet dictionary's technology keys are inconsistent with the others, or the dispatch
    /// identity (generation, storage, imports, unserved balanced against demand, charge, exports,
    /// curtailment) fails to close at some interval.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The resolution is not hourly, demand does not match the demand profile's composed total,
    /// curtailment and unserved demand coexist at the same interval, or any series contains a
    /// negative value where the invariant forbids it.
    /// </exception>
    public DispatchOutcome(
        string regionId,
        IReadOnlyDictionary<GenerationTechnology, FlowSeries> perFleetGeneration,
        IReadOnlyDictionary<GenerationTechnology, FlowSeries> perFleetCurtailment,
        IReadOnlyDictionary<GenerationTechnology, FlowSeries> perFleetDelivered,
        IReadOnlyDictionary<GenerationTechnology, FlowSeries> perFleetCharge,
        FlowSeries demand,
        FlowSeries unserved,
        FlowSeries charge,
        FlowSeries discharge,
        FlowSeries imports,
        FlowSeries exports,
        IReadOnlyDictionary<StorageTechnology, StockSeries>? stateOfChargeByTechnology = null,
        DemandProfile? demandProfile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        ArgumentNullException.ThrowIfNull(perFleetGeneration);
        ArgumentNullException.ThrowIfNull(perFleetCurtailment);
        ArgumentNullException.ThrowIfNull(perFleetDelivered);
        ArgumentNullException.ThrowIfNull(perFleetCharge);
        ArgumentNullException.ThrowIfNull(demand);
        ArgumentNullException.ThrowIfNull(unserved);
        ArgumentNullException.ThrowIfNull(charge);
        ArgumentNullException.ThrowIfNull(discharge);
        ArgumentNullException.ThrowIfNull(imports);
        ArgumentNullException.ThrowIfNull(exports);

        if (perFleetGeneration.Values.Any(flow => flow is null))
        {
            throw new ArgumentException(
                "Generation cannot contain a null flow.",
                nameof(perFleetGeneration));
        }

        if (perFleetCurtailment.Values.Any(flow => flow is null))
        {
            throw new ArgumentException(
                "Curtailment cannot contain a null flow.",
                nameof(perFleetCurtailment));
        }

        if (perFleetDelivered.Values.Any(flow => flow is null))
        {
            throw new ArgumentException(
                "Delivered flows cannot contain a null flow.",
                nameof(perFleetDelivered));
        }

        if (perFleetCharge.Values.Any(flow => flow is null))
        {
            throw new ArgumentException(
                "Charge flows cannot contain a null flow.",
                nameof(perFleetCharge));
        }

        if (!perFleetGeneration.Keys.ToHashSet().SetEquals(perFleetCurtailment.Keys)
            || !perFleetGeneration.Keys.ToHashSet().SetEquals(perFleetDelivered.Keys)
            || !perFleetGeneration.Keys.ToHashSet().SetEquals(perFleetCharge.Keys))
        {
            throw new ArgumentException(
                "Per-fleet dispatch flows must contain the same generation technology keys.");
        }

        RegionId = regionId;
        PerFleetGeneration = new ReadOnlyDictionary<GenerationTechnology, FlowSeries>(
            new Dictionary<GenerationTechnology, FlowSeries>(perFleetGeneration));
        PerFleetCurtailment = new ReadOnlyDictionary<GenerationTechnology, FlowSeries>(
            new Dictionary<GenerationTechnology, FlowSeries>(perFleetCurtailment));
        PerFleetDelivered = new ReadOnlyDictionary<GenerationTechnology, FlowSeries>(
            new Dictionary<GenerationTechnology, FlowSeries>(perFleetDelivered));
        PerFleetCharge = new ReadOnlyDictionary<GenerationTechnology, FlowSeries>(
            new Dictionary<GenerationTechnology, FlowSeries>(perFleetCharge));
        DemandProfile = demandProfile;
        Demand = demand;
        NativeDemand = demandProfile?.BaseDemand ?? demand;
        Unserved = unserved;
        DeliveredToLoad = Demand.Subtract(Unserved);
        Charge = charge;
        Discharge = discharge;
        Imports = imports;
        Exports = exports;
        Curtailment = SumFlows(perFleetCurtailment.Values, demand);
        StateOfChargeByTechnology = new ReadOnlyDictionary<StorageTechnology, StockSeries>(
            new Dictionary<StorageTechnology, StockSeries>(stateOfChargeByTechnology
                ?? new Dictionary<StorageTechnology, StockSeries>()));

        Validate();
        Reliability = ReliabilityMetrics.FromOutcome(this);
        RenewableShare = RenewableShareMetrics.FromOutcome(this);
    }

    private static FlowSeries SumFlows(IEnumerable<FlowSeries> flows, FlowSeries timeline)
    {
        var values = new double[timeline.Length];
        foreach (FlowSeries flow in flows)
        {
            timeline.RequireAligned(flow);
            for (int index = 0; index < values.Length; index++)
            {
                values[index] += flow[index].Megawatts;
            }
        }

        return new FlowSeries(timeline.Start, timeline.Resolution, values);
    }

    private void Validate()
    {
        if (Demand.Resolution != TimeSpan.FromHours(1))
        {
            throw new ArgumentException(
                "Dispatch outcomes must use hourly resolution.",
                nameof(Demand));
        }

        Demand.RequireAligned(Unserved);
        Demand.RequireAligned(Charge);
        Demand.RequireAligned(Discharge);
        Demand.RequireAligned(Imports);
        Demand.RequireAligned(Exports);
        if (DemandProfile is not null)
        {
            DemandProfile.TotalDemand.RequireAligned(Demand);
        }

        if (!PerFleetGeneration.Keys.ToHashSet().SetEquals(PerFleetCurtailment.Keys)
            || !PerFleetGeneration.Keys.ToHashSet().SetEquals(PerFleetDelivered.Keys)
            || !PerFleetGeneration.Keys.ToHashSet().SetEquals(PerFleetCharge.Keys))
        {
            throw new ArgumentException(
                "Per-fleet dispatch flows must contain the same generation technology keys.");
        }

        foreach (FlowSeries generation in PerFleetGeneration.Values)
        {
            Demand.RequireAligned(generation);
        }

        foreach (FlowSeries curtailment in PerFleetCurtailment.Values)
        {
            Demand.RequireAligned(curtailment);
        }

        foreach (FlowSeries delivered in PerFleetDelivered.Values)
        {
            Demand.RequireAligned(delivered);
        }

        foreach (FlowSeries fleetCharge in PerFleetCharge.Values)
        {
            Demand.RequireAligned(fleetCharge);
        }

        foreach (StockSeries stateOfCharge in StateOfChargeByTechnology.Values)
        {
            Demand.RequireAligned(stateOfCharge);
        }

        GenerationTechnology[] technologies = PerFleetGeneration.Keys.ToArray();
        for (int index = 0; index < Demand.Length; index++)
        {
            double generation = 0;
            double allocatedDelivered = 0;
            double allocatedCharge = 0;
            foreach (GenerationTechnology technology in technologies)
            {
                generation += PerFleetGeneration[technology][index].Megawatts;
                allocatedDelivered += PerFleetDelivered[technology][index].Megawatts;
                allocatedCharge += PerFleetCharge[technology][index].Megawatts;
            }

            double curtailment = Curtailment[index].Megawatts;
            double unserved = Unserved[index].Megawatts;
            double charge = Charge[index].Megawatts;
            double discharge = Discharge[index].Megawatts;
            double composedDemand = DemandProfile is null
                ? Demand[index].Megawatts
                : DemandProfile.BaseDemand[index].Megawatts
                    + DemandProfile.AdditiveComponents.Sum(
                        component => component.Demand[index].Megawatts);
            double magnitude = Math.Max(
                1,
                Math.Max(
                    Math.Abs(generation),
                    Math.Max(Math.Abs(composedDemand), Math.Abs(curtailment))));
            double tolerance = BalanceTolerance * magnitude;

            if (Math.Abs(Demand[index].Megawatts - composedDemand) > tolerance)
            {
                throw new InvalidOperationException(
                    $"Dispatch demand does not match composed demand at index {index} "
                    + $"({Demand.InstantAt(index):o}).");
            }

            if (Math.Abs(composedDemand - (DeliveredToLoad[index].Megawatts + unserved)) > tolerance)
            {
                throw new InvalidOperationException(
                    $"Demand composition balance failed at index {index} "
                    + $"({Demand.InstantAt(index):o}).");
            }

            foreach (GenerationTechnology technology in technologies)
            {
                if (PerFleetCurtailment[technology][index].Megawatts < -tolerance)
                {
                    throw new InvalidOperationException(
                        $"Curtailment cannot be negative at index {index} ({Demand.InstantAt(index):o}).");
                }
            }

            if (unserved < -tolerance)
            {
                throw new InvalidOperationException(
                    $"Unserved demand cannot be negative at index {index} ({Demand.InstantAt(index):o}).");
            }

            if (charge < -tolerance || discharge < -tolerance)
            {
                throw new InvalidOperationException(
                    $"Storage charge and discharge cannot be negative at index {index} "
                    + $"({Demand.InstantAt(index):o}).");
            }

            if (curtailment > tolerance && unserved > tolerance)
            {
                throw new InvalidOperationException(
                    $"Curtailment and unserved demand cannot coexist at index {index} ({Demand.InstantAt(index):o}).");
            }

            foreach (GenerationTechnology technology in technologies)
            {
                double fleetGeneration = PerFleetGeneration[technology][index].Megawatts;
                double fleetOutputs = PerFleetCurtailment[technology][index].Megawatts
                    + PerFleetCharge[technology][index].Megawatts
                    + PerFleetDelivered[technology][index].Megawatts;
                if (Math.Abs(fleetGeneration - fleetOutputs) > tolerance)
                {
                    throw new InvalidOperationException(
                        $"Per-fleet energy balance failed at index {index} ({Demand.InstantAt(index):o}).");
                }
            }

            double generatorDelivered = generation - curtailment - charge;
            if (Math.Abs(allocatedDelivered - generatorDelivered) > tolerance
                || Math.Abs(allocatedCharge - charge) > tolerance)
            {
                throw new InvalidOperationException(
                    $"Per-fleet allocation closure failed at index {index} ({Demand.InstantAt(index):o}).");
            }

            double inputs = generation
                + Discharge[index].Megawatts
                + Imports[index].Megawatts
                + unserved;
            double outputs = composedDemand
                + Charge[index].Megawatts
                + Exports[index].Megawatts
                + curtailment;
            tolerance = BalanceTolerance * Math.Max(
                1,
                Math.Max(Math.Abs(inputs), Math.Abs(outputs)));

            if (Math.Abs(inputs - outputs) > tolerance)
            {
                throw new InvalidOperationException(
                    $"Energy balance failed at index {index} ({Demand.InstantAt(index):o}): "
                    + $"inputs were {inputs} MW and outputs were {outputs} MW.");
            }
        }
    }
}