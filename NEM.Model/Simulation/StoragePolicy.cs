using System.Collections.ObjectModel;
using NEM.Model.Grid;
using NEM.Model.Units;

namespace NEM.Model.Simulation
{
    /// <summary>
    /// Decides what storage should attempt in one dispatch interval. This is the model's storage
    /// extension point: supply an implementation to <see cref="Dispatcher"/> to change storage
    /// behaviour without touching dispatch itself.
    /// </summary>
    /// <remarks>
    /// A policy owns intent and fleet ordering only. It does not own state of charge, execute
    /// storage physics, or book unserved demand and curtailment; the dispatch run clamps every
    /// request against real headroom and each fleet remains the authority on power limits, energy
    /// limits and round-trip loss. A policy therefore cannot violate physics, only ask for
    /// something physics will refuse.
    /// </remarks>
    public interface IStoragePolicy
    {
        /// <summary>Returns the intents to attempt for the interval described by the context.</summary>
        /// <param name="context">An immutable snapshot of the current interval.</param>
        /// <returns>
        /// Zero or more intents, in the order they should be attempted. Return
        /// <see cref="StorageDecision.None"/> to do nothing this interval.
        /// </returns>
        StorageDecision Decide(DispatchContext context);
    }

    /// <summary>
    /// One storage fleet's position at the start of an interval, as scalars rather than as the
    /// mutable fleet itself. A policy sees this so it cannot reach through to fleet state.
    /// </summary>
    /// <param name="StorageTechnology">The archetype this snapshot describes.</param>
    /// <param name="StorageLevel">Stored energy in MWh at the start of the interval.</param>
    /// <param name="ChargeHeadroom">
    /// Charging power in MW the fleet could still accept this interval, after both its power
    /// rating and its remaining energy capacity are taken into account.
    /// </param>
    /// <param name="DischargeHeadroom">Discharge power in MW the fleet could still deliver this interval.</param>
    public readonly record struct StorageFleetSnapshot(
        StorageTechnology StorageTechnology,
        Energy StorageLevel,
        Power ChargeHeadroom,
        Power DischargeHeadroom);

    /// <summary>
    /// Represents a generation fleet's incremental dispatch capacity and short-run marginal cost
    /// for one dispatch interval. Cost is expressed in AUD per MWh generated.
    ///
    /// For conventional Hydro, <see cref="IncrementalGenerationHeadroom"/> is deliberately NOT
    /// the fleet's full remaining monthly budget: it is capped to whatever of this interval's
    /// causally-paced allowance (see <see cref="HydroReservationState"/>) hasn't already been
    /// dispatched to local demand, and it never includes Hydro's reserve share at all (that
    /// share is reachable only after storage, via <see cref="RegionalDispatchRun.DispatchHydroFallback"/>,
    /// never through a policy decision). A policy that requests incremental generation from
    /// Hydro is choosing to substitute it for local demand this interval, not drawing on budget
    /// saved for a future peak.
    /// </summary>
    /// <param name="GenerationTechnology">The generation technology this snapshot describes.</param>
    /// <param name="IncrementalGenerationHeadroom">
    /// Additional generation in MW this fleet could start this interval, over and above what it
    /// has already dispatched to demand.
    /// </param>
    /// <param name="ShortRunMarginalCost">
    /// Cost of that additional generation in AUD per MWh generated: variable operating cost plus
    /// fuel price multiplied by heat rate.
    /// </param>
    public readonly record struct GenerationFleetSnapshot(
        GenerationTechnology GenerationTechnology,
        Power IncrementalGenerationHeadroom,
        GenerationEnergyCost ShortRunMarginalCost);

