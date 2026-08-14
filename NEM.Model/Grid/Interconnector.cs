using NEM.Model.Units;

namespace NEM.Model.Grid;

/// <summary>
/// A directed, bounded transfer path between two regions, owned by the
/// <see cref="PowerSystem"/> rather than by either endpoint.
/// </summary>
/// <remarks>
/// This record permits transfer only from <see cref="FromRegionId"/> to
/// <see cref="ToRegionId"/>. A separately declared interconnector is required for
/// transfer in the opposite direction. Capacity is metered at the sending end:
/// scheduling 100 MW consumes 100 MW of capacity, while losses are applied to what
/// arrives rather than what the link carries.
/// <para>
/// Region identity is a bare string throughout the model, compared case-insensitively,
/// so endpoints follow that convention rather than introducing an identifier type.
/// </para>
/// </remarks>
public sealed record Interconnector
{
    public Interconnector(
        string fromRegionId,
        string toRegionId,
        Power capacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromRegionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toRegionId);
        if (string.Equals(fromRegionId, toRegionId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"An interconnector cannot connect region '{fromRegionId}' to itself.",
                nameof(toRegionId));
        }

        RequireCapacity(capacity, nameof(capacity));

        FromRegionId = fromRegionId;
        ToRegionId = toRegionId;
        Capacity = capacity;
    }

    /// <summary>Sending region for this directed transfer path.</summary>
    public string FromRegionId { get; }

    /// <summary>Receiving region for this directed transfer path.</summary>
    public string ToRegionId { get; }

    /// <summary>Maximum scheduled transfer from <see cref="FromRegionId"/> to <see cref="ToRegionId"/>.</summary>
    public Power Capacity { get; }

    private static void RequireCapacity(Power capacity, string parameterName)
    {
        if (!double.IsFinite(capacity.Megawatts) || capacity.Megawatts < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                capacity.Megawatts,
                "Interconnector capacity must be finite and non-negative.");
        }
    }
}
