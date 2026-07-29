using System.Collections.ObjectModel;
using NEM.Model.Grid;
using NEM.Model.Series;

namespace NEM.Model.Simulation
{
    public sealed record DispatchOutcome
    {
        private const double BalanceTolerance = 1e-9;

        public string RegionId { get; }
        public IReadOnlyDictionary<TechnologyKey, FlowSeries> PerFleetGeneration { get; }
        public IReadOnlyDictionary<TechnologyKey, FlowSeries> PerFleetCurtailment { get; }
        public FlowSeries Demand { get; }
        public FlowSeries Charge { get; } // TODO: Set when battery
        public FlowSeries Discharge { get; } // TODO: Set when battery
        public FlowSeries Imports { get; } // TODO: Set when multiple states
        public FlowSeries Exports { get; } // TODO: Set when multiple states
        /// <summary>Total non-negative magnitude of available generation constrained off.</summary>
        public FlowSeries Curtailment { get; }
        public FlowSeries Unserved { get; }

        public DispatchOutcome(
            string regionId,
            IReadOnlyDictionary<TechnologyKey, FlowSeries> perFleetGeneration,
            IReadOnlyDictionary<TechnologyKey, FlowSeries> perFleetCurtailment,
            FlowSeries demand,
            FlowSeries unserved,
            FlowSeries charge,
            FlowSeries discharge,
            FlowSeries imports,
            FlowSeries exports)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
            ArgumentNullException.ThrowIfNull(perFleetGeneration);
            ArgumentNullException.ThrowIfNull(perFleetCurtailment);
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

            RegionId = regionId;
            PerFleetGeneration = new ReadOnlyDictionary<TechnologyKey, FlowSeries>(
                new Dictionary<TechnologyKey, FlowSeries>(perFleetGeneration));
            PerFleetCurtailment = new ReadOnlyDictionary<TechnologyKey, FlowSeries>(
                new Dictionary<TechnologyKey, FlowSeries>(perFleetCurtailment));
            Demand = demand;
            Unserved = unserved;
            Charge = charge;
            Discharge = discharge;
            Imports = imports;
            Exports = exports;
            Curtailment = SumFlows(perFleetCurtailment.Values, demand);

            Validate();
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

            if (!PerFleetGeneration.Keys.ToHashSet().SetEquals(PerFleetCurtailment.Keys))
            {
                throw new ArgumentException(
                    "Generation and curtailment must contain the same fleet keys.");
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

    public static class Dispatcher
    {
        public static DispatchOutcome Dispatch(Region region)
        {
            ArgumentNullException.ThrowIfNull(region);

            var start = region.Demand.TotalDemand.Start;
            var resolution = region.Demand.TotalDemand.Resolution;

            var zeroFlow = new FlowSeries(
                start,
                resolution,
                new double[region.Demand.TotalDemand.Length]);
            var unservedMw = region.Demand.TotalDemand;
            var perFleetGeneration = new Dictionary<TechnologyKey, FlowSeries>();
            var perFleetCurtailment = new Dictionary<TechnologyKey, FlowSeries>();

            // NEM-013: Implementation of Dispatch Order (Crude):
            foreach (var fleet in region.Fleets.OrderBy(f => f.ShortRunMarginalCost))
            {
                FlowSeries availableCapacity = fleet.AvailableCapacityFor(
                    region.ResourceProfile,
                    region.Demand.TotalDemand);
                var balance = unservedMw.Subtract(availableCapacity);
                var remainingUnservedMw = balance.PositivePart();
                var candidateGeneration = unservedMw.Subtract(remainingUnservedMw);
                var deliveredGeneration = fleet.ApplyEnergyBudget(candidateGeneration);

                unservedMw = unservedMw.Subtract(deliveredGeneration);
                if (fleet.IsIntermittentRenewable)
                {
                    perFleetGeneration.Add(fleet.TechnologyKey, availableCapacity);
                    perFleetCurtailment.Add(
                        fleet.TechnologyKey,
                        availableCapacity.Subtract(deliveredGeneration));
                }
                else
                {
                    perFleetGeneration.Add(fleet.TechnologyKey, deliveredGeneration);
                    perFleetCurtailment.Add(fleet.TechnologyKey, zeroFlow);
                }
            }

            return new DispatchOutcome(
                region.RegionId,
                perFleetGeneration,
                perFleetCurtailment,
                region.Demand.TotalDemand,
                unservedMw,
                zeroFlow,
                zeroFlow,
                zeroFlow,
                zeroFlow);
        }
    }
}