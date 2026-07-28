using NEM.Model.Series;

namespace NEM.Model.Grid
{
    /// <summary>A NEM region whose grid-model series are represented hourly.</summary>
    public sealed class Region
    {
        public string RegionId { get; }
        public DemandProfile Demand { get; }

        public Region(
            string regionId,
            FlowSeries baseDemand,
            IReadOnlyDictionary<string, FlowSeries>? additiveDemandComponents = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(regionId);

            RegionId = regionId;
            Demand = new DemandProfile(baseDemand, additiveDemandComponents);
        }
    }
}