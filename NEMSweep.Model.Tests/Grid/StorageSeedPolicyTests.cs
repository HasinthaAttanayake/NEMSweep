using AwesomeAssertions;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Tests.Grid
{
    public sealed class StorageSeedPolicyTests
    {
        [Fact]
        public void SeedFor_Battery_Returns50PercentOfInstalledEnergy()
        {
            Energy seed = StorageSeedPolicy.SeedFor(
                StorageTechnology.Battery,
                Energy.FromMegawattHours(1000));

            seed.Should().Be(Energy.FromMegawattHours(500));
        }

        [Fact]
        public void SeedFor_PumpedHydro_Returns80PercentOfInstalledEnergy()
        {
            Energy seed = StorageSeedPolicy.SeedFor(
                StorageTechnology.PumpedHydro,
                Energy.FromMegawattHours(1000));

            seed.Should().Be(Energy.FromMegawattHours(800));
        }

        [Theory]
        [InlineData(StorageTechnology.Battery)]
        [InlineData(StorageTechnology.PumpedHydro)]
        public void SeedFor_ZeroInstalledCapacity_ReturnsZero(StorageTechnology technology)
        {
            Energy seed = StorageSeedPolicy.SeedFor(technology, Energy.Zero);

            seed.Should().Be(Energy.Zero);
        }
    }
}
