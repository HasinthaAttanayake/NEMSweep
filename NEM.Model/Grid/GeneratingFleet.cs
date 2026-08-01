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

        internal GenerationEnergyBudget CreateEnergyBudget() =>
            new(GenerationTechnology, NameplateCapacity, _monthlyCapacityFactors);
    }

    internal sealed class GenerationEnergyBudget
    {
        private readonly GenerationTechnology _generationTechnology;
        private readonly Dictionary<DateOnly, double>? _remainingMwhByMonth;

        public GenerationEnergyBudget(
            GenerationTechnology generationTechnology,
            Power nameplateCapacity,
            IReadOnlyDictionary<DateOnly, double>? monthlyCapacityFactors)
        {
            _generationTechnology = generationTechnology;
            _remainingMwhByMonth = monthlyCapacityFactors?.ToDictionary(
                entry => entry.Key,
                entry => nameplateCapacity.Megawatts
                    * DateTime.DaysInMonth(entry.Key.Year, entry.Key.Month)
                    * 24
                    * entry.Value);
        }

        public Power Headroom(
            Power availableCapacity,
            Power generated,
            DateTimeOffset instant,
            TimeSpan resolution)
        {
            Power capacityHeadroom = Power.Max(Power.Zero, availableCapacity - generated);
            if (_remainingMwhByMonth is null)
            {
                return capacityHeadroom;
            }

            double remainingMwh = RemainingFor(instant);
            return Power.Min(
                capacityHeadroom,
                Energy.FromMegawattHours(remainingMwh) / resolution);
        }

        public Power Take(Power requested, DateTimeOffset instant, TimeSpan resolution)
        {
            if (requested < Power.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requested), requested.Megawatts, "Generation cannot be negative.");
            }

            if (_remainingMwhByMonth is null)
            {
                return requested;
            }

            var month = new DateOnly(instant.Year, instant.Month, 1);
            double remainingMwh = RemainingFor(instant);
            Power accepted = Power.Min(
                requested,
                Energy.FromMegawattHours(remainingMwh) / resolution);
            _remainingMwhByMonth[month] = Math.Max(
                0,
                remainingMwh - (accepted * resolution).MegawattHours);
            return accepted;
        }

        private double RemainingFor(DateTimeOffset instant)
        {
            var month = new DateOnly(instant.Year, instant.Month, 1);
            if (_remainingMwhByMonth is null)
            {
                return double.PositiveInfinity;
            }

            if (!_remainingMwhByMonth.TryGetValue(month, out double remainingMwh))
            {
                throw new InvalidOperationException(
                    $"{_generationTechnology} has no energy budget for {month:yyyy-MM}.");
            }

            return remainingMwh;
        }
    }
}