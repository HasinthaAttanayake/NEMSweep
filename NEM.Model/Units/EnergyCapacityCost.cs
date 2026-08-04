namespace NEM.Model.Units
{
    /// <summary>
    /// Capital cost per unit of storage energy capacity built, in AUD/MWh storage.
    /// <para>
    /// This quantity prices the MWh rating of a storage asset; it does not price
    /// energy delivered to load. It therefore remains a separate type from
    /// <see cref="EnergyPrice"/> even though both are commonly written AUD/MWh.
    /// </para>
    /// </summary>
    public readonly record struct EnergyCapacityCost
    {
        /// <summary>The capital cost in AUD per MWh of storage capacity built.</summary>
        public decimal AudPerMwhStorage { get; }

        private EnergyCapacityCost(decimal audPerMwhStorage) =>
            AudPerMwhStorage = audPerMwhStorage;

        /// <summary>Creates a capacity cost from AUD per MWh of storage built.</summary>
        public static EnergyCapacityCost FromAudPerMwhStorage(decimal audPerMwhStorage) =>
            new(audPerMwhStorage);

        /// <summary>
        /// Cost of storage energy capacity: AUD = AUD/MWh storage × MWh built.
        /// Storage capacity must be non-negative and finite.
        /// </summary>
        public Money For(Energy storageCapacity) => Money.FromAud(
            AudPerMwhStorage * DecimalPhysicalBoundary.RequireNonNegativeFinite(
                storageCapacity.MegawattHours,
                nameof(storageCapacity)));
    }
}