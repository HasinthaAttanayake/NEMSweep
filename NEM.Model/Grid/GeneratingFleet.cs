using NEM.Model.Generation.Solar;
using NEM.Model.Generation.Wind;
using NEM.Model.Series;
using NEM.Model.Units;
using NEM.Model.Weather;

namespace NEM.Model.Grid
{
    /// <summary>A region-wide aggregate of generation sharing one technology type.</summary>
    public sealed class GeneratingFleet
    {
        private readonly IReadOnlyDictionary<DateOnly, double>? _monthlyCapacityFactors;
        private readonly WindPowerCurveSettings _windPowerCurveSettings;

        /// <summary>Validates and creates a generating fleet.</summary>
        /// <param name="generationTechnology">The generation technology this fleet represents.</param>
        /// <param name="nameplateCapacity">Installed nameplate capacity in MW. Must not be negative.</param>
        /// <param name="monthlyCapacityFactors">
        /// Monthly energy budget as a capacity factor, keyed by the first day of each month.
        /// Required for Hydro and rejected for every other technology.
        /// </param>
        /// <param name="windPowerCurveSettings">
        /// Wind power-curve settings. Only valid for a Wind fleet; defaults to
        /// <see cref="WindPowerCurveSettings.Default"/> when a Wind fleet omits it.
        /// </param>
        /// <param name="shortRunMarginalCost">
        /// Cost of the fleet's next MWh generated, in AUD/MWh generated. Derived by
        /// <see cref="NEM.Model.Scenarios.GenerationCostParameters.ShortRunMarginalCostFor"/> at
        /// scenario realisation rather than declared independently here.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Nameplate capacity is negative, or a monthly capacity factor is outside zero to one.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Wind power-curve settings are supplied for a non-Wind fleet, monthly capacity factors
        /// are missing for Hydro or supplied for a non-Hydro fleet, the collection is empty, or a
        /// key is not the first day of a month.
        /// </exception>
        public GeneratingFleet(
            GenerationTechnology generationTechnology,
            Power nameplateCapacity,
            IReadOnlyDictionary<DateOnly, double>? monthlyCapacityFactors = null,
            WindPowerCurveSettings? windPowerCurveSettings = null,
            GenerationEnergyCost shortRunMarginalCost = default)
        {
            if (nameplateCapacity < Power.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nameplateCapacity),
                    nameplateCapacity.Megawatts,
                    "Nameplate capacity cannot be negative.");
            }

            if (windPowerCurveSettings is not null && generationTechnology != GenerationTechnology.Wind)
            {
                throw new ArgumentException(
                    "Wind power-curve settings can only be supplied for a wind fleet.",
                    nameof(windPowerCurveSettings));
            }

            if (generationTechnology == GenerationTechnology.Hydro && monthlyCapacityFactors is null)
            {
                throw new ArgumentException(
                    "Hydro requires monthly capacity factors.",
                    nameof(monthlyCapacityFactors));
            }

            if (monthlyCapacityFactors is not null && generationTechnology != GenerationTechnology.Hydro)
            {
                throw new ArgumentException(
                    "Monthly capacity factors can only be supplied for a hydro fleet.",
                    nameof(monthlyCapacityFactors));
            }

            if (monthlyCapacityFactors is not null)
            {
                if (monthlyCapacityFactors.Count == 0)
                {
                    throw new ArgumentException(
                        "Monthly capacity factors must contain at least one month.",
                        nameof(monthlyCapacityFactors));
                }

                foreach ((DateOnly month, double capacityFactor) in monthlyCapacityFactors)
                {
                    if (month.Day != 1)
                    {
                        throw new ArgumentException(
                            $"Monthly capacity factor key {month:yyyy-MM-dd} must be the first day of a month.",
                            nameof(monthlyCapacityFactors));
                    }

                    if (double.IsNaN(capacityFactor) || capacityFactor < 0 || capacityFactor > 1)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(monthlyCapacityFactors),
                            capacityFactor,
                            $"Monthly capacity factor for {month:yyyy-MM} must be between zero and one.");
                    }
                }
            }

            GenerationTechnology = generationTechnology;
            NameplateCapacity = nameplateCapacity;
            ShortRunMarginalCost = shortRunMarginalCost;
            _monthlyCapacityFactors = monthlyCapacityFactors is null
                ? null
                : new Dictionary<DateOnly, double>(monthlyCapacityFactors);
            _windPowerCurveSettings = windPowerCurveSettings ?? WindPowerCurveSettings.Default;
        }

        /// <summary>The generation technology this fleet represents.</summary>
        public GenerationTechnology GenerationTechnology { get; }
        /// <summary>Installed nameplate capacity in MW.</summary>
        public Power NameplateCapacity { get; }
        /// <summary>Cost of this fleet's next MWh generated, in AUD/MWh generated.</summary>
        public GenerationEnergyCost ShortRunMarginalCost { get; }
        internal IReadOnlyDictionary<DateOnly, double>? MonthlyCapacityFactors =>
            _monthlyCapacityFactors;
        /// <summary>
        /// Whether this fleet's output depends on an intermittent weather resource (Solar or
        /// Wind) rather than being fully controllable subject to fuel or budget constraints.
        /// </summary>
        public bool IsIntermittentRenewable => GenerationTechnology is GenerationTechnology.Solar or GenerationTechnology.Wind; // TODO: move to TechnologyProfile as appropriate

        internal FlowSeries AvailableCapacityFor(
            RegionalResourceProfile? resourceProfile,
            FlowSeries dispatchTimeline)
        {
            FlowSeries availableCapacity;
            if (GenerationTechnology == GenerationTechnology.Solar)
            {
                RegionalResourceProfile resources = RequireResourceProfile(resourceProfile);
                availableCapacity = DualAxisSolarPowerCurve.Calculate(
                    resources.GlobalHorizontalRadiation,
                    resources.DirectNormalRadiation,
                    resources.DiffuseHorizontalRadiation,
                    resources.DryBulbTemperature,
                    resources.SolarZenith,
                    NameplateCapacity);
            }
            else if (GenerationTechnology == GenerationTechnology.Wind)
            {
                availableCapacity = WindPowerCurve.Calculate(
                    RequireResourceProfile(resourceProfile).WindSpeed,
                    NameplateCapacity,
                    _windPowerCurveSettings);
            }
            else
            {
                var values = new double[dispatchTimeline.Length];
                Array.Fill(values, NameplateCapacity.Megawatts);
                availableCapacity = new FlowSeries(
                    dispatchTimeline.Start,
                    dispatchTimeline.Resolution,
                    values);
            }

            dispatchTimeline.RequireAligned(availableCapacity);
            return availableCapacity;
        }

        private RegionalResourceProfile RequireResourceProfile(
            RegionalResourceProfile? resourceProfile) =>
            resourceProfile ?? throw new InvalidOperationException(
                $"{GenerationTechnology} requires a regional resource profile.");

    }
}