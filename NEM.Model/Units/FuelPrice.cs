namespace NEM.Model.Units
{
    /// <summary>
    /// Fuel price per unit of thermal energy input, in AUD/GJ thermal.
    /// <para>
    /// Fuel price does not directly price electrical energy delivered. Combining
    /// it with a generator heat rate in GJ/MWh produces the fuel component of an
    /// <see cref="GenerationEnergyCost"/> in AUD/MWh generated.
    /// </para>
    /// </summary>
    public readonly record struct FuelPrice
    {
        /// <summary>The fuel price in AUD per GJ of thermal energy input.</summary>
        public decimal AudPerGjThermal { get; }

        private FuelPrice(decimal audPerGjThermal) => AudPerGjThermal = audPerGjThermal;

        /// <summary>Creates a fuel price from AUD per GJ of thermal energy.</summary>
        public static FuelPrice FromAudPerGjThermal(decimal audPerGjThermal) =>
            new(audPerGjThermal);

        /// <summary>Fuel cost per generated MWh: AUD/MWh = AUD/GJ × GJ/MWh.</summary>
        public GenerationEnergyCost ForHeatRate(HeatRate heatRate) =>
            GenerationEnergyCost.FromAudPerMwhGenerated(
                AudPerGjThermal * DecimalPhysicalBoundary.RequireNonNegativeFinite(
                    heatRate.GigajoulesPerMegawattHour,
                    nameof(heatRate)));
    }
}