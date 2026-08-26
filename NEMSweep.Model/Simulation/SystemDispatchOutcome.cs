using System.Collections.ObjectModel;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Series;

namespace NEMSweep.Model.Simulation;

/// <summary>
/// Immutable dispatch evidence aggregated across every region in a <see cref="PowerSystem"/>.
/// </summary>
/// <remarks>
/// Inter-regional flows net out across the system except for what transmission losses
/// consume, so the system identity carries a losses term that the regional identity does
/// not. Nothing enters or leaves the system as a whole: every export is some other
/// region's import plus the loss on the way.
/// </remarks>
public sealed class SystemDispatchOutcome
{
    private const double BalanceTolerance = 1e-9;

    /// <summary>
    /// Caller-facing name of the outcomes parameter, so validation extracted out of
    /// <see cref="CreateCore"/> still reports the argument the caller actually passed.
    /// </summary>
    private const string DispatchOutcomesParameter = "dispatchOutcomes";

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
        FlowSeries unserved,
        FlowSeries imports,
        FlowSeries exports,
        IReadOnlyList<InterconnectorFlow> interconnectorFlows)
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
        EnergyServed = demand.Subtract(unserved);
        Imports = imports;
        Exports = exports;
        InterconnectorFlows = Array.AsReadOnly(interconnectorFlows.ToArray());
        FlowSeries derivedLosses = exports.Subtract(imports);
        FlowSeries solverLosses = SumFlows(
            interconnectorFlows.Select(flow => flow.Losses),
            demand);
        TransmissionLosses = interconnectorFlows.Count == 0 ? derivedLosses : solverLosses;

        ValidateTransmissionLosses(derivedLosses, solverLosses, interconnectorFlows);
        ValidateEnergyIdentity();
        FlowSeries reliabilityZero = ZeroFlow();
        var reliabilityServed = new Dictionary<GenerationTechnology, FlowSeries>
        {
            [GenerationTechnology.Coal] = EnergyServed,
        };
        var reliabilityZeroByTechnology = new Dictionary<GenerationTechnology, FlowSeries>
        {
            [GenerationTechnology.Coal] = reliabilityZero,
        };
        Reliability = ReliabilityMetrics.FromOutcome(new DispatchOutcome(
            "SYSTEM",
            reliabilityServed,
            reliabilityZeroByTechnology,
            reliabilityServed,
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
        if (powerSystem.Interconnectors.Count > 0)
        {
            throw new ArgumentException(
                "A linked power system must be created from solver evidence.",
                nameof(dispatchOutcomes));
        }

        return CreateCore(powerSystem, dispatchOutcomes, []);
    }

    /// <summary>Aggregates regional outcomes with the solver evidence that produced them.</summary>
    public static SystemDispatchOutcome Create(
        PowerSystem powerSystem,
        SystemDispatchRunResult dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        return CreateCore(powerSystem, dispatch.RegionalOutcomes, dispatch.InterconnectorFlows);
    }

    private static SystemDispatchOutcome CreateCore(
        PowerSystem powerSystem,
        IReadOnlyList<DispatchOutcome> dispatchOutcomes,
        IReadOnlyList<InterconnectorFlow> interconnectorFlows)
    {
        ArgumentNullException.ThrowIfNull(powerSystem);
        ArgumentNullException.ThrowIfNull(dispatchOutcomes);
        ArgumentNullException.ThrowIfNull(interconnectorFlows);

        Dictionary<string, DispatchOutcome> outcomesByRegion = IndexOutcomesByRegion(dispatchOutcomes);
        RequireOutcomesMatchSystemRegions(powerSystem, outcomesByRegion);
        DispatchOutcome reference = RequireHourlyAlignedTimelines(outcomesByRegion);

        DispatchOutcome[] orderedOutcomes = powerSystem.Regions
            .Select(region => outcomesByRegion[region.RegionId])
            .ToArray();
        RequireNoBoundaryFlowsWithoutInterconnectors(
            powerSystem,
            orderedOutcomes,
            nameof(dispatchOutcomes));
        ValidateInterconnectorFlows(powerSystem, interconnectorFlows, orderedOutcomes);
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
            SumFlows(orderedOutcomes.Select(outcome => outcome.Unserved), reference.Demand),
            SumFlows(orderedOutcomes.Select(outcome => outcome.Imports), reference.Demand),
            SumFlows(orderedOutcomes.Select(outcome => outcome.Exports), reference.Demand),
            interconnectorFlows);
    }

    /// <summary>Indexes the supplied outcomes by region, rejecting nulls and duplicates.</summary>
    private static Dictionary<string, DispatchOutcome> IndexOutcomesByRegion(
        IReadOnlyList<DispatchOutcome> dispatchOutcomes)
    {
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

        return outcomesByRegion;
    }

    /// <summary>
    /// Requires the outcomes and the system's regions to be the same set, with each outcome
    /// aligned to its own region's demand and carrying no negative boundary flow.
    /// </summary>
    private static void RequireOutcomesMatchSystemRegions(
        PowerSystem powerSystem,
        Dictionary<string, DispatchOutcome> outcomesByRegion)
    {
        var systemRegionsById = powerSystem.Regions.ToDictionary(
            region => region.RegionId,
            StringComparer.OrdinalIgnoreCase);
        foreach ((string regionId, DispatchOutcome outcome) in outcomesByRegion)
        {
            if (!systemRegionsById.TryGetValue(regionId, out Region? region))
            {
                throw new ArgumentException(
                    $"Dispatch outcome identifies unknown region '{outcome.RegionId}'.",
                    DispatchOutcomesParameter);
            }

            RequireAligned(region.Demand.TotalDemand, outcome.Demand, regionId, DispatchOutcomesParameter);
            RequireNonNegativeBoundaryFlows(outcome, DispatchOutcomesParameter);
        }

        foreach (string regionId in systemRegionsById.Keys)
        {
            if (!outcomesByRegion.ContainsKey(regionId))
            {
                throw new ArgumentException(
                    $"Dispatch outcomes are missing region '{regionId}'.",
                    DispatchOutcomesParameter);
            }
        }
    }

    /// <summary>
    /// Requires every outcome to share one hourly timeline, and returns the outcome the rest
    /// of the aggregation measures against.
    /// </summary>
    private static DispatchOutcome RequireHourlyAlignedTimelines(
        Dictionary<string, DispatchOutcome> outcomesByRegion)
    {
        DispatchOutcome reference = outcomesByRegion.Values.First();
        if (reference.Demand.Resolution != TimeSpan.FromHours(1))
        {
            throw new ArgumentException(
                "System dispatch outcomes must use hourly resolution.",
                DispatchOutcomesParameter);
        }

        foreach (DispatchOutcome outcome in outcomesByRegion.Values)
        {
            RequireAligned(reference.Demand, outcome.Demand, outcome.RegionId, DispatchOutcomesParameter);
        }

        return reference;
    }

    /// <summary>Identity of the power system this evidence describes.</summary>
    public PowerSystemId PowerSystemId { get; }

    /// <summary>First interval instant, in NEM market time (UTC+10).</summary>
    public DateTimeOffset Start { get; }

    /// <summary>Interval length. System outcomes are hourly.</summary>
    public TimeSpan Resolution { get; }

    /// <summary>Number of intervals in every series on this outcome.</summary>
    public int Length { get; }

    /// <summary>
    /// The validated regional outcomes this aggregate was built from, in system region order.
    /// Retained so a published system artifact can also disclose its regional evidence.
    /// </summary>
    public IReadOnlyList<DispatchOutcome> RegionalOutcomes { get; }

    /// <summary>Total system demand in MW, the element-wise sum of every region's total demand.</summary>
    public FlowSeries Demand { get; }

    /// <summary>
    /// Gross generation in MW by technology, summed across regions. Technologies absent from a
    /// region are zero-filled so every series covers the whole system.
    /// </summary>
    public IReadOnlyDictionary<GenerationTechnology, FlowSeries> PerFleetGeneration { get; }

    /// <summary>Curtailed generation in MW by technology, summed across regions.</summary>
    public IReadOnlyDictionary<GenerationTechnology, FlowSeries> PerFleetCurtailment { get; }

    /// <summary>
    /// Generation delivered to the grid in MW by technology: available generation less curtailment
    /// and less storage charging, summed across regions. This, not generation minus curtailment, is
    /// what published delivered-generation figures use, because generation can also be diverted to
    /// charging storage.
    /// </summary>
    public IReadOnlyDictionary<GenerationTechnology, FlowSeries> PerFleetDelivered { get; }

    /// <summary>
    /// Storage charging in MW allocated to the generation technology that supplied it. A consistent
    /// bookkeeping allocation, not a physical attribution.
    /// </summary>
    public IReadOnlyDictionary<GenerationTechnology, FlowSeries> PerFleetCharge { get; }

    /// <summary>Total power drawn from the grid to charge storage, in MW.</summary>
    public FlowSeries Charge { get; }

    /// <summary>Total power returned to the grid by storage, in MW.</summary>
    public FlowSeries Discharge { get; }

    /// <summary>
    /// Interval-beginning stored energy in MWh by storage technology, summed across regions.
    /// </summary>
    public IReadOnlyDictionary<StorageTechnology, StockSeries> StateOfChargeByTechnology { get; }

    /// <summary>Demand that could not be met, in MW. The basis of the reliability measure.</summary>
    public FlowSeries Unserved { get; }

    /// <summary>
    /// Energy served: demand less unserved demand, in MW. Integrated over the run,
    /// this is the denominator of every levelised cost the model publishes.
    /// </summary>
    public FlowSeries EnergyServed { get; }

    /// <summary>Total power received by regions from other regions, in MW, net of losses.</summary>
    public FlowSeries Imports { get; }

    /// <summary>Total power sent by regions to other regions, in MW, metered at the sending end.</summary>
    public FlowSeries Exports { get; }

    /// <summary>
    /// Power lost moving energy between regions, in MW, being the gap between what was sent and
    /// what arrived. A real sink in the system energy ledger, not a residual.
    /// </summary>
    public FlowSeries TransmissionLosses { get; }

    /// <summary>Directional solver evidence for every link in the final power system.</summary>
    public IReadOnlyList<InterconnectorFlow> InterconnectorFlows { get; }

    /// <summary>
    /// Whole-system reliability, recalculated from aggregate demand and aggregate unserved energy.
    /// It is never an average of the regional percentages, which would weight a small region the
    /// same as a large one.
    /// </summary>
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

    /// <summary>
    /// Inter-regional flows are directional quantities, so neither side may be negative.
    /// A region exporting a negative amount would be importing, and booking it in the
    /// wrong series would still satisfy the regional identity while corrupting the system
    /// loss reconciliation.
    /// </summary>
    private static void RequireNonNegativeBoundaryFlows(
        DispatchOutcome outcome,
        string parameterName)
    {
        for (int index = 0; index < outcome.Demand.Length; index++)
        {
            if (outcome.Imports[index].Megawatts < -BalanceTolerance
                || outcome.Exports[index].Megawatts < -BalanceTolerance)
            {
                throw new ArgumentException(
                    $"Dispatch outcome for region '{outcome.RegionId}' has negative imports or "
                    + $"exports at index {index}.",
                    parameterName);
            }
        }
    }

    /// <summary>
    /// Nothing enters or leaves the system as a whole, so every export must be some other
    /// region's import plus the loss incurred on the way. Losses below zero would mean
    /// energy was created in transit; losses above what was exported would mean more was
    /// lost than ever left.
    /// </summary>
    private void ValidateTransmissionLosses(
        FlowSeries derivedLosses,
        FlowSeries solverLosses,
        IReadOnlyList<InterconnectorFlow> interconnectorFlows)
    {
        for (int index = 0; index < Length; index++)
        {
            double losses = TransmissionLosses[index].Megawatts;
            double exports = Exports[index].Megawatts;
            double tolerance = BalanceTolerance * Math.Max(1, Math.Abs(exports));
            if (interconnectorFlows.Count > 0
                && Math.Abs(derivedLosses[index].Megawatts - solverLosses[index].Megawatts)
                    > tolerance)
            {
                throw new InvalidOperationException(
                    $"System transmission loss reconciliation failed at index {index} "
                    + $"({Demand.InstantAt(index):o}): exports minus imports was "
                    + $"{derivedLosses[index].Megawatts} MW but solver losses were "
                    + $"{solverLosses[index].Megawatts} MW.");
            }
            if (losses < -tolerance)
            {
                throw new InvalidOperationException(
                    $"System imports exceed exports at index {index} "
                    + $"({Demand.InstantAt(index):o}): {-losses} MW of energy would be created "
                    + "in transit.");
            }

            if (losses > exports + tolerance)
            {
                throw new InvalidOperationException(
                    $"System transmission losses exceed exports at index {index} "
                    + $"({Demand.InstantAt(index):o}): {losses} MW lost against {exports} MW "
                    + "exported.");
            }
        }
    }

    private static void RequireNoBoundaryFlowsWithoutInterconnectors(
        PowerSystem powerSystem,
        IReadOnlyList<DispatchOutcome> outcomes,
        string parameterName)
    {
        if (powerSystem.Interconnectors.Count > 0)
        {
            return;
        }

        foreach (DispatchOutcome outcome in outcomes)
        {
            for (int index = 0; index < outcome.Demand.Length; index++)
            {
                if (outcome.Imports[index].Megawatts > BalanceTolerance
                    || outcome.Exports[index].Megawatts > BalanceTolerance)
                {
                    throw new ArgumentException(
                        $"Dispatch outcome for region '{outcome.RegionId}' has a boundary flow at "
                        + $"index {index}, but the power system has no interconnectors.",
                        parameterName);
                }
            }
        }
    }

    private static void ValidateInterconnectorFlows(
        PowerSystem powerSystem,
        IReadOnlyList<InterconnectorFlow> flows,
        IReadOnlyList<DispatchOutcome> outcomes)
    {
        if (flows.Count != powerSystem.Interconnectors.Count)
        {
            throw new ArgumentException(
                "Solver evidence must contain one flow per system interconnector.",
                nameof(flows));
        }

        if (flows.Any(flow => flow is null))
        {
            throw new ArgumentException("Solver evidence cannot contain null flows.", nameof(flows));
        }

        FlowSeries timeline = outcomes[0].Demand;
        foreach ((InterconnectorFlow flow, Interconnector link) in
                 flows.Zip(powerSystem.Interconnectors))
        {
            if (!MatchesTopology(flow.Interconnector, link))
            {
                throw new ArgumentException(
                    "Solver evidence interconnectors must match the power system topology.",
                    nameof(flows));
            }

            flow.Flow.RequireAligned(timeline);
            flow.Losses.RequireAligned(timeline);
            for (int index = 0; index < timeline.Length; index++)
            {
                if (flow.Flow[index].Megawatts < -BalanceTolerance
                    || flow.Losses[index].Megawatts < -BalanceTolerance
                    || flow.Flow[index].Megawatts > link.Capacity.Megawatts + BalanceTolerance
                    || flow.Losses[index].Megawatts
                        > flow.Flow[index].Megawatts + BalanceTolerance)
                {
                    throw new ArgumentException(
                        $"Solver evidence exceeds non-negative limits for interconnector "
                        + $"'{link.FromRegionId}-{link.ToRegionId}' at index {index}.",
                        nameof(flows));
                }
            }
        }
    }

    private static bool MatchesTopology(Interconnector evidence, Interconnector topology) =>
        string.Equals(
            evidence.FromRegionId,
            topology.FromRegionId,
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            evidence.ToRegionId,
            topology.ToRegionId,
            StringComparison.OrdinalIgnoreCase)
        && evidence.Capacity == topology.Capacity;

    private FlowSeries ZeroFlow() => new(Start, Resolution, new double[Length]);

    private void ValidateEnergyIdentity()
    {
        for (int index = 0; index < Length; index++)
        {
            double generation = PerFleetGeneration.Values.Sum(flow => flow[index].Megawatts);
            double curtailment = PerFleetCurtailment.Values.Sum(flow => flow[index].Megawatts);
            double inputs = generation + Discharge[index].Megawatts + Unserved[index].Megawatts;
            double outputs = Demand[index].Megawatts
                + Charge[index].Megawatts
                + curtailment
                + TransmissionLosses[index].Megawatts;
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