using System.Collections.ObjectModel;
using NEM.Model.Grid;
using NEM.Model.Series;

namespace NEM.Model.Simulation;

/// <summary>
/// Immutable dispatch evidence aggregated across every region in a <see cref="PowerSystem"/>.
/// Regional imports and exports must be zero until inter-regional dispatch is modelled.
/// </summary>
public sealed class SystemDispatchOutcome
{
    private const double BalanceTolerance = 1e-9;

    private SystemDispatchOutcome(
        PowerSystem powerSystem,
        IReadOnlyList<DispatchOutcome> regionalOutcomes,
        FlowSeries demand,
        IReadOnlyDictionary<GenerationTechnology, FlowSeries> perFleetGeneration,
        IReadOnlyDictionary<GenerationTechnology, FlowSeries> perFleetCurtailment,
        IReadOnlyDictionary<GenerationTechnology, FlowSeries> perFleetDelivered,
        IReadOnlyDictionary<GenerationTechnology, FlowSeries> perFleetCharge,
        FlowSeries charge,
        FlowSeries discharge,
        IReadOnlyDictionary<StorageTechnology, StockSeries> stateOfChargeByTechnology,
        FlowSeries unserved)
    {
        PowerSystemId = powerSystem.Id;
        RegionalOutcomes = Array.AsReadOnly(regionalOutcomes.ToArray());
        Demand = demand;
        Start = demand.Start;
        Resolution = demand.Resolution;
        Length = demand.Length;
        PerFleetGeneration = ReadOnly(perFleetGeneration);
        PerFleetCurtailment = ReadOnly(perFleetCurtailment);
        PerFleetDelivered = ReadOnly(perFleetDelivered);
        PerFleetCharge = ReadOnly(perFleetCharge);
        Charge = charge;
        Discharge = discharge;
        StateOfChargeByTechnology = new ReadOnlyDictionary<StorageTechnology, StockSeries>(
            new Dictionary<StorageTechnology, StockSeries>(stateOfChargeByTechnology));
        Unserved = unserved;
        DeliveredToLoad = demand.Subtract(unserved);

        ValidateEnergyIdentity();
        FlowSeries reliabilityZero = ZeroFlow();
        var reliabilityDelivered = new Dictionary<GenerationTechnology, FlowSeries>
        {
            [GenerationTechnology.Coal] = DeliveredToLoad,
        };
        var reliabilityZeroByTechnology = new Dictionary<GenerationTechnology, FlowSeries>
        {
            [GenerationTechnology.Coal] = reliabilityZero,
        };
        Reliability = ReliabilityMetrics.FromOutcome(new DispatchOutcome(
            "SYSTEM",
            reliabilityDelivered,
            reliabilityZeroByTechnology,
            reliabilityDelivered,
            reliabilityZeroByTechnology,
            Demand,
            Unserved,
            reliabilityZero,
            reliabilityZero,
            reliabilityZero,
            reliabilityZero));
    }

