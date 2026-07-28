using System.Collections.ObjectModel;
using NEM.Model.Series;
using NEM.Model.Units;

namespace NEM.Model.Grid
{
    /// <summary>
    /// Regional demand represented at the grid model's hourly resolution as one base
    /// series plus zero or more named additive components.
    /// </summary>
    public sealed class DemandProfile
    {
        public static TimeSpan Resolution { get; } = TimeSpan.FromHours(1);

        public FlowSeries TotalDemand { get; }
        public FlowSeries BaseDemand { get; }
        public IReadOnlyDictionary<string, FlowSeries> AdditiveComponents { get; }

        public DemandProfile(
            FlowSeries baseDemand,
            IReadOnlyDictionary<string, FlowSeries>? additiveComponents = null)
        {
            ArgumentNullException.ThrowIfNull(baseDemand);

            BaseDemand = baseDemand.ResampleToHourly();
            var hourlyComponents = new Dictionary<string, FlowSeries>(StringComparer.OrdinalIgnoreCase);
            FlowSeries totalDemand = BaseDemand;

            if (additiveComponents is not null)
            {
                foreach ((string name, FlowSeries component) in additiveComponents)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(name);
                    ArgumentNullException.ThrowIfNull(component);

                    FlowSeries hourlyComponent = component.ResampleToHourly();
                    hourlyComponents.Add(name, hourlyComponent);
                    totalDemand = totalDemand.Add(hourlyComponent);
                }
            }

            for (int index = 0; index < totalDemand.Length; index++)
            {
                if (totalDemand[index] < Power.Zero)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(baseDemand),
                        totalDemand[index].Megawatts,
                        $"Total demand (base plus additive components) at index {index} cannot be negative.");
                }
            }

            AdditiveComponents = new ReadOnlyDictionary<string, FlowSeries>(hourlyComponents);
            TotalDemand = totalDemand;
        }
    }
}
