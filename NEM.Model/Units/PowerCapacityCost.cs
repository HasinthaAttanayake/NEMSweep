namespace NEM.Model.Units
{
    /// <summary>
    /// Capital cost per unit of generation or storage power capacity, in AUD/MW.
    /// <para>
    /// Source values quoted in AUD/kW must be converted to AUD/MW before this
    /// quantity is created. It multiplies a physical MW rating and returns
    /// <see cref="Money"/>; raw decimal and double values are not combined by callers.
    /// </para>
    /// </summary>
    public readonly record struct PowerCapacityCost
    {
        /// <summary>The capital cost in AUD per MW of power capacity built.</summary>
        public decimal AudPerMwCapacity { get; }

        private PowerCapacityCost(decimal audPerMwCapacity) =>
            AudPerMwCapacity = audPerMwCapacity;

        /// <summary>Creates a capacity cost from AUD per MW of capacity built.</summary>
        public static PowerCapacityCost FromAudPerMwCapacity(decimal audPerMwCapacity) =>
            new(audPerMwCapacity);

        /// <summary>
        /// Cost of power capacity: AUD = AUD/MW capacity × MW built. Capacity must
        /// be non-negative and finite.
        /// </summary>
        public Money For(Power capacity) => Money.FromAud(
            AudPerMwCapacity * DecimalPhysicalBoundary.RequireNonNegativeFinite(
                capacity.Megawatts,
                nameof(capacity)));
    }
}