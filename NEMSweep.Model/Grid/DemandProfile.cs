using NEMSweep.Model.Series;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Grid;

/// <summary>
/// Regional demand represented at the grid model's hourly resolution as one base
/// series plus zero or more named additive components.
/// </summary>
public sealed class DemandProfile
{
    /// <summary>The grid model's fixed hourly resolution for demand series.</summary>
    public static TimeSpan Resolution { get; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Base demand plus the element-wise sum of every additive component. This is the only
    /// demand series consumed by dispatch.
    /// </summary>
    public FlowSeries TotalDemand { get; }
    /// <summary>The region's underlying demand, before any additive components.</summary>
    public FlowSeries BaseDemand { get; }
    /// <summary>Zero or more non-negative, uniquely named demand flows added to base demand.</summary>
    public IReadOnlyList<DemandComponent> AdditiveComponents { get; }

    /// <summary>Validates and creates a demand profile.</summary>
    /// <param name="baseDemand">Base demand series, resampled to hourly resolution.</param>
    /// <param name="additiveComponents">
    /// Optional components, each non-negative, uniquely named case-insensitively, and exactly
    /// aligned with <paramref name="baseDemand"/> once resampled to hourly resolution.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A component's name is duplicated, or its resampled series does not align with base
    /// demand.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A component is negative at some interval, or total demand (base plus components) is
    /// negative at some interval.
    /// </exception>
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
