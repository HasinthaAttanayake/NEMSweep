using FluentAssertions;
using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.Units;

namespace NEM.Model.Tests.Simulation
{
    public sealed class DispatcherTests
    {
        private const int HoursInJuly = 31 * 24;
        private static readonly DateTimeOffset NemStart =
            new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

        [Fact]
        public void Dispatch_HandComputedThreeHourCase_MatchesExactly()
        {
            GeneratingFleet[] fleets =
            [
                Fleet(TechnologyKey.Gas, 50),
                Fleet(TechnologyKey.Coal, 40),
                Fleet(TechnologyKey.Hydro, 30),
                Fleet(TechnologyKey.Wind, 10),
                Fleet(TechnologyKey.Solar, 20),
            ];
            var region = new Region("NSW1", fleets, HourlyFlow(10, 75, 180));

            DispatchOutcome outcome = Dispatcher.Dispatch(region);

            outcome.RegionId.Should().Be("NSW1");
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Solar], 10, 20, 20);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Wind], 0, 10, 10);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Hydro], 0, 30, 30);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Coal], 0, 15, 40);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Gas], 0, 0, 50);
            AssertSeries(outcome.CurtailmentMw, -20, 0, 0);
            AssertSeries(outcome.UnservedMw, 0, 0, 30);
        }

        [Theory]
        [InlineData(7)]
        [InlineData(41)]
        [InlineData(2026)]
        public void Dispatch_FullMonth_PreservesIntervalEnergyBalance(int seed)
        {
            GeneratingFleet[] fleets =
            [
                Fleet(TechnologyKey.Gas, 1_500),
                Fleet(TechnologyKey.Coal, 1_250),
                Fleet(TechnologyKey.Hydro, 1_000),
                Fleet(TechnologyKey.Wind, 750),
                Fleet(TechnologyKey.Solar, 500),
            ];
            var random = new Random(seed);
            double[] demand = Enumerable.Range(0, HoursInJuly)
                .Select(_ => (double)random.Next(0, 6_001))
                .ToArray();
            var region = new Region("NSW1", fleets, HourlyFlow(demand));

            DispatchOutcome outcome = Dispatcher.Dispatch(region);

            for (int hour = 0; hour < HoursInJuly; hour++)
            {
                double residual = demand[hour];
                double dispatched = 0;

                foreach (GeneratingFleet fleet in fleets.OrderBy(fleet => fleet.ShortRunMarginalCost))
                {
                    double fleetOutput = outcome.PerFleetGeneration[fleet.TechnologyKey][hour].Megawatts;
                    double expectedOutput = Math.Min(residual, fleet.NameplateCapacity.Megawatts);

                    fleetOutput.Should().Be(expectedOutput, $"fleet {fleet.TechnologyKey} must follow merit order at hour {hour}");
                    fleetOutput.Should().BeInRange(0, fleet.NameplateCapacity.Megawatts);
                    dispatched += fleetOutput;
                    residual -= expectedOutput;
                }

                double unserved = outcome.UnservedMw[hour].Megawatts;
                double curtailment = outcome.CurtailmentMw[hour].Megawatts;
                double expectedCurtailment = fleets
                    .Where(fleet => fleet.IsIntermittentRenewable)
                    .Sum(fleet => outcome.PerFleetGeneration[fleet.TechnologyKey][hour].Megawatts
                        - fleet.NameplateCapacity.Megawatts);

                (dispatched + unserved).Should().Be(demand[hour], $"energy must balance at hour {hour}");
                unserved.Should().Be(Math.Max(residual, 0));
                curtailment.Should().Be(expectedCurtailment);
                (curtailment < 0 && unserved > 0).Should().BeFalse(
                    $"curtailment and unserved energy cannot co-occur at hour {hour}");
            }
        }

        [Fact]
        public void Dispatch_ZeroDemand_ProducesOnlyNegativeRenewableCurtailment()
        {
            var region = new Region(
                "NSW1",
                [Fleet(TechnologyKey.Coal, 40), Fleet(TechnologyKey.Wind, 10), Fleet(TechnologyKey.Solar, 20)],
                HourlyFlow(0));

            DispatchOutcome outcome = Dispatcher.Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Solar], 0);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Wind], 0);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Coal], 0);
            AssertSeries(outcome.CurtailmentMw, -30);
            AssertSeries(outcome.UnservedMw, 0);
        }

        [Fact]
        public void Dispatch_DemandEqualToCumulativeCapacity_HasNoCurtailmentOrUnserved()
        {
            var region = new Region(
                "NSW1",
                [Fleet(TechnologyKey.Hydro, 30), Fleet(TechnologyKey.Wind, 10), Fleet(TechnologyKey.Solar, 20)],
                HourlyFlow(60));

            DispatchOutcome outcome = Dispatcher.Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Solar], 20);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Wind], 10);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Hydro], 30);
            AssertSeries(outcome.CurtailmentMw, 0);
            AssertSeries(outcome.UnservedMw, 0);
        }

        [Fact]
        public void Dispatch_ZeroCapacityFleet_ProducesZeroGeneration()
        {
            var region = new Region("NSW1", [Fleet(TechnologyKey.Solar, 0)], HourlyFlow(10));

            DispatchOutcome outcome = Dispatcher.Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Solar], 0);
            AssertSeries(outcome.CurtailmentMw, 0);
            AssertSeries(outcome.UnservedMw, 10);
        }

        [Fact]
        public void Dispatch_UsesPerIntervalAvailableGeneration()
        {
            var solar = new GeneratingFleet(
                TechnologyKey.Solar,
                Power.FromMegawatts(20),
                HourlyFlow(0, 10, 20));
            var region = new Region(
                "NSW1",
                [Fleet(TechnologyKey.Coal, 100), solar],
                HourlyFlow(5, 5, 5));

            DispatchOutcome outcome = Dispatcher.Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Solar], 0, 5, 5);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Coal], 5, 0, 0);
            AssertSeries(outcome.CurtailmentMw, 0, -5, -15);
            AssertSeries(outcome.UnservedMw, 0, 0, 0);
        }

        [Fact]
        public void Dispatch_RejectsAvailabilityMisalignedWithDemand()
        {
            var availability = new FlowSeries(
                NemStart.AddHours(1),
                TimeSpan.FromHours(1),
                [10.0]);
            var fleet = new GeneratingFleet(
                TechnologyKey.Solar,
                Power.FromMegawatts(20),
                availability);
            var region = new Region("NSW1", [fleet], HourlyFlow(10));

            var act = () => Dispatcher.Dispatch(region);

            act.Should().Throw<ArgumentException>().WithMessage("*misaligned on start*");
        }

        [Fact]
        public void Dispatch_RejectsNullRegion()
        {
            var act = () => Dispatcher.Dispatch(null!);

            act.Should().Throw<ArgumentNullException>().WithParameterName("region");
        }

        [Fact]
        public void DispatchOutcome_CopiesAndExposesReadOnlyFleetGeneration()
        {
            var generation = new Dictionary<TechnologyKey, FlowSeries>
            {
                [TechnologyKey.Coal] = HourlyFlow(10),
            };
            var outcome = new DispatchOutcome("NSW1", generation, HourlyFlow(0), HourlyFlow(0));

            generation.Clear();
            var mutableView = (IDictionary<TechnologyKey, FlowSeries>)outcome.PerFleetGeneration;
            var act = () => mutableView.Add(TechnologyKey.Gas, HourlyFlow(0));

            outcome.PerFleetGeneration.Should().ContainKey(TechnologyKey.Coal);
            act.Should().Throw<NotSupportedException>();
        }

        private static GeneratingFleet Fleet(TechnologyKey technology, double capacityMw) =>
            new(technology, Power.FromMegawatts(capacityMw));

        private static FlowSeries HourlyFlow(params double[] megawatts) =>
            new(NemStart, TimeSpan.FromHours(1), megawatts);

        private static void AssertSeries(FlowSeries actual, params double[] expected)
        {
            actual.Length.Should().Be(expected.Length);
            for (int index = 0; index < expected.Length; index++)
            {
                actual[index].Megawatts.Should().Be(expected[index]);
            }
        }
    }
}