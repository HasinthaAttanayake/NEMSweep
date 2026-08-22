using NEM.Model.Series;

namespace NEM.Model.Grid;

/// <summary>A labelled, non-negative demand flow added to a region's base demand.</summary>
public readonly record struct DemandComponent
{
    /// <summary>Validates and creates a labelled demand component.</summary>
    /// <param name="name">Non-blank label, unique within a region's components case-insensitively.</param>
    /// <param name="demand">
    /// Non-negative demand series in MW, starting in NEM market time (UTC+10).
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="demand"/> is negative at some interval.</exception>
    public DemandComponent(string name, FlowSeries demand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(demand);
        NemTime.Require(demand.Start, nameof(demand));

        for (int index = 0; index < demand.Length; index++)
        {
            if (demand[index].Megawatts < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(demand),
                    demand[index].Megawatts,
                    $"Demand component '{name}' cannot be negative at index {index}.");
            }
        }

        Name = name;
        Demand = demand;
    }

    /// <summary>The component's label, unique within a region's components case-insensitively.</summary>
    public string Name { get; }

    /// <summary>Component demand in MW.</summary>
    public FlowSeries Demand { get; }
}