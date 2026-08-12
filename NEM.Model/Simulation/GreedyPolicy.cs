using NEM.Model.Grid;
using NEM.Model.Units;

namespace NEM.Model.Simulation
{
    /// <summary>
    /// Requests immediate storage discharge for demand deficits and immediate charging from
    /// would-be-curtailed surplus generation. Unlike
    /// <see cref="GreedySurplusAndIncrementalGenerationChargingPolicy"/>, it never requests
    /// additional dispatchable generation to charge storage.
    /// </summary>
    public sealed class GreedyPolicy : IStoragePolicy
    {
        public StorageDecision Decide(DispatchContext context)
        {
            if (context.Residual == Power.Zero)
            {
                return StorageDecision.None;
            }

            Power remaining = Power.FromMegawatts(Math.Abs(context.Residual.Megawatts));
            var intents = new List<StorageIntent>();
            foreach (StorageFleetSnapshot fleet in context.StorageFleets
                         .OrderBy(fleet => PriorityFor(fleet.StorageTechnology)))
            {
                Power fleetHeadroom = context.Residual > Power.Zero
                    ? fleet.DischargeHeadroom
                    : fleet.ChargeHeadroom;
                Power requestedMagnitude = Power.Min(remaining, fleetHeadroom);
                if (requestedMagnitude == Power.Zero)
                {
                    continue;
                }

                Power requestedFlow = context.Residual > Power.Zero
                    ? requestedMagnitude
                    : requestedMagnitude * -1;
                intents.Add(new StorageIntent(
                    fleet.StorageTechnology,
                    requestedFlow,
                    context.Residual < Power.Zero ? ChargeSource.Surplus : null));
                remaining -= requestedMagnitude;
                if (remaining == Power.Zero)
                {
                    break;
                }
            }

            return intents.Count == 0 ? StorageDecision.None : new StorageDecision(intents);
        }

        private static int PriorityFor(StorageTechnology technology) =>
            technology switch
            {
                StorageTechnology.Battery => 0,
                StorageTechnology.PumpedHydro => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(technology)),
            };
    }
}