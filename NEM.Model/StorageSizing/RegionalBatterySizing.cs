using NEM.Model.Units;

namespace NEM.Model.StorageSizing;

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

    public string RegionId { get; }
    public Energy EnergyCapacity { get; }
    public Power PowerCapacity { get; }
    public bool WasChanged { get; }
}