    /// <summary>Aggregates exactly one aligned regional dispatch outcome per system region.</summary>
    public static SystemDispatchOutcome Create(
        PowerSystem powerSystem,
        IReadOnlyList<DispatchOutcome> dispatchOutcomes)
    {
        ArgumentNullException.ThrowIfNull(powerSystem);
        ArgumentNullException.ThrowIfNull(dispatchOutcomes);

        var outcomesByRegion = new Dictionary<string, DispatchOutcome>(StringComparer.OrdinalIgnoreCase);
        foreach (DispatchOutcome outcome in dispatchOutcomes)
        {
            if (outcome is null)
            {
                throw new ArgumentException("Dispatch outcomes cannot contain null.", nameof(dispatchOutcomes));
            }

            if (!outcomesByRegion.TryAdd(outcome.RegionId, outcome))
            {
                throw new ArgumentException(
                    $"Dispatch outcomes contain duplicate region '{outcome.RegionId}'.",
                    nameof(dispatchOutcomes));
            }
        }

        var systemRegionsById = powerSystem.Regions.ToDictionary(
            region => region.RegionId,
            StringComparer.OrdinalIgnoreCase);
        foreach ((string regionId, DispatchOutcome outcome) in outcomesByRegion)
        {
            if (!systemRegionsById.TryGetValue(regionId, out Region? region))
            {
                throw new ArgumentException(
                    $"Dispatch outcome identifies unknown region '{outcome.RegionId}'.",
                    nameof(dispatchOutcomes));
            }

            RequireAligned(region.Demand.TotalDemand, outcome.Demand, regionId, nameof(dispatchOutcomes));
            RequireZeroBoundaryFlows(outcome, nameof(dispatchOutcomes));
        }

        foreach (string regionId in systemRegionsById.Keys)
        {
            if (!outcomesByRegion.ContainsKey(regionId))
            {
                throw new ArgumentException(
                    $"Dispatch outcomes are missing region '{regionId}'.",
                    nameof(dispatchOutcomes));
            }
        }

        DispatchOutcome reference = outcomesByRegion.Values.First();
        if (reference.Demand.Resolution != TimeSpan.FromHours(1))
        {
            throw new ArgumentException(
                "System dispatch outcomes must use hourly resolution.",
                nameof(dispatchOutcomes));
        }

        foreach (DispatchOutcome outcome in outcomesByRegion.Values)
        {
            RequireAligned(reference.Demand, outcome.Demand, outcome.RegionId, nameof(dispatchOutcomes));
        }

        DispatchOutcome[] orderedOutcomes = powerSystem.Regions
            .Select(region => outcomesByRegion[region.RegionId])
            .ToArray();
        FlowSeries demand = SumFlows(orderedOutcomes.Select(outcome => outcome.Demand), reference.Demand);
        return new SystemDispatchOutcome(
            powerSystem,
            orderedOutcomes,
            demand,
            SumByTechnology(orderedOutcomes, outcome => outcome.PerFleetGeneration, reference.Demand),
            SumByTechnology(orderedOutcomes, outcome => outcome.PerFleetCurtailment, reference.Demand),
            SumByTechnology(orderedOutcomes, outcome => outcome.PerFleetDelivered, reference.Demand),
            SumByTechnology(orderedOutcomes, outcome => outcome.PerFleetCharge, reference.Demand),
            SumFlows(orderedOutcomes.Select(outcome => outcome.Charge), reference.Demand),
            SumFlows(orderedOutcomes.Select(outcome => outcome.Discharge), reference.Demand),
            SumStocksByTechnology(orderedOutcomes, reference.Demand),
            SumFlows(orderedOutcomes.Select(outcome => outcome.Unserved), reference.Demand));
    }

    public PowerSystemId PowerSystemId { get; }
    public DateTimeOffset Start { get; }
    public TimeSpan Resolution { get; }
    public int Length { get; }
    public IReadOnlyList<DispatchOutcome> RegionalOutcomes { get; }
    public FlowSeries Demand { get; }
    public IReadOnlyDictionary<GenerationTechnology, FlowSeries> PerFleetGeneration { get; }
    public IReadOnlyDictionary<GenerationTechnology, FlowSeries> PerFleetCurtailment { get; }
    public IReadOnlyDictionary<GenerationTechnology, FlowSeries> PerFleetDelivered { get; }
    public IReadOnlyDictionary<GenerationTechnology, FlowSeries> PerFleetCharge { get; }
    public FlowSeries Charge { get; }
    public FlowSeries Discharge { get; }
    public IReadOnlyDictionary<StorageTechnology, StockSeries> StateOfChargeByTechnology { get; }
    public FlowSeries Unserved { get; }
    public FlowSeries DeliveredToLoad { get; }
    public ReliabilityMetrics Reliability { get; }

    private static IReadOnlyDictionary<GenerationTechnology, FlowSeries> ReadOnly(
        IReadOnlyDictionary<GenerationTechnology, FlowSeries> flows) =>
        new ReadOnlyDictionary<GenerationTechnology, FlowSeries>(
            new Dictionary<GenerationTechnology, FlowSeries>(flows));

