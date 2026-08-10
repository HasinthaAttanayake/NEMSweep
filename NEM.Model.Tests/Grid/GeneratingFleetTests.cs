using AwesomeAssertions;
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

    }
}