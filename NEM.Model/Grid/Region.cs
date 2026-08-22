using System.Collections.ObjectModel;
using NEM.Model.Series;
using NEM.Model.Units;
using NEM.Model.Weather;

namespace NEM.Model.Grid
{
    /// <summary>A NEM region whose grid-model series are represented hourly.</summary>
    public sealed class Region
    {
        /// <summary>Identifies the NEM region, for example <c>NSW1</c>.</summary>
        public string RegionId { get; }
        /// <summary>The region's base demand plus any labelled additive components.</summary>
        public DemandProfile Demand { get; }
        /// <summary>One generating fleet per generation technology present in the region.</summary>
        public IReadOnlyList<GeneratingFleet> GeneratingFleets { get; }
        /// <summary>Zero or more storage fleets, with distinct storage technologies.</summary>
        public IReadOnlyList<StorageFleet> StorageFleets { get; }
        /// <summary>
        /// Technical assumptions available for installed or scenario-planned storage.
        /// </summary>
        public IReadOnlyDictionary<StorageTechnology, StorageTechnologyProfile>
            StorageTechnologyProfiles
        { get; }
        /// <summary>
        /// Weather-derived resource data, required when the region has a Solar or Wind fleet and
        /// optional otherwise.
        /// </summary>
        public RegionalResourceProfile? ResourceProfile { get; }

        /// <summary>Validates and creates a realised region.</summary>
        /// <param name="regionId">The NEM region this realises, for example <c>NSW1</c>.</param>
        /// <param name="generatingFleets">
        /// At least one fleet, with distinct generation technologies. A Solar or Wind fleet
        /// requires <paramref name="resourceProfile"/>.
        /// </param>
        /// <param name="baseDemand">Base demand series, resampled to hourly resolution.</param>
        /// <param name="additiveDemandComponents">
        /// Optional non-negative demand components, uniquely named case-insensitively and aligned
        /// with <paramref name="baseDemand"/>.
        /// </param>
        /// <param name="resourceProfile">
        /// Weather-derived resource data. Required when any fleet is Solar or Wind.
        /// </param>
        /// <param name="storageFleets">Storage fleets with distinct storage technologies, or null.</param>
        /// <param name="storageTechnologyProfiles">
        /// Technology profiles for installed or scenario-planned storage. A fleet's own profile
        /// must match any entry already keyed by its technology.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The region has no generating fleets, contains duplicate generation or storage
        /// technologies, is missing a required resource profile, or a storage fleet's profile
        /// conflicts with its region-level entry.
        /// </exception>
        public Region(
            string regionId,
            IReadOnlyList<GeneratingFleet> generatingFleets,
            FlowSeries baseDemand,
            IReadOnlyList<DemandComponent>? additiveDemandComponents = null,
            RegionalResourceProfile? resourceProfile = null,
            IReadOnlyList<StorageFleet>? storageFleets = null,
            IReadOnlyDictionary<StorageTechnology, StorageTechnologyProfile>?
                storageTechnologyProfiles = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
            ArgumentNullException.ThrowIfNull(generatingFleets);
            if (generatingFleets.Count == 0)
            {
                throw new ArgumentException(
                    "Region must have at least one generating fleet.",
                    nameof(generatingFleets));
            }

            if (generatingFleets.Any(fleet => fleet is null))
            {
                throw new ArgumentException(
                    "Region generating fleets cannot contain null.",
                    nameof(generatingFleets));
            }

            if (generatingFleets.DistinctBy(fleet => fleet.GenerationTechnology).Count()
                != generatingFleets.Count)
            {
                throw new ArgumentException(
                    "Region cannot have more than one generating fleet with the same generation technology.",
                    nameof(generatingFleets));
            }

            if (generatingFleets.Any(fleet => fleet.IsIntermittentRenewable) && resourceProfile is null)
            {
                throw new ArgumentException(
                    "Regions containing wind or solar generating fleets require a resource profile.",
                    nameof(resourceProfile));
            }

            IReadOnlyList<StorageFleet> resolvedStorageFleets = storageFleets ?? [];
            if (resolvedStorageFleets.Any(fleet => fleet is null))
            {
                throw new ArgumentException(
                    "Region storage fleets cannot contain null.",
                    nameof(storageFleets));
            }

            if (resolvedStorageFleets
                .DistinctBy(fleet => fleet.StorageTechnology)
                .Count() != resolvedStorageFleets.Count)
            {
                throw new ArgumentException(
                    "Region cannot have more than one storage fleet with the same storage technology.",
                    nameof(storageFleets));
            }

            if (storageTechnologyProfiles?.Any(entry => entry.Value is null) == true)
            {
                throw new ArgumentException(
                    "Region storage technology profiles cannot contain null.",
                    nameof(storageTechnologyProfiles));
            }

            var resolvedStorageTechnologyProfiles = storageTechnologyProfiles is null
                ? []
                : new Dictionary<StorageTechnology, StorageTechnologyProfile>(
                    storageTechnologyProfiles);
            foreach (StorageFleet fleet in resolvedStorageFleets)
            {
                if (resolvedStorageTechnologyProfiles.TryGetValue(
                        fleet.StorageTechnology,
                        out StorageTechnologyProfile? configuredProfile)
                    && configuredProfile != fleet.TechnologyProfile)
                {
                    throw new ArgumentException(
                        "A storage fleet must use its region's configured technology profile.",
                        nameof(storageFleets));
                }

                resolvedStorageTechnologyProfiles[fleet.StorageTechnology] =
                    fleet.TechnologyProfile;
            }

            var demand = new DemandProfile(baseDemand, additiveDemandComponents);
            resourceProfile?.RequireAligned(demand.TotalDemand);

            RegionId = regionId;
            Demand = demand;
            GeneratingFleets = Array.AsReadOnly(generatingFleets.ToArray());
            StorageFleets = Array.AsReadOnly(resolvedStorageFleets.ToArray());
            StorageTechnologyProfiles = new ReadOnlyDictionary<
                StorageTechnology,
                StorageTechnologyProfile>(resolvedStorageTechnologyProfiles);
            ResourceProfile = resourceProfile;
        }

