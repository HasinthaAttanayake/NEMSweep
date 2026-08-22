using NEM.Model.Units;

namespace NEM.Model.StorageSizing;

/// <summary>Total installed Battery capacity selected or assessed for one region.</summary>
public sealed record RegionalBatterySizing
{
    /// <summary>Validates and creates a regional Battery sizing.</summary>
    /// <param name="regionId">Identifies the region whose Battery capacity is described.</param>
    /// <param name="energyCapacity">Total installed Battery energy capacity in MWh. Must be zero or positive.</param>
    /// <param name="powerCapacity">Total installed Battery power capacity in MW. Must be zero or positive.</param>
    /// <param name="wasChanged">Whether the sizing search changed capacity from the installed baseline.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either capacity is negative.</exception>
    /// <exception cref="ArgumentException">
    /// One capacity is zero while the other is positive; a fleet is either fully installed or absent.
    /// </exception>
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