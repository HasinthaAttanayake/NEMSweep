using AwesomeAssertions;
using NEM.Model.Units;

namespace NEM.Model.Tests.Units
{
    public class QuantityExtensionsTests
    {
        [Fact]
        public void PowerSum_AggregatesNameplateCapacities()
        {
            Power[] fleet =
            [
                Power.FromMegawatts(700),
                Power.FromMegawatts(1200),
                Power.FromMegawatts(150),
            ];

            fleet.Sum().Megawatts.Should().BeApproximately(2050, 1e-9);
        }

        [Fact]
        public void PowerSum_IsZeroForEmptyCollection()
        {
            Array.Empty<Power>().Sum().Should().Be(Power.Zero);
        }

        [Fact]
        public void EnergySum_AggregatesAcrossTechnologies()
        {
            Energy[] totals =
            [
                Energy.FromMegawattHours(3700),
                Energy.FromMegawattHours(900),
            ];

            totals.Sum().MegawattHours.Should().BeApproximately(4600, 1e-9);
        }
    }
}