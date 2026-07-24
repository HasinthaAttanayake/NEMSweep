using FluentAssertions;
using NEM.Model.Units;

namespace NEM.Model.Tests.Units
{
    public class EnergyTests
    {
        private const double Tolerance = 1e-9;

        [Fact]
        public void From_ComputesMegawattHoursFromHalfHourlyPower()
        {
            Energy energy = Energy.From(Power.FromMegawatts(7400), TimeSpan.FromMinutes(30));

            energy.MegawattHours.Should().BeApproximately(3700, Tolerance);
        }

        [Fact]
        public void From_EqualsPowerNumericallyForOneHour()
        {
            Power power = Power.FromMegawatts(7400);

            Energy energy = Energy.From(power, TimeSpan.FromHours(1));

            energy.MegawattHours.Should().BeApproximately(power.Megawatts, Tolerance);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void FromMegawattHours_RejectsNonFiniteValue(double value)
        {
            var act = () => Energy.FromMegawattHours(value);

            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-30)]
        public void From_RejectsNonPositiveInterval(int minutes)
        {
            var act = () => Energy.From(Power.FromMegawatts(500), TimeSpan.FromMinutes(minutes));

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void DividedByInterval_ProducesAveragePower()
        {
            Power power = Energy.FromMegawattHours(3700) / TimeSpan.FromMinutes(30);

            power.Megawatts.Should().BeApproximately(7400, Tolerance);
        }

        [Fact]
        public void DividedByPower_ProducesStorageDuration()
        {
            TimeSpan duration = Energy.FromMegawattHours(500) / Power.FromMegawatts(250);

            duration.TotalHours.Should().BeApproximately(2, Tolerance);
        }

        [Fact]
        public void DividedByPower_IsZeroWhenBothAreZero()
        {
            (Energy.Zero / Power.Zero).Should().Be(TimeSpan.Zero);
        }

        [Fact]
        public void DividedByPower_ThrowsWhenPowerIsZeroButEnergyIsNot()
        {
            var act = () => Energy.FromMegawattHours(100) / Power.Zero;

            act.Should().Throw<DivideByZeroException>();
        }

        [Fact]
        public void DividedByEnergy_ProducesDimensionlessShare()
        {
            double share = Energy.FromMegawattHours(3000) / Energy.FromMegawattHours(12000);

            share.Should().BeApproximately(0.25, Tolerance);
        }

        [Fact]
        public void DividedByEnergy_ThrowsWhenDenominatorIsZero()
        {
            var act = () => Energy.FromMegawattHours(100) / Energy.Zero;

            act.Should().Throw<DivideByZeroException>();
        }

        [Fact]
        public void Comparison_SupportsMinAndMax()
        {
            Energy lower = Energy.FromMegawattHours(3000);
            Energy higher = Energy.FromMegawattHours(5000);

            Energy.Min(lower, higher).Should().Be(lower);
            Energy.Max(lower, higher).Should().Be(higher);
        }

        [Fact]
        public void DividedByPower_ThrowsWhenDurationWouldBeNegative()
        {
            var act = () => Energy.FromMegawattHours(-100) / Power.FromMegawatts(50);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void DividedByPower_ThrowsWhenPowerRatingIsNegative()
        {
            var act = () => Energy.FromMegawattHours(-100) / Power.FromMegawatts(-50);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}