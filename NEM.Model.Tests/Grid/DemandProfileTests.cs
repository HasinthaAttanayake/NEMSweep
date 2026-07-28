using FluentAssertions;
using NEM.Model.Grid;
using NEM.Model.Series;

namespace NEM.Model.Tests.Grid
{
    public sealed class DemandProfileTests
    {
        private static readonly DateTimeOffset NemStart =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(10));

        [Fact]
        public void BaseOnly_BehavesIdenticallyToBareHourlySeries()
        {
            var bareDemand = HourlyFlow(1_000, 1_100);

            var region = new Region("NSW1", bareDemand);

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
                new Dictionary<string, FlowSeries>
                {
                    ["Data centres"] = dataCentreDemand,
                    ["Electrification"] = electrificationDemand,
                });

            profile.TotalDemand[0].Megawatts.Should().Be(1_110);
            profile.TotalDemand[1].Megawatts.Should().Be(1_320);
            profile.AdditiveComponents["Data centres"].Should().BeSameAs(dataCentreDemand);
            profile.AdditiveComponents["Electrification"].Should().BeSameAs(electrificationDemand);
        }

        [Fact]
        public void Construction_ResamplesAllGridDemandToHourlyResolution()
        {
            var baseDemand = HalfHourlyFlow(900, 1_100, 1_200, 1_400);
            var additiveDemand = HalfHourlyFlow(100, 300, 200, 400);

            var profile = new DemandProfile(
                baseDemand,
                new Dictionary<string, FlowSeries> { ["Data centres"] = additiveDemand });

            profile.BaseDemand.Resolution.Should().Be(DemandProfile.Resolution);
            profile.AdditiveComponents["Data centres"].Resolution.Should().Be(DemandProfile.Resolution);
            profile.TotalDemand.Resolution.Should().Be(DemandProfile.Resolution);
            profile.TotalDemand[0].Megawatts.Should().Be(1_200);
            profile.TotalDemand[1].Megawatts.Should().Be(1_600);
        }

        [Fact]
        public void Construction_CopiesAdditiveComponentCollection()
        {
            var components = new Dictionary<string, FlowSeries>
            {
                ["Data centres"] = HourlyFlow(100, 200),
            };
            var profile = new DemandProfile(HourlyFlow(1_000, 1_100), components);

            components.Clear();

            profile.AdditiveComponents.Should().ContainKey("Data centres");
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
                new Dictionary<string, FlowSeries> { ["Data centres"] = shiftedComponent });

            act.Should().Throw<ArgumentException>().WithMessage("*misaligned on start*");
        }

        [Fact]
        public void Construction_RejectsBlankComponentName()
        {
            var act = () => new DemandProfile(
                HourlyFlow(1_000, 1_100),
                new Dictionary<string, FlowSeries> { [" "] = HourlyFlow(100, 200) });

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Construction_RejectsDuplicateComponentNamesIgnoringCase()
        {
            var components = new Dictionary<string, FlowSeries>
            {
                ["Data centres"] = HourlyFlow(100, 200),
                ["DATA CENTRES"] = HourlyFlow(300, 400),
            };

            var act = () => new DemandProfile(HourlyFlow(1_000, 1_100), components);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Region_RejectsBlankRegionId()
        {
            var act = () => new Region(" ", HourlyFlow(1_000, 1_100));

            act.Should().Throw<ArgumentException>();
        }

        private static FlowSeries HourlyFlow(params double[] megawatts) =>
            new(NemStart, TimeSpan.FromHours(1), megawatts);

        private static FlowSeries HalfHourlyFlow(params double[] megawatts) =>
            new(NemStart, TimeSpan.FromMinutes(30), megawatts);
    }
}