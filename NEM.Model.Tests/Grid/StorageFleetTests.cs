using FluentAssertions;
using NEM.Model.Grid;
using NEM.Model.Units;

namespace NEM.Model.Tests.Grid
{
    public sealed class StorageFleetTests
    {
        private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

        [Fact]
        public void Operate_ChargingAppliesRoundTripEfficiencyToStoredEnergy()
        {
            var fleet = Battery(storageCapacityMwh: 200, powerCapacityMw: 50);

            StorageOutcome outcome = fleet.Operate(
                Energy.Zero,
                Power.FromMegawatts(-20),
                OneHour);

            outcome.FinalStorageLevel.MegawattHours.Should().BeApproximately(17.4, 1e-10);
            outcome.DeliveredFlow.Megawatts.Should().BeApproximately(-20, 1e-10);
        }

        [Fact]
        public void Operate_DischargingDeliversStoredEnergyWithoutApplyingEfficiencyAgain()
        {
            var fleet = Battery(storageCapacityMwh: 200, powerCapacityMw: 50);

            StorageOutcome outcome = fleet.Operate(
                Energy.FromMegawattHours(20),
                Power.FromMegawatts(10),
                OneHour);

            outcome.FinalStorageLevel.Should().Be(Energy.FromMegawattHours(10));
            outcome.DeliveredFlow.Should().Be(Power.FromMegawatts(10));
        }

        [Fact]
        public void Operate_ChargingHonoursPowerCapacity()
        {
            var fleet = Battery(storageCapacityMwh: 40, powerCapacityMw: 10);

            StorageOutcome outcome = fleet.Operate(
                Energy.Zero,
                Power.FromMegawatts(-20),
                OneHour);

            outcome.FinalStorageLevel.MegawattHours.Should().BeApproximately(8.7, 1e-10);
            outcome.DeliveredFlow.Should().Be(Power.FromMegawatts(-10));
        }

        [Fact]
        public void Operate_ChargingHonoursEnergyCapacity()
        {
            var fleet = Battery(storageCapacityMwh: 40, powerCapacityMw: 10);

            StorageOutcome outcome = fleet.Operate(
                Energy.FromMegawattHours(35),
                Power.FromMegawatts(-20),
                OneHour);

            outcome.FinalStorageLevel.Should().Be(fleet.StorageCapacity);
            outcome.DeliveredFlow.Megawatts.Should().BeApproximately(-5 / 0.87, 1e-10);
        }

        [Fact]
        public void Operate_FullChargeDischargeCycleLosesTheRoundTripEfficiencyShareOnce()
        {
            var fleet = Battery(storageCapacityMwh: 200, powerCapacityMw: 200);

            StorageOutcome charged = fleet.Operate(
                Energy.Zero,
                Power.FromMegawatts(-100),
                OneHour);
            StorageOutcome discharged = fleet.Operate(
                charged.FinalStorageLevel,
                Power.FromMegawatts(100),
                OneHour);

            charged.DeliveredFlow.Megawatts.Should().Be(-100);
            discharged.DeliveredFlow.Megawatts.Should().BeApproximately(87, 1e-10);
            discharged.FinalStorageLevel.Should().Be(Energy.Zero);
        }

        [Fact]
        public void Operate_KeepsStateOfChargeWithinBoundsAcrossRandomisedRequests()
        {
            var fleet = Battery(storageCapacityMwh: 100, powerCapacityMw: 20);
            var random = new Random(8765);
            Energy stateOfCharge = Energy.FromMegawattHours(50);

            for (int interval = 0; interval < 1_000; interval++)
            {
                Power requestedFlow = Power.FromMegawatts((random.NextDouble() * 100) - 50);
                StorageOutcome outcome = fleet.Operate(stateOfCharge, requestedFlow, OneHour);

                outcome.FinalStorageLevel.Should().BeGreaterThanOrEqualTo(Energy.Zero);
                outcome.FinalStorageLevel.Should().BeLessThanOrEqualTo(fleet.StorageCapacity);
                stateOfCharge = outcome.FinalStorageLevel;
            }
        }

        [Fact]
        public void Archetypes_UseDifferentPinnedEfficiencyAndFleetDurations()
        {
            StorageTechnologyProfile battery = StorageTechnologyProfile.ProfileFor(StorageTechnology.Battery);
            StorageTechnologyProfile pumpedHydro = StorageTechnologyProfile.ProfileFor(StorageTechnology.PumpedHydro);
            var batteryFleet = Battery(storageCapacityMwh: 40, powerCapacityMw: 10);
            var pumpedHydroFleet = new StorageFleet(
                StorageTechnology.PumpedHydro,
                Energy.FromMegawattHours(120),
                Power.FromMegawatts(10));

            battery.RoundTripEfficiency.Should().Be(0.87);
            pumpedHydro.RoundTripEfficiency.Should().Be(0.78);
            batteryFleet.Duration.Should().Be(TimeSpan.FromHours(4));
            pumpedHydroFleet.Duration.Should().Be(TimeSpan.FromHours(12));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void StorageTechnologyProfile_AllowsInclusiveEfficiencyBounds(double efficiency)
        {
            var profile = new StorageTechnologyProfile(
                technicalLifeYears: 10,
                roundTripEfficiency: efficiency);

            profile.RoundTripEfficiency.Should().Be(efficiency);
        }

        [Theory]
        [InlineData(-0.01)]
        [InlineData(1.01)]
        public void StorageTechnologyProfile_RejectsEfficiencyOutsideInclusiveBounds(double efficiency)
        {
            var act = () => new StorageTechnologyProfile(
                technicalLifeYears: 10,
                roundTripEfficiency: efficiency);

            act.Should().Throw<ArgumentOutOfRangeException>()
                .WithParameterName("roundTripEfficiency");
        }

        private static StorageFleet Battery(double storageCapacityMwh, double powerCapacityMw) =>
            new(
                StorageTechnology.Battery,
            Energy.FromMegawattHours(storageCapacityMwh),
                Power.FromMegawatts(powerCapacityMw));
    }
}