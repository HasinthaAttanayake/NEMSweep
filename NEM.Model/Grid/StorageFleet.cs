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
            ValidateState(initialStorageLevel, resolution, nameof(initialStorageLevel));

            if (requestedFlow > Power.Zero)
            {
                Power deliveredFlow = Power.Min(
                    requestedFlow,
                    DischargeHeadroom(initialStorageLevel, resolution));
                Energy dischargedEnergy = deliveredFlow * resolution;

                return new StorageOutcome(
                    initialStorageLevel - dischargedEnergy,
                    deliveredFlow);
            }

            if (requestedFlow < Power.Zero)
            {
                double roundTripEfficiency = _technologyProfile.RoundTripEfficiency;
                Power chargedInput = Power.Min(
                    requestedFlow * -1,
                    ChargeHeadroom(initialStorageLevel, resolution));
                Energy chargedInputEnergy = chargedInput * resolution;
                Energy storedEnergy = chargedInputEnergy * roundTripEfficiency;

                return new StorageOutcome(
                    initialStorageLevel + storedEnergy,
                    chargedInputEnergy / resolution * -1);
            }

            return new StorageOutcome(initialStorageLevel, Power.Zero);
        }

        internal Power ChargeHeadroom(Energy storageLevel, TimeSpan resolution)
        {
            ValidateState(storageLevel, resolution, nameof(storageLevel));
            double roundTripEfficiency = _technologyProfile.RoundTripEfficiency;
            if (roundTripEfficiency == 0)
            {
                return PowerCapacity;
            }

            Power energyHeadroom = ((StorageCapacity - storageLevel) * (1 / roundTripEfficiency))
                / resolution;
            return Power.Min(PowerCapacity, energyHeadroom);
        }

        internal Power DischargeHeadroom(Energy storageLevel, TimeSpan resolution)
        {
            ValidateState(storageLevel, resolution, nameof(storageLevel));
            return Power.Min(PowerCapacity, storageLevel / resolution);
        }

        private void ValidateState(
            Energy storageLevel,
            TimeSpan resolution,
            string storageLevelParameterName)
        {
            if (storageLevel < Energy.Zero || storageLevel > StorageCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    storageLevelParameterName,
                    storageLevel.MegawattHours,
                    "Storage level must be within storage capacity.");
            }

            if (resolution <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resolution), resolution, "Resolution must be positive.");
            }
        }
    }

    public sealed record StorageOutcome(Energy FinalStorageLevel, Power DeliveredFlow) { }
}