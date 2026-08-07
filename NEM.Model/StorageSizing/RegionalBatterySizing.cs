using NEM.Model.Units;

namespace NEM.Model.StorageSizing;

/// <summary>Total installed Battery capacity selected or assessed for one region.</summary>
public sealed record RegionalBatterySizing
{
    public RegionalBatterySizing(
        string regionId,
        Energy energyCapacity,
        Power powerCapacity,
        bool wasChanged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        if (energyCapacity < Energy.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(energyCapacity));
        }

        if (powerCapacity < Power.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(powerCapacity));
        }

        if ((energyCapacity == Energy.Zero) != (powerCapacity == Power.Zero))
        {
            throw new ArgumentException(
                "Battery power and energy capacity must either both be zero or both be positive.");
        }

        RegionId = regionId;
        EnergyCapacity = energyCapacity;
        PowerCapacity = powerCapacity;
        WasChanged = wasChanged;
    }

    /// <summary>Identifies the region whose Battery capacity is described.</summary>
    public string RegionId { get; }
    /// <summary>Total installed Battery energy capacity.</summary>
    public Energy EnergyCapacity { get; }
    /// <summary>Total installed Battery power capacity.</summary>
    public Power PowerCapacity { get; }
    /// <summary>Whether the sizing search changed capacity from the installed baseline.</summary>
    public bool WasChanged { get; }
}