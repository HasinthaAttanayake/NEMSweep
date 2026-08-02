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

        public GeneratingFleet(
            GenerationTechnology generationTechnology,
            Power nameplateCapacity,
            IReadOnlyDictionary<DateOnly, double>? monthlyCapacityFactors = null,
            WindPowerCurveSettings? windPowerCurveSettings = null)
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
            _monthlyCapacityFactors = monthlyCapacityFactors is null
                ? null
                : new Dictionary<DateOnly, double>(monthlyCapacityFactors);
            _windPowerCurveSettings = windPowerCurveSettings ?? WindPowerCurveSettings.Default;
        }

        public GenerationTechnology GenerationTechnology { get; }
        public Power NameplateCapacity { get; }
        internal IReadOnlyDictionary<DateOnly, double>? MonthlyCapacityFactors =>
            _monthlyCapacityFactors;
        public ushort ShortRunMarginalCost => (ushort)GenerationTechnology; // TODO: replace with SMRC cost basis B5
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