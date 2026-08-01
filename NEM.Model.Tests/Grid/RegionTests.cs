using FluentAssertions;
using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Units;
using NEM.Model.Weather;

namespace NEM.Model.Tests.Grid
{
    public sealed class RegionTests
    {
        private static readonly DateTimeOffset NemStart =
            new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

        [Fact]
        public void Construction_RejectsNullFleetCollection()
        {
            var act = () => new Region("NSW1", null!, HourlyFlow(100));

            act.Should().Throw<ArgumentNullException>().WithParameterName("fleets");
        }

        [Fact]
        public void Construction_RejectsEmptyFleetCollection()
        {
            var act = () => new Region("NSW1", [], HourlyFlow(100));

            act.Should().Throw<ArgumentException>().WithParameterName("fleets");
        }

        [Fact]
        public void Construction_RejectsNullFleetEntry()
        {
            var act = () => new Region("NSW1", [null!], HourlyFlow(100));

            act.Should().Throw<ArgumentException>().WithParameterName("fleets");
        }

        [Fact]
        public void Construction_RejectsDuplicateTechnologyAggregates()
        {
            var act = () => new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Coal), Fleet(GenerationTechnology.Coal)],
                HourlyFlow(100));

            act.Should().Throw<ArgumentException>().WithParameterName("fleets");
        }

        [Fact]
        public void Construction_CopiesAndExposesReadOnlyFleetCollection()
        {
            GeneratingFleet coal = Fleet(GenerationTechnology.Coal);
            GeneratingFleet[] fleets = [coal];
            var region = new Region("NSW1", fleets, HourlyFlow(100));

            fleets[0] = Fleet(GenerationTechnology.Gas);
            var mutableView = (IList<GeneratingFleet>)region.Fleets;
            var act = () => mutableView[0] = Fleet(GenerationTechnology.Gas);

            region.Fleets.Should().ContainSingle().Which.Should().BeSameAs(coal);
            act.Should().Throw<NotSupportedException>();
        }

        [Theory]
        [InlineData(GenerationTechnology.Solar)]
        [InlineData(GenerationTechnology.Wind)]
        public void Construction_RejectsRenewableFleetWithoutResourceProfile(
            GenerationTechnology technology)
        {
            var act = () => new Region(
                "NSW1",
                [Fleet(technology)],
                HourlyFlow(100));

            act.Should().Throw<ArgumentException>()
                .WithParameterName("resourceProfile")
                .WithMessage("*wind or solar fleets require a resource profile*");
        }

        [Fact]
        public void Construction_RejectsResourceProfileMisalignedWithDemand()
        {
            FlowSeries demand = HourlyFlow(100);
            RegionalResourceProfile resources = ResourceProfile(NemStart.AddHours(1));

            var act = () => new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Wind)],
                demand,
                resourceProfile: resources);

            act.Should().Throw<ArgumentException>().WithMessage("*misaligned on start*");
        }

        private static GeneratingFleet Fleet(GenerationTechnology technology) =>
            new(technology, Power.FromMegawatts(100));

        private static FlowSeries HourlyFlow(params double[] megawatts) =>
            new(NemStart, TimeSpan.FromHours(1), megawatts);

        private static RegionalResourceProfile ResourceProfile(DateTimeOffset start)
        {
            var values = new[] { 0.0 };
            return new RegionalResourceProfile(
                TraceSeries.GlobalHorizontalRadiation(start, TimeSpan.FromHours(1), values),
                TraceSeries.DirectNormalRadiation(start, TimeSpan.FromHours(1), values),
                TraceSeries.DiffuseHorizontalRadiation(start, TimeSpan.FromHours(1), values),
                SolarZenithSeries.Calculate(
                    start,
                    TimeSpan.FromHours(1),
                    1,
                    latitude: -33.8688,
                    longitude: 151.2093),
                TraceSeries.DryBulbTemperature(start, TimeSpan.FromHours(1), values),
                TraceSeries.WindSpeed(start, TimeSpan.FromHours(1), values, 10));
        }
    }
}