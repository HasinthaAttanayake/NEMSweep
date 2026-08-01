using System.Collections.ObjectModel;
using NEM.Model.Grid;
using NEM.Model.Units;

namespace NEM.Model.Simulation
{
    public interface IStoragePolicy
    {
        StorageDecision Decide(DispatchContext context);
    }

    public readonly record struct StorageFleetSnapshot(
        StorageTechnology StorageTechnology,
        Energy StorageLevel,
        Power ChargeHeadroom,
        Power DischargeHeadroom);

    public readonly record struct GenerationFleetSnapshot(
        GenerationTechnology GenerationTechnology,
        Power IncrementalGenerationHeadroom);

    public readonly record struct DispatchContext
    {
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

        public Power Residual { get; }
        public IReadOnlyList<StorageFleetSnapshot> StorageFleets { get; }
        public IReadOnlyList<GenerationFleetSnapshot> GenerationFleets { get; }
        public TimeSpan Resolution { get; }
    }

    public enum ChargeSourceKind
    {
        Surplus,
        IncrementalGeneration,
    }

    public readonly record struct ChargeSource
    {
        private ChargeSource(
            ChargeSourceKind kind,
            GenerationTechnology? generationTechnology)
        {
            Kind = kind;
            GenerationTechnology = generationTechnology;
        }

        public ChargeSourceKind Kind { get; }
        public GenerationTechnology? GenerationTechnology { get; }

        public static ChargeSource Surplus { get; } = new(ChargeSourceKind.Surplus, null);

        public static ChargeSource IncrementalGeneration(GenerationTechnology generationTechnology) =>
            new(ChargeSourceKind.IncrementalGeneration, generationTechnology);
    }

    public readonly record struct StorageIntent
    {
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

        public StorageTechnology StorageTechnology { get; }
        public Power RequestedFlow { get; }
        public ChargeSource? ChargeSource { get; }
    }

    public sealed record StorageDecision
    {
        public static StorageDecision None { get; } = new([]);

        public StorageDecision(IReadOnlyList<StorageIntent> intents)
        {
            ArgumentNullException.ThrowIfNull(intents);
            if (intents.DistinctBy(intent => intent.StorageTechnology).Count() != intents.Count)
            {
                throw new ArgumentException(
                    "A decision cannot contain multiple intents for one storage technology.",
                    nameof(intents));
            }

            Intents = new ReadOnlyCollection<StorageIntent>(intents.ToArray());
        }

        public IReadOnlyList<StorageIntent> Intents { get; }
    }
}