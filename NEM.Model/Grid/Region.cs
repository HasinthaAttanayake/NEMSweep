using System.Collections.ObjectModel;
using NEM.Model.Series;
using NEM.Model.Units;
using NEM.Model.Weather;

namespace NEM.Model.Grid
{
    /// <summary>A NEM region whose grid-model series are represented hourly.</summary>
    public sealed class Region
    {
        public string RegionId { get; }
        public DemandProfile Demand { get; }
        public IReadOnlyList<GeneratingFleet> GeneratingFleets { get; }
        public IReadOnlyList<StorageFleet> StorageFleets { get; }
        /// <summary>
        /// Technical assumptions available for installed or scenario-planned storage.
        /// </summary>
        public IReadOnlyDictionary<StorageTechnology, StorageTechnologyProfile>
            StorageTechnologyProfiles
        { get; }
        public RegionalResourceProfile? ResourceProfile { get; }

        public Region(
            string regionId,
            IReadOnlyList<GeneratingFleet> generatingFleets,
            FlowSeries baseDemand,
            IReadOnlyDictionary<string, FlowSeries>? additiveDemandComponents = null,
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

            StorageFleet[] storageFleets = StorageFleets
                .Where(fleet => fleet.StorageTechnology != StorageTechnology.Battery)
                .Append(new StorageFleet(
                    StorageTechnology.Battery,
                    storageCapacity,
                    powerCapacity,
                    technologyProfile))
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