    /// <summary>
    /// Everything a storage policy is allowed to see for one interval: the signed residual after
    /// generation has been dispatched to demand, the interval length, and one snapshot per storage
    /// and generation fleet.
    /// </summary>
    /// <remarks>
    /// The context is current-interval only. It can support a policy that charges from present
    /// excess generation capacity, but it cannot support pre-charging in anticipation of future
    /// residual demand, because no forward residual series is provided. That is a deliberate
    /// property of the model rather than an oversight, and it is why the shipped policies are
    /// greedy.
    /// </remarks>
    public readonly record struct DispatchContext
    {
        /// <summary>Validates and creates an interval snapshot.</summary>
        /// <param name="residual">
        /// Signed residual power. Positive means unmet demand; negative means surplus that would
        /// otherwise be curtailed.
        /// </param>
        /// <param name="storageFleets">One snapshot per storage fleet, with distinct technologies.</param>
        /// <param name="generationFleets">One snapshot per generation fleet, with distinct technologies.</param>
        /// <param name="resolution">Interval length. Must be positive.</param>
        /// <exception cref="ArgumentException">Two snapshots share a technology.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The resolution is not positive, or a level or headroom is negative.
        /// </exception>
        public DispatchContext(
            Power residual,
            IReadOnlyList<StorageFleetSnapshot> storageFleets,
            IReadOnlyList<GenerationFleetSnapshot> generationFleets,
            TimeSpan resolution)
        {
            ArgumentNullException.ThrowIfNull(storageFleets);
            ArgumentNullException.ThrowIfNull(generationFleets);
            if (resolution <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resolution), resolution, "Resolution must be positive.");
            }

            if (storageFleets.DistinctBy(fleet => fleet.StorageTechnology).Count()
                != storageFleets.Count)
            {
                throw new ArgumentException(
                    "Storage fleet snapshots must have distinct technologies.",
                    nameof(storageFleets));
            }

            if (generationFleets.DistinctBy(fleet => fleet.GenerationTechnology).Count()
                != generationFleets.Count)
            {
                throw new ArgumentException(
                    "Generation fleet snapshots must have distinct technologies.",
                    nameof(generationFleets));
            }

