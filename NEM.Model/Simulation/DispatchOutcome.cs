using System.Collections.ObjectModel;
using NEM.Model.Grid;
using NEM.Model.Series;

namespace NEM.Model.Simulation;

public sealed record DispatchOutcome
{
    private const double BalanceTolerance = 1e-9;

    public string RegionId { get; }
    public IReadOnlyDictionary<GenerationTechnology, FlowSeries> PerFleetGeneration { get; }
    public IReadOnlyDictionary<GenerationTechnology, FlowSeries> PerFleetCurtailment { get; }
    public FlowSeries Demand { get; }
    public FlowSeries Charge { get; }
    public FlowSeries SurplusCharge { get; }
    public FlowSeries IncrementalGenerationCharge { get; }
    public FlowSeries Discharge { get; }
    public FlowSeries Imports { get; }
    public FlowSeries Exports { get; }
    /// <summary>Total non-negative magnitude of available generation constrained off.</summary>
    public FlowSeries Curtailment { get; }
    public FlowSeries Unserved { get; }
    public ReliabilityMetrics Reliability { get; }

    public DispatchOutcome(
        string regionId,
        IReadOnlyDictionary<GenerationTechnology, FlowSeries> perFleetGeneration,
        IReadOnlyDictionary<GenerationTechnology, FlowSeries> perFleetCurtailment,
        FlowSeries demand,
        FlowSeries unserved,
        FlowSeries surplusCharge,
        FlowSeries discharge,
        FlowSeries imports,
        FlowSeries exports,
        FlowSeries? incrementalGenerationCharge = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        ArgumentNullException.ThrowIfNull(perFleetGeneration);
        ArgumentNullException.ThrowIfNull(perFleetCurtailment);
        ArgumentNullException.ThrowIfNull(demand);
        ArgumentNullException.ThrowIfNull(unserved);
        ArgumentNullException.ThrowIfNull(surplusCharge);
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

        RegionId = regionId;
        PerFleetGeneration = new ReadOnlyDictionary<GenerationTechnology, FlowSeries>(
            new Dictionary<GenerationTechnology, FlowSeries>(perFleetGeneration));
        PerFleetCurtailment = new ReadOnlyDictionary<GenerationTechnology, FlowSeries>(
            new Dictionary<GenerationTechnology, FlowSeries>(perFleetCurtailment));
        Demand = demand;
        Unserved = unserved;
        SurplusCharge = surplusCharge;
        IncrementalGenerationCharge = incrementalGenerationCharge ?? ZeroFlow(demand);
        Charge = SumFlows([SurplusCharge, IncrementalGenerationCharge], demand);
        Discharge = discharge;
        Imports = imports;
        Exports = exports;
        Curtailment = SumFlows(perFleetCurtailment.Values, demand);

        Validate();
        Reliability = ReliabilityMetrics.FromOutcome(this);
    }

    private static FlowSeries ZeroFlow(FlowSeries timeline) =>
        new(timeline.Start, timeline.Resolution, new double[timeline.Length]);

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
        Demand.RequireAligned(SurplusCharge);
        Demand.RequireAligned(IncrementalGenerationCharge);
        Demand.RequireAligned(Discharge);
        Demand.RequireAligned(Imports);
        Demand.RequireAligned(Exports);

        if (!PerFleetGeneration.Keys.ToHashSet().SetEquals(PerFleetCurtailment.Keys))
        {
            throw new ArgumentException(
                "Generation and curtailment must contain the same generation technology keys.");
        }

        foreach (FlowSeries generation in PerFleetGeneration.Values)
        {
            Demand.RequireAligned(generation);
        }

        foreach (FlowSeries curtailment in PerFleetCurtailment.Values)
        {
            Demand.RequireAligned(curtailment);
        }

        FlowSeries[] generationFlows = PerFleetGeneration.Values.ToArray();
        FlowSeries[] curtailmentFlows = PerFleetCurtailment.Values.ToArray();
        for (int index = 0; index < Demand.Length; index++)
        {
            double generation = 0;
            for (int fleetIndex = 0; fleetIndex < generationFlows.Length; fleetIndex++)
            {
                generation += generationFlows[fleetIndex][index].Megawatts;
            }

            double curtailment = Curtailment[index].Megawatts;
            double unserved = Unserved[index].Megawatts;
            double charge = Charge[index].Megawatts;
            double surplusCharge = SurplusCharge[index].Megawatts;
            double incrementalGenerationCharge = IncrementalGenerationCharge[index].Megawatts;
            double discharge = Discharge[index].Megawatts;
            double magnitude = Math.Max(
                1,
                Math.Max(
                    Math.Abs(generation),
                    Math.Max(Math.Abs(Demand[index].Megawatts), Math.Abs(curtailment))));
            double tolerance = BalanceTolerance * magnitude;

            for (int fleetIndex = 0; fleetIndex < curtailmentFlows.Length; fleetIndex++)
            {
                if (curtailmentFlows[fleetIndex][index].Megawatts < -tolerance)
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

            if (surplusCharge < -tolerance
                || incrementalGenerationCharge < -tolerance
                || charge < -tolerance
                || discharge < -tolerance)
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

            double inputs = generation
                + Discharge[index].Megawatts
                + Imports[index].Megawatts
                + unserved;
            double outputs = Demand[index].Megawatts
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