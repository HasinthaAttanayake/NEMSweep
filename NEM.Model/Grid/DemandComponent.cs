using NEM.Model.Series;

namespace NEM.Model.Grid;

/// <summary>A labelled, non-negative demand flow added to a region's base demand.</summary>
public readonly record struct DemandComponent
{
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

    public string Name { get; }

    /// <summary>Component demand in MW.</summary>
    public FlowSeries Demand { get; }
}