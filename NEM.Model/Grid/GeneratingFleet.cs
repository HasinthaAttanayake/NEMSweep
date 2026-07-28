using NEM.Model.Series;
using NEM.Model.Units;

namespace NEM.Model.Grid
{
    /// <summary>A region-wide aggregate of generation sharing one technology type.</summary>
    public sealed class GeneratingFleet
    {
        private readonly FlowSeries? _availableGeneration;

        public GeneratingFleet(
            TechnologyKey technologyKey,
            Power nameplateCapacity,
            FlowSeries? availableGeneration = null)
        {
            if (nameplateCapacity < Power.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nameplateCapacity),
                    nameplateCapacity.Megawatts,
                    "Nameplate capacity cannot be negative.");
            }

            if (availableGeneration is not null)
            {
                for (int index = 0; index < availableGeneration.Length; index++)
                {
                    Power available = availableGeneration[index];
                    if (available < Power.Zero || available > nameplateCapacity)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(availableGeneration),
                            available.Megawatts,
                            $"Available generation at index {index} must be between zero and nameplate capacity.");
                    }
                }
            }

            TechnologyKey = technologyKey;
            NameplateCapacity = nameplateCapacity;
            _availableGeneration = availableGeneration;
        }

        public TechnologyKey TechnologyKey { get; }
        public Power NameplateCapacity { get; }
        public ushort ShortRunMarginalCost => (ushort)TechnologyKey; // TODO: replace with SMRC cost basis B5
        public bool IsIntermittentRenewable => TechnologyKey is TechnologyKey.Solar or TechnologyKey.Wind; // TODO: move to TechnologyProfile as appropriate

        internal FlowSeries AvailableGenerationFor(FlowSeries dispatchTimeline)
        {
            if (_availableGeneration is not null)
            {
                dispatchTimeline.RequireAligned(_availableGeneration);
                return _availableGeneration;
            }

            var values = new double[dispatchTimeline.Length];
            Array.Fill(values, NameplateCapacity.Megawatts);
            return new FlowSeries(dispatchTimeline.Start, dispatchTimeline.Resolution, values);
        }
    }

    public enum TechnologyKey
    {
        // Note: ENUM values here are akin to ranking of SMRC. This is temporary
        Solar = 1,
        Wind = 2,
        Hydro = 3,
        Coal = 4,
        Gas = 5,
    }
}