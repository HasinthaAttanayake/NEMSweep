using AwesomeAssertions;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Series;
using NEMSweep.Model.Units;
using NEMSweep.Model.Weather;

namespace NEMSweep.Model.Tests.Grid
{
    public sealed class GeneratingFleetTests
    {
        private static readonly DateTimeOffset NemStart =
            new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

        [Fact]
        public void Construction_RejectsNegativeNameplateCapacity()
        {
            var act = () => new GeneratingFleet(
                GenerationTechnology.Coal,
                Power.FromMegawatts(-1));

            act.Should().Throw<ArgumentOutOfRangeException>()
                .WithParameterName("nameplateCapacity");
        }

        [Fact]
        public void Construction_AllowsZeroNameplateCapacity()
        {
            var fleet = new GeneratingFleet(GenerationTechnology.Solar, Power.Zero);

            fleet.NameplateCapacity.Should().Be(Power.Zero);
        }

        [Fact]
        public void Construction_RejectsHydroWithoutMonthlyCapacityFactors()
        {
            var act = () => new GeneratingFleet(
                GenerationTechnology.Hydro,
                Power.FromMegawatts(100));

            act.Should().Throw<ArgumentException>()
                .WithParameterName("monthlyCapacityFactors")
                .WithMessage("*Hydro requires monthly capacity factors*");
        }

        [Theory]
        [InlineData(-0.01)]
        [InlineData(1.01)]
        [InlineData(double.NaN)]
        public void Construction_RejectsInvalidMonthlyCapacityFactor(double capacityFactor)
        {
            var act = () => new GeneratingFleet(
                GenerationTechnology.Hydro,
                Power.FromMegawatts(100),
                monthlyCapacityFactors: new Dictionary<DateOnly, double>
                {
                    [new DateOnly(2026, 7, 1)] = capacityFactor,
                });

            act.Should().Throw<ArgumentOutOfRangeException>()
                .WithParameterName("monthlyCapacityFactors");
        }

        [Fact]
        public void Construction_RejectsMonthlyCapacityFactorsForNonHydroFleet()
        {
            var act = () => new GeneratingFleet(
                GenerationTechnology.Wind,
                Power.FromMegawatts(100),
                monthlyCapacityFactors: new Dictionary<DateOnly, double>
                {
                    [new DateOnly(2026, 7, 1)] = 0.5,
                });

            act.Should().Throw<ArgumentException>()
                .WithParameterName("monthlyCapacityFactors")
                .WithMessage("*only be supplied for a hydro fleet*");
        }


        /// <summary>
        /// The reason the cache exists: a storage sizing search re-dispatches the same fleets
        /// over the same weather many times, rebuilding an equal but distinct timeline each
        /// pass, so the key has to match on timeline shape rather than reference.
        /// </summary>
        [Fact]
        public void AvailableCapacityFor_ReusesOneSeries_AcrossEqualButDistinctTimelines()
        {
            var fleet = new GeneratingFleet(GenerationTechnology.Coal, Power.FromMegawatts(100));

            FlowSeries first = fleet.AvailableCapacityFor(null, HourlyFlow(1, 2, 3));
            FlowSeries second = fleet.AvailableCapacityFor(null, HourlyFlow(4, 5, 6));

            second.Should().BeSameAs(first);
        }

        [Fact]
        public void AvailableCapacityFor_RebuildsTheSeries_WhenTheTimelineStartMoves()
        {
            var fleet = new GeneratingFleet(GenerationTechnology.Coal, Power.FromMegawatts(100));

            FlowSeries first = fleet.AvailableCapacityFor(null, HourlyFlow(1, 2, 3));
            FlowSeries second = fleet.AvailableCapacityFor(
                null,
                new FlowSeries(NemStart.AddDays(1), TimeSpan.FromHours(1), [1, 2, 3]));

            second.Should().NotBeSameAs(first);
            second.Start.Should().Be(NemStart.AddDays(1));
        }

        [Fact]
        public void AvailableCapacityFor_RebuildsTheSeries_WhenTheTimelineResolutionChanges()
        {
            var fleet = new GeneratingFleet(GenerationTechnology.Coal, Power.FromMegawatts(100));

            FlowSeries first = fleet.AvailableCapacityFor(null, HourlyFlow(1, 2, 3));
            FlowSeries second = fleet.AvailableCapacityFor(
                null,
                new FlowSeries(NemStart, TimeSpan.FromMinutes(30), [1, 2, 3]));

            second.Should().NotBeSameAs(first);
            second.Resolution.Should().Be(TimeSpan.FromMinutes(30));
        }

        [Fact]
        public void AvailableCapacityFor_RebuildsTheSeries_WhenTheTimelineLengthChanges()
        {
            var fleet = new GeneratingFleet(GenerationTechnology.Coal, Power.FromMegawatts(100));

            FlowSeries first = fleet.AvailableCapacityFor(null, HourlyFlow(1, 2, 3));
            FlowSeries second = fleet.AvailableCapacityFor(null, HourlyFlow(1, 2, 3, 4));

            second.Should().NotBeSameAs(first);
            second.Length.Should().Be(4);
        }

        [Fact]
        public void AvailableCapacityFor_ReusesTheSolarCurve_ForTheSameProfileInstance()
        {
            var fleet = new GeneratingFleet(GenerationTechnology.Solar, Power.FromMegawatts(100));
            RegionalResourceProfile profile = ResourceProfile();

            FlowSeries first = fleet.AvailableCapacityFor(profile, HourlyFlow(1, 2, 3));
            FlowSeries second = fleet.AvailableCapacityFor(profile, HourlyFlow(4, 5, 6));

            second.Should().BeSameAs(first);
        }

        /// <summary>
        /// The profile is keyed by reference, so an equal but distinct profile has to miss.
        /// Returning the previous curve here would hand a pass a stale year of weather.
        /// </summary>
        [Fact]
        public void AvailableCapacityFor_RebuildsTheSolarCurve_ForADistinctProfileInstance()
        {
            var fleet = new GeneratingFleet(GenerationTechnology.Solar, Power.FromMegawatts(100));

            FlowSeries first = fleet.AvailableCapacityFor(ResourceProfile(), HourlyFlow(1, 2, 3));
            FlowSeries second = fleet.AvailableCapacityFor(ResourceProfile(), HourlyFlow(1, 2, 3));

            second.Should().NotBeSameAs(first);
            Enumerable.Range(0, second.Length).Select(index => second[index])
                .Should().Equal(Enumerable.Range(0, first.Length).Select(index => first[index]));
        }

        private static FlowSeries HourlyFlow(params double[] megawatts) =>
            new(NemStart, TimeSpan.FromHours(1), megawatts);

        private static RegionalResourceProfile ResourceProfile()
        {
            var values = new[] { 0.0, 0.0, 0.0 };
            TimeSpan hourly = TimeSpan.FromHours(1);
            return new RegionalResourceProfile(
                TraceSeries.GlobalHorizontalRadiation(NemStart, hourly, values),
                TraceSeries.DirectNormalRadiation(NemStart, hourly, values),
                TraceSeries.DiffuseHorizontalRadiation(NemStart, hourly, values),
                SolarZenithSeries.Calculate(
                    NemStart,
                    hourly,
                    values.Length,
                    latitude: -33.8688,
                    longitude: 151.2093),
                TraceSeries.DryBulbTemperature(NemStart, hourly, values),
                TraceSeries.WindSpeed(NemStart, hourly, values, 10));
        }


    }
}