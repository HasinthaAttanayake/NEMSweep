using FluentAssertions;
using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Units;

namespace NEM.Model.Tests.Grid
{
    public sealed class GeneratingFleetTests
    {
        private static readonly DateTimeOffset NemStart =
            new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

        [Fact]
        public void Construction_RejectsNegativeNameplateCapacity()
        {
            var act = () => new GeneratingFleet(
                TechnologyKey.Coal,
                Power.FromMegawatts(-1));

            act.Should().Throw<ArgumentOutOfRangeException>()
                .WithParameterName("nameplateCapacity");
        }

        [Fact]
        public void Construction_AllowsZeroNameplateCapacity()
        {
            var fleet = new GeneratingFleet(TechnologyKey.Solar, Power.Zero);

            fleet.NameplateCapacity.Should().Be(Power.Zero);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(101)]
        public void Construction_RejectsAvailabilityOutsideNameplateBounds(double availableMw)
        {
            var availability = new FlowSeries(
                NemStart,
                TimeSpan.FromHours(1),
                [availableMw]);

            var act = () => new GeneratingFleet(
                TechnologyKey.Wind,
                Power.FromMegawatts(100),
                availability);

            act.Should().Throw<ArgumentOutOfRangeException>()
                .WithParameterName("availableGeneration");
        }
    }
}