            if (storageFleets.Any(fleet =>
                    fleet.StorageLevel < Energy.Zero
                    || fleet.ChargeHeadroom < Power.Zero
                    || fleet.DischargeHeadroom < Power.Zero))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(storageFleets),
                    "Storage levels and headroom must be non-negative.");
            }

            if (generationFleets.Any(fleet => fleet.IncrementalGenerationHeadroom < Power.Zero))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(generationFleets),
                    "Incremental-generation headroom must be non-negative.");
            }

            Residual = residual;
            StorageFleets = new ReadOnlyCollection<StorageFleetSnapshot>(storageFleets.ToArray());
            GenerationFleets = new ReadOnlyCollection<GenerationFleetSnapshot>(generationFleets.ToArray());
            Resolution = resolution;
        }

        /// <summary>
        /// Signed residual power. Positive is unmet demand; negative is would-be-curtailed surplus.
        /// </summary>
        public Power Residual { get; }

        /// <summary>One snapshot per storage fleet in the region.</summary>
        public IReadOnlyList<StorageFleetSnapshot> StorageFleets { get; }

        /// <summary>One snapshot per generation fleet in the region.</summary>
        public IReadOnlyList<GenerationFleetSnapshot> GenerationFleets { get; }

        /// <summary>Interval length, used to convert requested MW into MWh.</summary>
        public TimeSpan Resolution { get; }
    }

    /// <summary>Where the energy for a charge intent comes from.</summary>
    public enum ChargeSourceKind
    {
        /// <summary>
        /// Generation that would otherwise be curtailed. Charging from surplus consumes no
        /// additional fuel and starts no additional plant.
        /// </summary>
        Surplus,

        /// <summary>
        /// Generation started specifically to charge storage. This consumes the named fleet's
        /// remaining capacity and, where it has one, its energy budget.
        /// </summary>
        IncrementalGeneration,
    }

    /// <summary>
    /// The energy source named by a charge intent. Surplus is sourceless; incremental generation
    /// identifies the fleet whose output is being started, so the charge can be booked against it.
    /// </summary>
    public readonly record struct ChargeSource
    {
        private ChargeSource(
            ChargeSourceKind kind,
            GenerationTechnology? generationTechnology)
        {
            Kind = kind;
            GenerationTechnology = generationTechnology;
        }

        /// <summary>Whether the energy is surplus or newly started generation.</summary>
        public ChargeSourceKind Kind { get; }

        /// <summary>
        /// The fleet supplying incremental generation, or null when the source is surplus.
        /// </summary>
        public GenerationTechnology? GenerationTechnology { get; }

        /// <summary>Charging from generation that would otherwise be curtailed.</summary>
        public static ChargeSource Surplus { get; } = new(ChargeSourceKind.Surplus, null);

        /// <summary>Charging from generation started for that purpose.</summary>
        /// <param name="generationTechnology">The fleet whose output is being started.</param>
        public static ChargeSource IncrementalGeneration(GenerationTechnology generationTechnology) =>
            new(ChargeSourceKind.IncrementalGeneration, generationTechnology);
    }

    /// <summary>
    /// One thing a policy asks of one storage fleet this interval. Sign carries the direction: a
    /// positive requested flow discharges to the grid, a negative one charges from it.
    /// </summary>
    public readonly record struct StorageIntent
    {
        /// <summary>Validates and creates one intent.</summary>
        /// <param name="storageTechnology">The fleet the intent targets.</param>
        /// <param name="requestedFlow">
        /// Requested MW. Positive discharges, negative charges; zero is rejected because an intent
        /// to do nothing is expressed by omitting the intent.
        /// </param>
        /// <param name="chargeSource">
        /// Required for a charge intent, rejected for a discharge intent.
        /// </param>
        /// <exception cref="ArgumentException">
        /// A discharge intent named a source, or a charge intent did not.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">The requested flow is zero.</exception>
        public StorageIntent(
            StorageTechnology storageTechnology,
            Power requestedFlow,
            ChargeSource? chargeSource = null)
        {
            if (requestedFlow == Power.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestedFlow),
                    requestedFlow.Megawatts,
                    "A storage intent must request non-zero flow.");
            }

            if (requestedFlow > Power.Zero && chargeSource is not null)
            {
                throw new ArgumentException(
                    "A discharge intent cannot specify a charging source.",
                    nameof(chargeSource));
            }

            if (requestedFlow < Power.Zero && chargeSource is null)
            {
                throw new ArgumentException(
                    "A charge intent must specify its charging source.",
                    nameof(chargeSource));
            }

            StorageTechnology = storageTechnology;
            RequestedFlow = requestedFlow;
            ChargeSource = chargeSource;
        }

        /// <summary>The fleet this intent targets.</summary>
        public StorageTechnology StorageTechnology { get; }

        /// <summary>Requested MW: positive discharges to the grid, negative charges from it.</summary>
        public Power RequestedFlow { get; }

        /// <summary>The charging source, or null for a discharge intent.</summary>
        public ChargeSource? ChargeSource { get; }
    }

    /// <summary>
    /// Contains the ordered storage intents for one dispatch interval. A storage technology can
    /// have one discharge intent, one surplus-charge intent, and one incremental-generation
    /// charge intent per generation technology.
    /// </summary>
    public sealed record StorageDecision
    {
        /// <summary>A decision to attempt nothing this interval.</summary>
        public static StorageDecision None { get; } = new([]);

        /// <summary>Validates and creates a decision from intents in attempt order.</summary>
        /// <param name="intents">
        /// The intents to attempt, in order. Per storage technology, at most one discharge intent,
        /// at most one surplus-charge intent, and at most one incremental-generation charge intent
        /// per generation technology.
        /// </param>
        /// <exception cref="ArgumentException">Those per-technology limits are exceeded.</exception>
        public StorageDecision(IReadOnlyList<StorageIntent> intents)
        {
            ArgumentNullException.ThrowIfNull(intents);
            foreach (IGrouping<StorageTechnology, StorageIntent> fleetIntents in intents
                         .GroupBy(intent => intent.StorageTechnology))
            {
                if (fleetIntents.Count(intent => intent.RequestedFlow > Power.Zero) > 1)
                {
                    throw new ArgumentException(
                        "A decision cannot contain multiple discharge intents for one storage technology.",
                        nameof(intents));
                }

                if (fleetIntents.Count(intent => intent.ChargeSource == ChargeSource.Surplus) > 1)
                {
                    throw new ArgumentException(
                        "A decision cannot contain multiple surplus-charge intents for one storage technology.",
                        nameof(intents));
                }

                if (fleetIntents
                    .Where(intent => intent.ChargeSource?.Kind == ChargeSourceKind.IncrementalGeneration)
                    .GroupBy(intent => intent.ChargeSource!.Value.GenerationTechnology)
                    .Any(sourceIntents => sourceIntents.Count() > 1))
                {
                    throw new ArgumentException(
                        "A decision cannot contain multiple incremental-generation charge intents "
                        + "from one generation technology for one storage technology.",
                        nameof(intents));
                }
            }

            Intents = new ReadOnlyCollection<StorageIntent>(intents.ToArray());
        }

        /// <summary>
        /// The intents to attempt, in order. An intent can be skipped without producing an outcome
        /// when no deficit, surplus, or incremental-generation headroom remains by the time it is
        /// reached.
        /// </summary>
        public IReadOnlyList<StorageIntent> Intents { get; }
    }
}