using System.Collections.ObjectModel;
using NEM.Model.Grid;
using NEM.Model.Series;

namespace NEM.Model.Simulation
{
    public sealed record DispatchOutcome
    {
        public string RegionId { get; }
        public IReadOnlyDictionary<TechnologyKey, FlowSeries> PerFleetGeneration { get; }
        // public FlowSeries ChargeMw { get; } // TODO: Set when battery
        // public FlowSeries DischargeMw { get; } // TODO: Set when battery
        // public FlowSeries ImportsMw{ get; } // TODO: Set when multiple states
        // public FlowSeries ExportsMw { get; } // TODO: Set when multiple states
        /// <summary>Curtailment as a non-positive flow, distinguishing wasted energy from generation.</summary>
        public FlowSeries CurtailmentMw { get; }
        public FlowSeries UnservedMw { get; }
        public DispatchOutcome(
            string regionId,
            IReadOnlyDictionary<TechnologyKey, FlowSeries> perFleetGeneration,
            FlowSeries curtailmentMw,
            FlowSeries unservedMw)
        {
            RegionId = regionId;
            PerFleetGeneration = new ReadOnlyDictionary<TechnologyKey, FlowSeries>(
                new Dictionary<TechnologyKey, FlowSeries>(perFleetGeneration));
            CurtailmentMw = curtailmentMw;
            UnservedMw = unservedMw;
        }
    }

    public static class Dispatcher
    {
        public static DispatchOutcome Dispatch(Region region)
        {
            ArgumentNullException.ThrowIfNull(region);

            var start = region.Demand.TotalDemand.Start;
            var resolution = region.Demand.TotalDemand.Resolution;

            // Initialise starting balances for curtailment and unserved.
            var curtailmentMw = new FlowSeries(start, resolution, new double [region.Demand.TotalDemand.Length]);
            var unservedMw = region.Demand.TotalDemand;
            var perFleetGeneration = new Dictionary<TechnologyKey, FlowSeries>();

            // NEM-013: Implementation of Dispatch Order (Crude):
            foreach (var fleet in region.Fleets.OrderBy(f => f.ShortRunMarginalCost))
            {
                FlowSeries availableCapacity = fleet.AvailableCapacityFor(
                    region.ResourceProfile,
                    region.Demand.TotalDemand);
                var balance = unservedMw.Subtract(availableCapacity);
                var remainingUnservedMw = balance.PositivePart();
                var candidateGeneration = unservedMw.Subtract(remainingUnservedMw);
                var fleetGeneration = fleet.ApplyEnergyBudget(candidateGeneration);

                perFleetGeneration.Add(fleet.TechnologyKey, fleetGeneration);
                unservedMw = unservedMw.Subtract(fleetGeneration);
                if (fleet.IsIntermittentRenewable)
                {
                    var curtailedGenerationMw = balance.NegativePart();
                    curtailmentMw = curtailmentMw.Add(curtailedGenerationMw);
                }
            }

            return new DispatchOutcome(region.RegionId, perFleetGeneration, curtailmentMw, unservedMw);
        }
    }
}