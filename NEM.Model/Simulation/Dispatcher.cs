using System.Collections.ObjectModel;
using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Units;

namespace NEM.Model.Simulation
{
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
        public FlowSeries Imports { get; } // TODO: Set when multiple states
        public FlowSeries Exports { get; } // TODO: Set when multiple states
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

    public static class Dispatcher
    {
        public static IReadOnlyList<DispatchOutcome> Dispatch(PowerSystem powerSystem) =>
            Dispatch(powerSystem, new GreedyPolicy());

        public static IReadOnlyList<DispatchOutcome> Dispatch(
            PowerSystem powerSystem,
            IStoragePolicy storagePolicy)
        {
            ArgumentNullException.ThrowIfNull(powerSystem);
            ArgumentNullException.ThrowIfNull(storagePolicy);

            return Array.AsReadOnly(powerSystem.Regions
                .Select(region => DispatchRegion(region, storagePolicy))
                .ToArray());
        }

        private static DispatchOutcome DispatchRegion(Region region, IStoragePolicy storagePolicy)
        {
            FlowSeries demand = region.Demand.TotalDemand;
            TimeSpan resolution = demand.Resolution;
            GeneratingFleet[] generatingFleets = region.GeneratingFleets
                .OrderBy(fleet => fleet.ShortRunMarginalCost)
                .ToArray();
            var availableByTechnology = generatingFleets.ToDictionary(
                fleet => fleet.GenerationTechnology,
                fleet => fleet.AvailableCapacityFor(region.ResourceProfile, demand));
            var budgetByTechnology = generatingFleets.ToDictionary(
                fleet => fleet.GenerationTechnology,
                fleet => fleet.CreateEnergyBudget());
            var generationMwByTechnology = generatingFleets.ToDictionary(
                fleet => fleet.GenerationTechnology,
                _ => new double[demand.Length]);
            var curtailmentMwByTechnology = generatingFleets.ToDictionary(
                fleet => fleet.GenerationTechnology,
                _ => new double[demand.Length]);
            var storageByTechnology = region.StorageFleets.ToDictionary(
                fleet => fleet.StorageTechnology);
            var storageLevelByTechnology = region.StorageFleets.ToDictionary(
                fleet => fleet.StorageTechnology,
                _ => Energy.Zero);
            var unservedMw = new double[demand.Length];
            var surplusChargeMw = new double[demand.Length];
            var incrementalGenerationChargeMw = new double[demand.Length];
            var dischargeMw = new double[demand.Length];

            for (int index = 0; index < demand.Length; index++)
            {
                DateTimeOffset instant = demand.InstantAt(index);
                Power remainingDemand = demand[index];

                foreach (GeneratingFleet fleet in generatingFleets)
                {
                    GenerationTechnology technology = fleet.GenerationTechnology;
                    Power available = availableByTechnology[technology][index];
                    GenerationEnergyBudget budget = budgetByTechnology[technology];
                    Power candidate = Power.Min(
                        Power.Max(Power.Zero, remainingDemand),
                        budget.Headroom(available, Power.Zero, instant, resolution));
                    Power delivered = budget.Take(candidate, instant, resolution);

                    generationMwByTechnology[technology][index] = fleet.IsIntermittentRenewable
                        ? available.Megawatts
                        : delivered.Megawatts;
                    curtailmentMwByTechnology[technology][index] = fleet.IsIntermittentRenewable
                        ? available.Megawatts - delivered.Megawatts
                        : 0;
                    remainingDemand -= delivered;
                }

                Power surplus = Power.FromMegawatts(
                    curtailmentMwByTechnology.Values.Sum(values => values[index]));
                Power residual = remainingDemand > Power.Zero
                    ? remainingDemand
                    : surplus * -1;
                StorageFleetSnapshot[] storageSnapshots = region.StorageFleets
                    .Select(fleet => new StorageFleetSnapshot(
                        fleet.StorageTechnology,
                        storageLevelByTechnology[fleet.StorageTechnology],
                        fleet.ChargeHeadroom(
                            storageLevelByTechnology[fleet.StorageTechnology],
                            resolution),
                        fleet.DischargeHeadroom(
                            storageLevelByTechnology[fleet.StorageTechnology],
                            resolution)))
                    .ToArray();
                GenerationFleetSnapshot[] generationSnapshots = generatingFleets
                    .Select(fleet => new GenerationFleetSnapshot(
                        fleet.GenerationTechnology,
                        fleet.IsIntermittentRenewable
                            ? Power.Zero
                            : budgetByTechnology[fleet.GenerationTechnology].Headroom(
                                availableByTechnology[fleet.GenerationTechnology][index],
                                Power.FromMegawatts(
                                    generationMwByTechnology[fleet.GenerationTechnology][index]),
                                instant,
                                resolution)))
                    .ToArray();
                var context = new DispatchContext(
                    residual,
                    storageSnapshots,
                    generationSnapshots,
                    resolution);
                StorageDecision decision = storagePolicy.Decide(context)
                    ?? throw new InvalidOperationException("Storage policy returned no decision.");

                Power remainingDeficit = Power.Max(Power.Zero, remainingDemand);
                Power remainingSurplus = surplus;
                foreach (StorageIntent intent in decision.Intents)
                {
                    if (!storageByTechnology.TryGetValue(intent.StorageTechnology, out StorageFleet? fleet))
                    {
                        throw new InvalidOperationException(
                            $"Storage policy returned an intent for unknown fleet {intent.StorageTechnology}.");
                    }

                    Energy storageLevel = storageLevelByTechnology[intent.StorageTechnology];
                    if (intent.RequestedFlow > Power.Zero)
                    {
                        Power requested = Power.Min(intent.RequestedFlow, remainingDeficit);
                        if (requested == Power.Zero)
                        {
                            continue;
                        }

                        StorageOutcome outcome = fleet.Operate(storageLevel, requested, resolution);
                        storageLevelByTechnology[intent.StorageTechnology] = outcome.FinalStorageLevel;
                        remainingDeficit -= outcome.DeliveredFlow;
                        dischargeMw[index] += outcome.DeliveredFlow.Megawatts;
                        continue;
                    }

                    if (remainingDeficit > Power.Zero)
                    {
                        continue;
                    }

                    Power requestedCharge = intent.RequestedFlow * -1;
                    if (intent.ChargeSource == ChargeSource.Surplus)
                    {
                        requestedCharge = Power.Min(requestedCharge, remainingSurplus);
                        if (requestedCharge == Power.Zero)
                        {
                            continue;
                        }

                        StorageOutcome outcome = fleet.Operate(
                            storageLevel,
                            requestedCharge * -1,
                            resolution);
                        Power actualCharge = outcome.DeliveredFlow * -1;
                        storageLevelByTechnology[intent.StorageTechnology] = outcome.FinalStorageLevel;
                        remainingSurplus -= actualCharge;
                        surplusChargeMw[index] += actualCharge.Megawatts;
                        ReduceCurtailment(
                            generatingFleets,
                            curtailmentMwByTechnology,
                            index,
                            actualCharge);
                        continue;
                    }

                    GenerationTechnology sourceTechnology = intent.ChargeSource!.Value.GenerationTechnology
                        ?? throw new InvalidOperationException(
                            "Incremental-generation charging must identify a generation technology.");
                    GeneratingFleet? sourceFleet = generatingFleets.SingleOrDefault(
                        candidate => candidate.GenerationTechnology == sourceTechnology);
                    if (sourceFleet is null)
                    {
                        throw new InvalidOperationException(
                            $"Storage policy named unknown generation source {sourceTechnology}.");
                    }

                    Power generated = Power.FromMegawatts(
                        generationMwByTechnology[sourceTechnology][index]);
                    Power sourceHeadroom = sourceFleet.IsIntermittentRenewable
                        ? Power.Zero
                        : budgetByTechnology[sourceTechnology].Headroom(
                            availableByTechnology[sourceTechnology][index],
                            generated,
                            instant,
                            resolution);
                    requestedCharge = Power.Min(requestedCharge, sourceHeadroom);
                    if (requestedCharge == Power.Zero)
                    {
                        continue;
                    }

                    StorageOutcome incrementalChargeOutcome = fleet.Operate(
                        storageLevel,
                        requestedCharge * -1,
                        resolution);
                    Power actualIncrementalCharge = incrementalChargeOutcome.DeliveredFlow * -1;
                    Power additionalGeneration = budgetByTechnology[sourceTechnology].Take(
                        actualIncrementalCharge,
                        instant,
                        resolution);
                    storageLevelByTechnology[intent.StorageTechnology] =
                        incrementalChargeOutcome.FinalStorageLevel;
                    generationMwByTechnology[sourceTechnology][index] += additionalGeneration.Megawatts;
                    incrementalGenerationChargeMw[index] += additionalGeneration.Megawatts;
                }

                unservedMw[index] = remainingDeficit.Megawatts;
            }

            var perFleetGeneration = generationMwByTechnology.ToDictionary(
                entry => entry.Key,
                entry => new FlowSeries(demand.Start, resolution, entry.Value));
            var perFleetCurtailment = curtailmentMwByTechnology.ToDictionary(
                entry => entry.Key,
                entry => new FlowSeries(demand.Start, resolution, entry.Value));
            var zeroFlow = new FlowSeries(demand.Start, resolution, new double[demand.Length]);
            return new DispatchOutcome(
                region.RegionId,
                perFleetGeneration,
                perFleetCurtailment,
                demand,
                new FlowSeries(demand.Start, resolution, unservedMw),
                new FlowSeries(demand.Start, resolution, surplusChargeMw),
                new FlowSeries(demand.Start, resolution, dischargeMw),
                zeroFlow,
                zeroFlow,
                new FlowSeries(demand.Start, resolution, incrementalGenerationChargeMw));
        }

        private static void ReduceCurtailment(
            IReadOnlyList<GeneratingFleet> generatingFleets,
            IReadOnlyDictionary<GenerationTechnology, double[]> curtailmentMwByTechnology,
            int index,
            Power charge)
        {
            double remainingMw = charge.Megawatts;
            foreach (GeneratingFleet fleet in generatingFleets)
            {
                double[] curtailmentMw = curtailmentMwByTechnology[fleet.GenerationTechnology];
                double reductionMw = Math.Min(remainingMw, curtailmentMw[index]);
                curtailmentMw[index] -= reductionMw;
                remainingMw -= reductionMw;
                if (remainingMw <= 0)
                {
                    return;
                }
            }
        }
    }
}