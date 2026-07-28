using FluentAssertions;
using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Units;

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
                [Fleet(TechnologyKey.Coal), Fleet(TechnologyKey.Coal)],
                HourlyFlow(100));

            act.Should().Throw<ArgumentException>().WithParameterName("fleets");
        }

        [Fact]
        public void Construction_CopiesAndExposesReadOnlyFleetCollection()
        {
            GeneratingFleet coal = Fleet(TechnologyKey.Coal);
            GeneratingFleet[] fleets = [coal];
            var region = new Region("NSW1", fleets, HourlyFlow(100));

            fleets[0] = Fleet(TechnologyKey.Gas);
            var mutableView = (IList<GeneratingFleet>)region.Fleets;
            var act = () => mutableView[0] = Fleet(TechnologyKey.Gas);

            region.Fleets.Should().ContainSingle().Which.Should().BeSameAs(coal);
            act.Should().Throw<NotSupportedException>();
        }

        private static GeneratingFleet Fleet(TechnologyKey technology) =>
            new(technology, Power.FromMegawatts(100));

        private static FlowSeries HourlyFlow(params double[] megawatts) =>
            new(NemStart, TimeSpan.FromHours(1), megawatts);
    }
}