        /// <summary>
        /// Returns a copy of this region with its Battery fleet replaced at the given capacity,
        /// used by storage sizing to grow or shrink Battery capacity between dispatch passes.
        /// Any other storage fleet (pumped hydro) is carried over unchanged.
        /// </summary>
        /// <param name="storageCapacity">New Battery energy capacity in MWh.</param>
        /// <param name="powerCapacity">New Battery power capacity in MW.</param>
        /// <returns>A new <see cref="Region"/> with the resized Battery fleet.</returns>
        /// <exception cref="InvalidOperationException">
        /// The region has no configured Battery technology profile.
        /// </exception>
        public Region WithBatteryStorage(
            Energy storageCapacity,
            Power powerCapacity)
        {
            if (!StorageTechnologyProfiles.TryGetValue(
                    StorageTechnology.Battery,
                    out StorageTechnologyProfile? technologyProfile))
            {
                throw new InvalidOperationException(
                    $"Region {RegionId} has no configured Battery technology profile.");
            }

            // Seed energy is fixed at whatever the Battery already carries (Energy.Zero if
            // this region has never had one) - resizing must never change it. See
            // StorageSeedPolicy for why. Clamped to the new capacity: growth never needs this
            // (StorageSizingSearch never refines below installed capacity today), but nothing
            // enforces that dependency, and refinement shrinking capacity below the seed would
            // otherwise leave the fleet opening above 100% state of charge.
            Energy seedEnergy = Energy.Min(
                StorageFleets
                    .SingleOrDefault(fleet => fleet.StorageTechnology == StorageTechnology.Battery)
                    ?.SeedEnergy ?? Energy.Zero,
                storageCapacity);

            StorageFleet[] storageFleets = StorageFleets
                .Where(fleet => fleet.StorageTechnology != StorageTechnology.Battery)
                .Append(new StorageFleet(
                    StorageTechnology.Battery,
                    storageCapacity,
                    powerCapacity,
                    technologyProfile,
                    seedEnergy))
                .ToArray();

            return new Region(
                RegionId,
                GeneratingFleets,
                Demand.BaseDemand,
                Demand.AdditiveComponents,
                ResourceProfile,
                storageFleets,
                StorageTechnologyProfiles);
        }
    }
}