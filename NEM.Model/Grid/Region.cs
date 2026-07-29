using NEM.Model.Series;
using NEM.Model.Weather;

namespace NEM.Model.Grid
{
    /// <summary>A NEM region whose grid-model series are represented hourly.</summary>
    public sealed class Region
    {
        public string RegionId { get; }
        public DemandProfile Demand { get; }
        public IReadOnlyList<GeneratingFleet> Fleets { get; }
        public RegionalResourceProfile? ResourceProfile { get; }

        public Region(
            string regionId,
            IReadOnlyList<GeneratingFleet> fleets,
            FlowSeries baseDemand,
            IReadOnlyDictionary<string, FlowSeries>? additiveDemandComponents = null,
            RegionalResourceProfile? resourceProfile = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
            ArgumentNullException.ThrowIfNull(fleets);
            if (fleets.Count == 0)
            {
                throw new ArgumentException("Region must have at least one generating fleet.", nameof(fleets));
            }

            if (fleets.Any(fleet => fleet is null))
            {
                throw new ArgumentException("Region fleets cannot contain null.", nameof(fleets));
            }

            if (fleets.DistinctBy(fleet => fleet.TechnologyKey).Count() != fleets.Count)
            {
                throw new ArgumentException(
                    "Region cannot have more than one fleet with the same technology key.",
                    nameof(fleets));
            }

            if (fleets.Any(fleet => fleet.IsIntermittentRenewable) && resourceProfile is null)
            {
                throw new ArgumentException(
                    "Regions containing wind or solar fleets require a resource profile.",
                    nameof(resourceProfile));
            }

            var demand = new DemandProfile(baseDemand, additiveDemandComponents);
            resourceProfile?.RequireAligned(demand.TotalDemand);

            RegionId = regionId;
            Demand = demand;
            Fleets = Array.AsReadOnly(fleets.ToArray());
            ResourceProfile = resourceProfile;
        }
    }
}