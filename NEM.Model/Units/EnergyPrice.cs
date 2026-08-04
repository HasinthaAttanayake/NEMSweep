namespace NEM.Model.Units
{
    /// <summary>
    /// Money per unit of energy delivered to load, in AUD/MWh delivered.
    /// <para>
    /// This is the output unit of system levelised cost of energy (SLCoE). It is
    /// deliberately distinct from <see cref="EnergyCapacityCost"/>, which has the
    /// same unit string but prices storage capacity built rather than energy
    /// delivered. The two types have no shared cost abstraction and cannot be
    /// substituted for one another.
    /// </para>
    /// </summary>
    public readonly record struct EnergyPrice
    {
        /// <summary>The price in AUD per MWh delivered to load.</summary>
        public decimal AudPerMwhDelivered { get; }

        private EnergyPrice(decimal audPerMwhDelivered) =>
            AudPerMwhDelivered = audPerMwhDelivered;

        /// <summary>Creates a price from AUD per MWh delivered to load.</summary>
        public static EnergyPrice FromAudPerMwhDelivered(decimal audPerMwhDelivered) =>
            new(audPerMwhDelivered);

        /// <summary>
        /// Cost of delivered energy: AUD = AUD/MWh delivered × MWh delivered.
        /// Delivered energy must be non-negative and finite.
        /// </summary>
        public Money For(Energy deliveredEnergy) => Money.FromAud(
            AudPerMwhDelivered * DecimalPhysicalBoundary.RequireNonNegativeFinite(
                deliveredEnergy.MegawattHours,
                nameof(deliveredEnergy)));
    }
}