    private static IReadOnlyDictionary<GenerationTechnology, FlowSeries> SumByTechnology(
        IEnumerable<DispatchOutcome> outcomes,
        Func<DispatchOutcome, IReadOnlyDictionary<GenerationTechnology, FlowSeries>> selector,
        FlowSeries timeline)
    {
        GenerationTechnology[] technologies = outcomes.SelectMany(outcome => selector(outcome).Keys)
            .Distinct()
            .ToArray();
        return technologies.ToDictionary(
            technology => technology,
            technology => SumFlows(
                outcomes.Where(outcome => selector(outcome).ContainsKey(technology))
                    .Select(outcome => selector(outcome)[technology]),
                timeline));
    }

    private static IReadOnlyDictionary<StorageTechnology, StockSeries> SumStocksByTechnology(
        IEnumerable<DispatchOutcome> outcomes,
        FlowSeries timeline)
    {
        StorageTechnology[] technologies = outcomes.SelectMany(outcome => outcome.StateOfChargeByTechnology.Keys)
            .Distinct()
            .ToArray();
        return technologies.ToDictionary(
            technology => technology,
            technology => new StockSeries(
                timeline.Start,
                timeline.Resolution,
                SumValues(
                    outcomes.Where(outcome => outcome.StateOfChargeByTechnology.ContainsKey(technology))
                        .Select(outcome => outcome.StateOfChargeByTechnology[technology]),
                    timeline)));
    }

    private static FlowSeries SumFlows(IEnumerable<FlowSeries> flows, FlowSeries timeline) =>
        new(timeline.Start, timeline.Resolution, SumValues(flows, timeline));

    private static double[] SumValues(IEnumerable<TimeSeries> series, FlowSeries timeline)
    {
        var values = new double[timeline.Length];
        foreach (TimeSeries value in series)
        {
            RequireAligned(timeline, value, "system", nameof(series));
            for (int index = 0; index < values.Length; index++)
            {
                values[index] += value switch
                {
                    FlowSeries flow => flow[index].Megawatts,
                    StockSeries stock => stock[index].MegawattHours,
                    _ => throw new ArgumentException("Unsupported system dispatch series.", nameof(series)),
                };
            }
        }

        return values;
    }

    private static void RequireAligned(
        TimeSeries expected,
        TimeSeries actual,
        string regionId,
        string parameterName)
    {
        try
        {
            expected.RequireAligned(actual);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                $"Dispatch timeline does not match region '{regionId}'.",
                parameterName,
                exception);
        }
    }

    private static void RequireZeroBoundaryFlows(DispatchOutcome outcome, string parameterName)
    {
        for (int index = 0; index < outcome.Demand.Length; index++)
        {
            if (outcome.Imports[index].Megawatts != 0 || outcome.Exports[index].Megawatts != 0)
            {
                throw new ArgumentException(
                    $"Dispatch outcome for region '{outcome.RegionId}' has nonzero imports or exports at the system boundary.",
                    parameterName);
            }
        }
    }

    private FlowSeries ZeroFlow() => new(Start, Resolution, new double[Length]);

    private void ValidateEnergyIdentity()
    {
        for (int index = 0; index < Length; index++)
        {
            double generation = PerFleetGeneration.Values.Sum(flow => flow[index].Megawatts);
            double curtailment = PerFleetCurtailment.Values.Sum(flow => flow[index].Megawatts);
            double inputs = generation + Discharge[index].Megawatts + Unserved[index].Megawatts;
            double outputs = Demand[index].Megawatts + Charge[index].Megawatts + curtailment;
            double tolerance = BalanceTolerance * Math.Max(1, Math.Max(Math.Abs(inputs), Math.Abs(outputs)));
            if (Math.Abs(inputs - outputs) > tolerance)
            {
                throw new InvalidOperationException(
                    $"System energy balance failed at index {index} ({Demand.InstantAt(index):o}): "
                    + $"inputs were {inputs} MW and outputs were {outputs} MW.");
            }
        }
    }
}