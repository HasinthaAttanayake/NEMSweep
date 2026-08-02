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
        public RegionalResourceProfile? ResourceProfile { get; }

        public Region(
            string regionId,
            IReadOnlyList<GeneratingFleet> generatingFleets,
            FlowSeries baseDemand,
            IReadOnlyDictionary<string, FlowSeries>? additiveDemandComponents = null,
            RegionalResourceProfile? resourceProfile = null,
            IReadOnlyList<StorageFleet>? storageFleets = null)
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

            var demand = new DemandProfile(baseDemand, additiveDemandComponents);
            resourceProfile?.RequireAligned(demand.TotalDemand);

            RegionId = regionId;
            Demand = demand;
            GeneratingFleets = Array.AsReadOnly(generatingFleets.ToArray());
            StorageFleets = Array.AsReadOnly(resolvedStorageFleets.ToArray());
            ResourceProfile = resourceProfile;
        }

        public Region WithBatteryStorage(Energy storageCapacity, Power powerCapacity)
        {
            StorageFleet[] storageFleets = StorageFleets
                .Where(fleet => fleet.StorageTechnology != StorageTechnology.Battery)
                .Append(new StorageFleet(
                    StorageTechnology.Battery,
                    storageCapacity,
                    powerCapacity))
                .ToArray();

            return new Region(
                RegionId,
                GeneratingFleets,
                Demand.BaseDemand,
                Demand.AdditiveComponents,
                ResourceProfile,
                storageFleets);
        }
    }
}