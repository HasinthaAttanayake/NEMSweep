using FluentAssertions;
using NEM.Model.Units;

namespace NEM.Model.Tests.Units
{
    public class PowerTests
    {
        private const double Tolerance = 1e-9;

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void FromMegawatts_RejectsNonFiniteValue(double value)
        {
            var act = () => Power.FromMegawatts(value);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void FromMegawatts_AcceptsNegativeValueBecauseFlowsAreSigned()
        {
            Power power = Power.FromMegawatts(-500);

            power.Megawatts.Should().BeApproximately(-500, Tolerance);
        }

        [Fact]
        public void Subtraction_ProducesSignedResidualDemand()
        {
            Power residual = Power.FromMegawatts(6000) - Power.FromMegawatts(6500);

            residual.Megawatts.Should().BeApproximately(-500, Tolerance);
        }

        [Fact]
        public void Scaling_MultipliesByDimensionlessFactor()
        {
            Power scaled = Power.FromMegawatts(400) * 1.5;

            scaled.Megawatts.Should().BeApproximately(600, Tolerance);
        }

        [Fact]
        public void Comparison_SupportsMinMaxAndOrdering()
        {
            Power residual = Power.FromMegawatts(800);
            Power available = Power.FromMegawatts(500);

            (residual > available).Should().BeTrue();
            Power.Min(residual, available).Should().Be(available);
            Power.Max(residual, available).Should().Be(residual);
        }

        [Fact]
        public void TimesTimeSpan_ProducesEnergy()
        {
            Energy energy = Power.FromMegawatts(7400) * TimeSpan.FromMinutes(30);

            energy.MegawattHours.Should().BeApproximately(3700, Tolerance);
        }
    }
}