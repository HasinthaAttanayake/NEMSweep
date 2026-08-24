using AwesomeAssertions;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Series;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Tests.Grid
{
    public sealed class DemandProfileTests
    {
        private static readonly DateTimeOffset NemStart =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(10));

        [Fact]
        public void BaseOnly_BehavesIdenticallyToBareHourlySeries()
        {
            var bareDemand = HourlyFlow(1_000, 1_100);

            var region = new Region("NSW1", [TestFleet()], bareDemand);

            region.Demand.BaseDemand.Should().BeSameAs(bareDemand);
            region.Demand.TotalDemand.Should().BeSameAs(bareDemand);
            region.Demand.AdditiveComponents.Should().BeEmpty();
        }

        [Fact]
        public void TotalDemand_EqualsSumOfBaseAndAllAdditiveComponents()
        {
            var baseDemand = HourlyFlow(1_000, 1_100);
            var dataCentreDemand = HourlyFlow(100, 200);
            var electrificationDemand = HourlyFlow(10, 20);

            var profile = new DemandProfile(
                baseDemand,
                [
                    new DemandComponent("Data centres", dataCentreDemand),
                    new DemandComponent("Electrification", electrificationDemand),
                ]);

            profile.TotalDemand[0].Megawatts.Should().Be(1_110);
            profile.TotalDemand[1].Megawatts.Should().Be(1_320);
            profile.AdditiveComponents.Should().ContainEquivalentOf(
                new DemandComponent("Data centres", dataCentreDemand));
            profile.AdditiveComponents.Should().ContainEquivalentOf(
                new DemandComponent("Electrification", electrificationDemand));
        }

        [Fact]
        public void TotalDemand_IncludesConstantComponentInEveryInterval()
        {
            var baseDemand = HourlyFlow(1_000, 1_100);
            var profile = new DemandProfile(
                baseDemand,
                [new DemandComponent("Firm load", HourlyFlow(500, 500))]);

            profile.TotalDemand[0].Megawatts.Should().Be(baseDemand[0].Megawatts + 500);
            profile.TotalDemand[1].Megawatts.Should().Be(baseDemand[1].Megawatts + 500);
        }

        [Fact]
        public void Construction_ResamplesBaseAndComponentsToHourlyResolution()
        {
            var baseDemand = HalfHourlyFlow(900, 1_100, 1_200, 1_400);
            var additiveDemand = HalfHourlyFlow(100, 300, 200, 400);

            var profile = new DemandProfile(
                baseDemand,
                [new DemandComponent("Data centres", additiveDemand)]);

            profile.BaseDemand.Resolution.Should().Be(DemandProfile.Resolution);
            profile.AdditiveComponents.Single().Demand.Resolution.Should().Be(DemandProfile.Resolution);
            profile.TotalDemand.Resolution.Should().Be(DemandProfile.Resolution);
            profile.TotalDemand[0].Megawatts.Should().Be(1_200);
            profile.TotalDemand[1].Megawatts.Should().Be(1_600);
        }

        [Fact]
        public void Construction_CopiesAdditiveComponentCollection()
        {
            var component = new DemandComponent("Data centres", HourlyFlow(100, 200));
            var components = new List<DemandComponent> { component };
            var profile = new DemandProfile(HourlyFlow(1_000, 1_100), components);

            components.Clear();

            profile.AdditiveComponents.Should().Contain(component);
        }

        [Fact]
        public void Construction_RejectsMisalignedComponentAfterHourlyResampling()
        {
            var baseDemand = HourlyFlow(1_000, 1_100);
            var shiftedComponent = new FlowSeries(
                NemStart.AddHours(1),
                TimeSpan.FromHours(1),
                [100, 200]);

            var act = () => new DemandProfile(
                baseDemand,
                [new DemandComponent("Data centres", shiftedComponent)]);

            act.Should().Throw<ArgumentException>()
                .WithMessage("*Data centres*misaligned on start*");
        }

        [Fact]
        public void Construction_RejectsDuplicateComponentNamesIgnoringCase()
        {
            var components = new List<DemandComponent>
            {
                new("Data centres", HourlyFlow(100, 200)),
                new("DATA CENTRES", HourlyFlow(300, 400)),
            };

            var act = () => new DemandProfile(HourlyFlow(1_000, 1_100), components);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Construction_RejectsNegativeAdditiveComponent()
        {
            var act = () => new DemandProfile(
                HourlyFlow(100, 100),
                [new DemandComponent("Data centres", HourlyFlow(100, -1))]);

            act.Should().Throw<ArgumentOutOfRangeException>()
                .WithParameterName("demand")
                .WithMessage("*Data centres*cannot be negative at index 1*");
        }

        [Fact]
        public void DemandComponent_RejectsNegativeHalfHourlyValueBeforeResampling()
        {
            var act = () => new DemandComponent(
                "Data centres",
                HalfHourlyFlow(-100, 100));

            act.Should().Throw<ArgumentOutOfRangeException>()
                .WithParameterName("demand")
                .WithMessage("*Data centres*cannot be negative at index 0*");
        }

        [Fact]
        public void Region_RejectsBlankRegionId()
        {
            var act = () => new Region(" ", [TestFleet()], HourlyFlow(1_000, 1_100));

            act.Should().Throw<ArgumentException>();
        }

        private static FlowSeries HourlyFlow(params double[] megawatts) =>
            new(NemStart, TimeSpan.FromHours(1), megawatts);

        private static FlowSeries HalfHourlyFlow(params double[] megawatts) =>
            new(NemStart, TimeSpan.FromMinutes(30), megawatts);

        private static GeneratingFleet TestFleet() =>
            new(GenerationTechnology.Coal, Power.FromMegawatts(1_000));
    }
}