using NEM.Model.Units;

namespace NEM.Model.Grid
{
    public sealed class StorageFleet
    {
        private readonly StorageTechnologyProfile _technologyProfile;

        public StorageFleet(
            StorageTechnology storageTechnology,
            Energy storageCapacity,
            Power powerCapacity)
        {
            if (storageCapacity <= Energy.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(storageCapacity),
                    storageCapacity.MegawattHours,
                    "Storage capacity must be positive.");
            }

            if (powerCapacity <= Power.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(powerCapacity),
                    powerCapacity.Megawatts,
                    "Power capacity must be positive.");
            }

            _technologyProfile = StorageTechnologyProfile.ProfileFor(storageTechnology);
            StorageTechnology = storageTechnology;
            StorageCapacity = storageCapacity;
            PowerCapacity = powerCapacity;
        }

        /// <summary>Storage duration determined by this fleet's energy and power capacities.</summary>
        public TimeSpan Duration => StorageCapacity / PowerCapacity;

        public StorageTechnology StorageTechnology { get; }
        public Energy StorageCapacity { get; }
        public Power PowerCapacity { get; }

        /// <summary>
        /// Advances storage over an interval. Positive requested flow discharges to the
        /// grid; negative requested flow charges from it. Round-trip efficiency is applied
        /// once when charging: grid input becomes stored energy at that factor, and stored
        /// energy subsequently discharges without an additional efficiency adjustment.
        /// </summary>
        public StorageOutcome Operate(Energy initialStorageLevel, Power requestedFlow, TimeSpan resolution)
        {
            if (initialStorageLevel < Energy.Zero || initialStorageLevel > StorageCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialStorageLevel),
                    initialStorageLevel.MegawattHours,
                    "Initial storage level must be within storage capacity.");
            }

            if (resolution <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resolution), resolution, "Resolution must be positive.");
            }

            if (requestedFlow > Power.Zero)
            {
                Energy dischargedEnergy = Energy.Min(
                    initialStorageLevel,
                    Energy.Min(requestedFlow * resolution, PowerCapacity * resolution));

                return new StorageOutcome(
                    initialStorageLevel - dischargedEnergy,
                    dischargedEnergy / resolution);
            }

            if (requestedFlow < Power.Zero)
            {
                double roundTripEfficiency = _technologyProfile.RoundTripEfficiency;
                Energy requestedInputEnergy = requestedFlow * resolution * -1;
                Energy availableInputEnergy = roundTripEfficiency == 0
                    ? requestedInputEnergy
                    : (StorageCapacity - initialStorageLevel) * (1 / roundTripEfficiency);
                Energy chargedInputEnergy = Energy.Min(
                    requestedInputEnergy,
                    Energy.Min(PowerCapacity * resolution, availableInputEnergy));
                Energy storedEnergy = chargedInputEnergy * roundTripEfficiency;

                return new StorageOutcome(
                    initialStorageLevel + storedEnergy,
                    chargedInputEnergy / resolution * -1);
            }

            return new StorageOutcome(initialStorageLevel, Power.Zero);
        }
    }

    public sealed record StorageOutcome(Energy FinalStorageLevel, Power DeliveredFlow) { }
}