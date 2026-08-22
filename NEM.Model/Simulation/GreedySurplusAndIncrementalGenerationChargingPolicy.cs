using NEM.Model.Grid;
using NEM.Model.Units;

namespace NEM.Model.Simulation
{
    /// <summary>
    /// Requests immediate storage discharge for demand deficits. For balanced and surplus
    /// intervals, it charges from would-be-curtailed surplus first, then from available Coal and
    /// Gas generation in ascending short-run marginal cost order. This is the dispatcher's
    /// default policy and differs from <see cref="GreedyPolicy"/> by permitting incremental
    /// generation to charge storage.
    /// </summary>
    public sealed class GreedySurplusAndIncrementalGenerationChargingPolicy : IStoragePolicy
    {
        /// <summary>
        /// For a deficit, requests discharge from Battery first and pumped hydro second. For a
        /// balanced or surplus interval, allocates surplus to charging first, then requests
        /// incremental Coal and Gas generation (in ascending short-run marginal cost order) to
        /// fill remaining charge headroom.
        /// </summary>
        /// <param name="context">An immutable snapshot of the current interval.</param>
        /// <returns>
        /// Zero or more discharge, surplus-charge, or incremental-generation charge intents, or
        /// <see cref="StorageDecision.None"/> when no fleet has headroom.
        /// </returns>
        public StorageDecision Decide(DispatchContext context)
        {
            return context.Residual > Power.Zero
                ? Discharge(context)
                : Charge(context);
        }

        private static StorageDecision Discharge(DispatchContext context)
        {
            Power remainingDeficit = context.Residual;
            var intents = new List<StorageIntent>();
            foreach (StorageFleetSnapshot fleet in context.StorageFleets
                         .OrderBy(fleet => PriorityFor(fleet.StorageTechnology)))
            {
                Power requestedDischarge = Power.Min(remainingDeficit, fleet.DischargeHeadroom);
                if (requestedDischarge == Power.Zero)
                {
                    continue;
                }

                intents.Add(new StorageIntent(fleet.StorageTechnology, requestedDischarge));
                remainingDeficit -= requestedDischarge;
                if (remainingDeficit == Power.Zero)
                {
                    break;
                }
            }

            return intents.Count == 0 ? StorageDecision.None : new StorageDecision(intents);
        }

        private static StorageDecision Charge(DispatchContext context)
        {
            Power remainingSurplus = context.Residual * -1;
            var incrementalGenerators = context.GenerationFleets
                .Where(generator =>
                    AllowedIncrementalGenerators(generator.GenerationTechnology)
                    && generator.IncrementalGenerationHeadroom > Power.Zero)
                .OrderBy(generator => generator.ShortRunMarginalCost)
                .ThenBy(generator => generator.GenerationTechnology)
                .ToList();
            var intents = new List<StorageIntent>();

            foreach (StorageFleetSnapshot fleet in context.StorageFleets
                         .OrderBy(fleet => PriorityFor(fleet.StorageTechnology)))
            {
                Power remainingChargeHeadroom = fleet.ChargeHeadroom;
                Power requestedSurplusCharge = Power.Min(remainingSurplus, remainingChargeHeadroom);
                if (requestedSurplusCharge > Power.Zero)
                {
                    intents.Add(new StorageIntent(
                        fleet.StorageTechnology,
                        requestedSurplusCharge * -1,
                        ChargeSource.Surplus));
                    remainingSurplus -= requestedSurplusCharge;
                    remainingChargeHeadroom -= requestedSurplusCharge;
                }

                for (int index = 0;
                     index < incrementalGenerators.Count && remainingChargeHeadroom > Power.Zero;
                     index++)
                {
                    GenerationFleetSnapshot generator = incrementalGenerators[index];
                    Power requestedIncrementalCharge = Power.Min(
                        generator.IncrementalGenerationHeadroom,
                        remainingChargeHeadroom);
                    if (requestedIncrementalCharge == Power.Zero)
                    {
                        continue;
                    }

                    intents.Add(new StorageIntent(
                        fleet.StorageTechnology,
                        requestedIncrementalCharge * -1,
                        ChargeSource.IncrementalGeneration(generator.GenerationTechnology)));
                    remainingChargeHeadroom -= requestedIncrementalCharge;
                    incrementalGenerators[index] = generator with
                    {
                        IncrementalGenerationHeadroom =
                            generator.IncrementalGenerationHeadroom - requestedIncrementalCharge,
                    };
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

        private static bool AllowedIncrementalGenerators(GenerationTechnology technology) =>
            technology is GenerationTechnology.Coal or GenerationTechnology.Gas;
    }
}