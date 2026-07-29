using FluentAssertions;
using NEM.Model.Generation.Solar;
using NEM.Model.Generation.Wind;
using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.Units;
using NEM.Model.Weather;

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
            FlowSeries demand = HourlyFlowAt(NemStart.AddHours(12), 10, 75, 180);
            var region = new Region(
                "NSW1",
                fleets,
                demand,
                resourceProfile: RegionalResources(demand));

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
            FlowSeries demandSeries = HourlyFlow(demand);
            RegionalResourceProfile resources = RegionalResources(demandSeries);
            var region = new Region(
                "NSW1",
                fleets,
                demandSeries,
                resourceProfile: resources);
            var availableByFleet = fleets.ToDictionary(
                fleet => fleet.TechnologyKey,
                fleet => ExpectedAvailableCapacity(fleet, resources, demandSeries));

            DispatchOutcome outcome = Dispatcher.Dispatch(region);

            for (int hour = 0; hour < HoursInJuly; hour++)
            {
                double residual = demand[hour];
                double dispatched = 0;

                foreach (GeneratingFleet fleet in fleets.OrderBy(fleet => fleet.ShortRunMarginalCost))
                {
                    double fleetOutput = outcome.PerFleetGeneration[fleet.TechnologyKey][hour].Megawatts;
                    double available = availableByFleet[fleet.TechnologyKey][hour].Megawatts;
                    double expectedOutput = Math.Min(residual, available);

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
                        - availableByFleet[fleet.TechnologyKey][hour].Megawatts);

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
            FlowSeries demand = HourlyFlowAt(NemStart.AddHours(12), 0);
            var region = new Region(
                "NSW1",
                [Fleet(TechnologyKey.Coal, 40), Fleet(TechnologyKey.Wind, 10), Fleet(TechnologyKey.Solar, 20)],
                demand,
                resourceProfile: RegionalResources(demand));

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
            FlowSeries demand = HourlyFlowAt(NemStart.AddHours(12), 60);
            var region = new Region(
                "NSW1",
                [Fleet(TechnologyKey.Hydro, 30), Fleet(TechnologyKey.Wind, 10), Fleet(TechnologyKey.Solar, 20)],
                demand,
                resourceProfile: RegionalResources(demand));

            DispatchOutcome outcome = Dispatcher.Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Solar], 20);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Wind], 10);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Hydro], 30);
            AssertSeries(outcome.CurtailmentMw, 0);
            AssertSeries(outcome.UnservedMw, 0);
        }

        [Fact]
        public void Dispatch_FullYear_HydroGenerationCannotExceedCapacityFactorBudgets()
        {
            DateTimeOffset start = NemStart;
            DateTimeOffset end = start.AddYears(1);
            int hours = (int)(end - start).TotalHours;
            var monthlyCapacityFactors = Enumerable.Range(0, 12).ToDictionary(
                offset => DateOnly.FromDateTime(start.AddMonths(offset).Date),
                offset => 100.0 / (50 * DateTime.DaysInMonth(
                    start.AddMonths(offset).Year,
                    start.AddMonths(offset).Month) * 24));
            var hydro = new GeneratingFleet(
                TechnologyKey.Hydro,
                Power.FromMegawatts(50),
                monthlyCapacityFactors: monthlyCapacityFactors);
            var region = new Region(
                "NSW1",
                [hydro],
                new FlowSeries(start, TimeSpan.FromHours(1), Enumerable.Repeat(50.0, hours).ToArray()));

            DispatchOutcome outcome = Dispatcher.Dispatch(region);

            outcome.PerFleetGeneration[TechnologyKey.Hydro].Integrate()
                .Should().Be(Energy.FromMegawattHours(1_200));
        }

        [Fact]
        public void Dispatch_HydroBudgetMissingForDemandMonth_Throws()
        {
            var hydro = new GeneratingFleet(
                TechnologyKey.Hydro,
                Power.FromMegawatts(50),
                monthlyCapacityFactors: new Dictionary<DateOnly, double>
                {
                    [new DateOnly(2026, 6, 1)] = 0.5,
                });
            var region = new Region("NSW1", [hydro], HourlyFlow(50));

            var act = () => Dispatcher.Dispatch(region);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Hydro has no energy budget for 2026-07*");
        }

        [Fact]
        public void Dispatch_ZeroCapacityFleet_ProducesZeroGeneration()
        {
            FlowSeries demand = HourlyFlow(10);
            var region = new Region(
                "NSW1",
                [Fleet(TechnologyKey.Solar, 0)],
                demand,
                resourceProfile: RegionalResources(demand));

            DispatchOutcome outcome = Dispatcher.Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Solar], 0);
            AssertSeries(outcome.CurtailmentMw, 0);
            AssertSeries(outcome.UnservedMw, 10);
        }

        [Fact]
        public void Dispatch_WindCapacityIsDerivedFromRegionResourceTrace()
        {
            var demand = HourlyFlow(100, 100, 100);
            var resources = RegionalResources(
                demand,
                windMetresPerSecond:
                [
                    WindPowerCurve.CutInWindSpeedMetresPerSecond - 0.01,
                    WindPowerCurve.RatedWindSpeedMetresPerSecond,
                    WindPowerCurve.DefaultCutOutWindSpeedMetresPerSecond + 0.01,
                ]);
            var region = new Region(
                "NSW1",
                [Fleet(TechnologyKey.Wind, 10)],
                demand,
                resourceProfile: resources);

            DispatchOutcome outcome = Dispatcher.Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Wind], 0, 10, 0);
            AssertSeries(outcome.UnservedMw, 100, 90, 100);
        }

        [Fact]
        public void Dispatch_SubHourlyDemandAlignsResourcesToNormalizedTimeline()
        {
            var subHourlyDemand = new FlowSeries(
                NemStart,
                TimeSpan.FromMinutes(30),
                [100, 100, 100, 100]);
            FlowSeries dispatchTimeline = HourlyFlow(0, 0);
            var region = new Region(
                "NSW1",
                [Fleet(TechnologyKey.Wind, 10)],
                subHourlyDemand,
                resourceProfile: RegionalResources(dispatchTimeline));

            DispatchOutcome outcome = Dispatcher.Dispatch(region);

            region.Demand.BaseDemand.Resolution.Should().Be(DemandProfile.Resolution);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Wind], 10, 10);
            AssertSeries(outcome.UnservedMw, 90, 90);
        }

        [Fact]
        public void Dispatch_SolarCapacityIsDerivedFromRegionResourceTraces()
        {
            FlowSeries demand = HourlyFlowAt(NemStart.AddHours(12), 200, 200, 200);
            var resources = RegionalResources(
                demand,
                directNormalRadiation: [0, 1_000, 2_000]);
            var region = new Region(
                "NSW1",
                [Fleet(TechnologyKey.Solar, 100)],
                demand,
                resourceProfile: resources);

            DispatchOutcome outcome = Dispatcher.Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Solar], 0, 95, 100);
            AssertSeries(outcome.UnservedMw, 200, 105, 100);
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
            new(
                technology,
                Power.FromMegawatts(capacityMw),
                monthlyCapacityFactors: technology == TechnologyKey.Hydro
                    ? new Dictionary<DateOnly, double>
                    {
                        [new DateOnly(2026, 7, 1)] = 1,
                    }
                    : null);

        private static FlowSeries HourlyFlow(params double[] megawatts) =>
            new(NemStart, TimeSpan.FromHours(1), megawatts);

        private static FlowSeries HourlyFlowAt(
            DateTimeOffset start,
            params double[] megawatts) =>
            new(start, TimeSpan.FromHours(1), megawatts);

        private static RegionalResourceProfile RegionalResources(
            FlowSeries timeline,
            double[]? windMetresPerSecond = null,
            double[]? directNormalRadiation = null)
        {
            var zeros = new double[timeline.Length];
            windMetresPerSecond ??= Enumerable.Repeat(
                WindPowerCurve.RatedWindSpeedMetresPerSecond,
                timeline.Length).ToArray();
            directNormalRadiation ??= Enumerable.Repeat(2_000.0, timeline.Length).ToArray();
            return new RegionalResourceProfile(
                TraceSeries.GlobalHorizontalRadiation(timeline.Start, timeline.Resolution, zeros),
                TraceSeries.DirectNormalRadiation(
                    timeline.Start,
                    timeline.Resolution,
                    directNormalRadiation),
                TraceSeries.DiffuseHorizontalRadiation(timeline.Start, timeline.Resolution, zeros),
                SolarZenithSeries.Calculate(
                    timeline.Start,
                    timeline.Resolution,
                    timeline.Length,
                    latitude: -33.8688,
                    longitude: 151.2093),
                TraceSeries.DryBulbTemperature(timeline.Start, timeline.Resolution, zeros),
                TraceSeries.WindSpeed(
                    timeline.Start,
                    timeline.Resolution,
                    windMetresPerSecond,
                    WindPowerCurve.DefaultHubHeightMetres));
        }

        private static FlowSeries ExpectedAvailableCapacity(
            GeneratingFleet fleet,
            RegionalResourceProfile resources,
            FlowSeries timeline)
        {
            if (fleet.TechnologyKey == TechnologyKey.Solar)
            {
                return DualAxisSolarPowerCurve.Calculate(
                    resources.GlobalHorizontalRadiation,
                    resources.DirectNormalRadiation,
                    resources.DiffuseHorizontalRadiation,
                    resources.DryBulbTemperature,
                    resources.SolarZenith,
                    fleet.NameplateCapacity);
            }

            if (fleet.TechnologyKey == TechnologyKey.Wind)
            {
                return WindPowerCurve.Calculate(resources.WindSpeed, fleet.NameplateCapacity);
            }

            return new FlowSeries(
                timeline.Start,
                timeline.Resolution,
                Enumerable.Repeat(fleet.NameplateCapacity.Megawatts, timeline.Length).ToArray());
        }

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