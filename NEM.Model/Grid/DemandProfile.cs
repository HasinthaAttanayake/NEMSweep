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
        public IReadOnlyList<DemandComponent> AdditiveComponents { get; }

        public DemandProfile(
            FlowSeries baseDemand,
            IReadOnlyList<DemandComponent>? additiveComponents = null)
        {
            ArgumentNullException.ThrowIfNull(baseDemand);

            BaseDemand = baseDemand.ResampleToHourly();
            var components = new List<DemandComponent>();
            var componentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            FlowSeries totalDemand = BaseDemand;

            if (additiveComponents is not null)
            {
                foreach (DemandComponent component in additiveComponents)
                {
                    if (component.Demand is null)
                    {
                        throw new ArgumentException(
                            "Additive demand components cannot contain an uninitialized component.",
                            nameof(additiveComponents));
                    }

                    if (!componentNames.Add(component.Name))
                    {
                        throw new ArgumentException(
                            $"Additive demand component '{component.Name}' is duplicated.",
                            nameof(additiveComponents));
                    }

                    FlowSeries hourlyDemand = component.Demand.ResampleToHourly();
                    try
                    {
                        BaseDemand.RequireAligned(hourlyDemand);
                    }
                    catch (ArgumentException exception)
                    {
                        throw new ArgumentException(
                            $"Additive demand component '{component.Name}' must align with base demand: "
                            + exception.Message,
                            nameof(additiveComponents),
                            exception);
                    }

                    for (int index = 0; index < hourlyDemand.Length; index++)
                    {
                        if (hourlyDemand[index] < Power.Zero)
                        {
                            throw new ArgumentOutOfRangeException(
                                nameof(additiveComponents),
                                hourlyDemand[index].Megawatts,
                                $"Additive demand component '{component.Name}' cannot be negative at index {index}.");
                        }
                    }

                    var hourlyComponent = new DemandComponent(component.Name, hourlyDemand);
                    components.Add(hourlyComponent);
                    totalDemand = totalDemand.Add(hourlyComponent.Demand);
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

            AdditiveComponents = Array.AsReadOnly(components.ToArray());
            TotalDemand = totalDemand;
        }
    }
}
