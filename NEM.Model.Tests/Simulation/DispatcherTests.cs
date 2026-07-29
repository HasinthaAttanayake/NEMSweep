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
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Solar], 20, 20, 20);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Wind], 10, 10, 10);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Hydro], 0, 30, 30);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Coal], 0, 15, 40);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Gas], 0, 0, 50);
            AssertSeries(outcome.PerFleetCurtailment[TechnologyKey.Solar], 10, 0, 0);
            AssertSeries(outcome.PerFleetCurtailment[TechnologyKey.Wind], 10, 0, 0);
            AssertSeries(outcome.Curtailment, 20, 0, 0);
            AssertSeries(outcome.Unserved, 0, 0, 30);
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
                double generation = 0;
                double expectedCurtailment = 0;

                foreach (GeneratingFleet fleet in fleets.OrderBy(fleet => fleet.ShortRunMarginalCost))
                {
                    double fleetOutput = outcome.PerFleetGeneration[fleet.TechnologyKey][hour].Megawatts;
                    double available = availableByFleet[fleet.TechnologyKey][hour].Megawatts;
                    double expectedDelivered = Math.Min(residual, available);
                    double expectedOutput = fleet.IsIntermittentRenewable
                        ? available
                        : expectedDelivered;
                    double fleetCurtailment = outcome.PerFleetCurtailment[fleet.TechnologyKey][hour].Megawatts;
                    double expectedFleetCurtailment = fleet.IsIntermittentRenewable
                        ? available - expectedDelivered
                        : 0;

                    fleetOutput.Should().Be(expectedOutput, $"fleet {fleet.TechnologyKey} must follow merit order at hour {hour}");
                    fleetOutput.Should().BeInRange(0, fleet.NameplateCapacity.Megawatts);
                    fleetCurtailment.Should().Be(expectedFleetCurtailment);
                    generation += fleetOutput;
                    expectedCurtailment += expectedFleetCurtailment;
                    residual -= expectedDelivered;
                }

                double unserved = outcome.Unserved[hour].Megawatts;
                double curtailment = outcome.Curtailment[hour].Megawatts;

                (generation + unserved).Should().Be(
                    demand[hour] + curtailment,
                    $"energy must balance at hour {hour}");
                unserved.Should().Be(Math.Max(residual, 0));
                curtailment.Should().Be(expectedCurtailment);
                curtailment.Should().BeGreaterThanOrEqualTo(0);
                (curtailment > 0 && unserved > 0).Should().BeFalse(
                    $"curtailment and unserved energy cannot co-occur at hour {hour}");
            }
        }

        [Fact]
        public void Dispatch_ZeroDemand_ReportsAvailableRenewablesAsPositiveCurtailment()
        {
            FlowSeries demand = HourlyFlowAt(NemStart.AddHours(12), 0);
            var region = new Region(
                "NSW1",
                [Fleet(TechnologyKey.Coal, 40), Fleet(TechnologyKey.Wind, 10), Fleet(TechnologyKey.Solar, 20)],
                demand,
                resourceProfile: RegionalResources(demand));

            DispatchOutcome outcome = Dispatcher.Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Solar], 20);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Wind], 10);
            AssertSeries(outcome.PerFleetGeneration[TechnologyKey.Coal], 0);
            AssertSeries(outcome.PerFleetCurtailment[TechnologyKey.Solar], 20);
            AssertSeries(outcome.PerFleetCurtailment[TechnologyKey.Wind], 10);
            AssertSeries(outcome.PerFleetCurtailment[TechnologyKey.Coal], 0);
            AssertSeries(outcome.Curtailment, 30);
            AssertSeries(outcome.Unserved, 0);
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
            AssertSeries(outcome.Curtailment, 0);
            AssertSeries(outcome.Unserved, 0);
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
            AssertSeries(outcome.Curtailment, 0);
            AssertSeries(outcome.Unserved, 10);
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
            AssertSeries(outcome.Unserved, 100, 90, 100);
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
            AssertSeries(outcome.Unserved, 90, 90);
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
            AssertSeries(outcome.Unserved, 200, 105, 100);
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
            var curtailment = new Dictionary<TechnologyKey, FlowSeries>
            {
                [TechnologyKey.Coal] = HourlyFlow(0),
            };
            var zero = HourlyFlow(0);
            var outcome = new DispatchOutcome(
                "NSW1",
                generation,
                curtailment,
                HourlyFlow(10),
                zero,
                zero,
                zero,
                zero,
                zero);

            generation.Clear();
            curtailment.Clear();
            var mutableView = (IDictionary<TechnologyKey, FlowSeries>)outcome.PerFleetGeneration;
            var act = () => mutableView.Add(TechnologyKey.Gas, HourlyFlow(0));

            outcome.PerFleetGeneration.Should().ContainKey(TechnologyKey.Coal);
            outcome.PerFleetCurtailment.Should().ContainKey(TechnologyKey.Coal);
            act.Should().Throw<NotSupportedException>();
        }

        [Fact]
        public void DispatchOutcome_RejectsIntervalImbalanceEvenWhenIntegratedTotalsBalance()
        {
            var act = () => Outcome(
                generation: [90, 110],
                demand: [100, 100],
                curtailment: [0, 0],
                unserved: [0, 0]);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("Energy balance failed at index 0*");
        }

        [Fact]
        public void DispatchOutcome_RejectsNegativeCurtailment()
        {
            var act = () => Outcome(
                generation: [90],
                demand: [100],
                curtailment: [-10],
                unserved: [0]);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("Curtailment cannot be negative at index 0*");
        }

        [Fact]
        public void DispatchOutcome_RejectsCurtailmentAndUnservedInSameInterval()
        {
            var act = () => Outcome(
                generation: [100],
                demand: [100],
                curtailment: [10],
                unserved: [10]);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("Curtailment and unserved demand cannot coexist at index 0*");
        }

        [Fact]
        public void DispatchOutcome_RejectsNonHourlyResolution()
        {
            var halfHourlyDemand = new FlowSeries(
                NemStart,
                TimeSpan.FromMinutes(30),
                [100, 100]);
            var halfHourlyZero = new FlowSeries(
                NemStart,
                TimeSpan.FromMinutes(30),
                [0, 0]);
            var act = () => new DispatchOutcome(
                "NSW1",
                new Dictionary<TechnologyKey, FlowSeries>
                {
                    [TechnologyKey.Coal] = halfHourlyDemand,
                },
                new Dictionary<TechnologyKey, FlowSeries>
                {
                    [TechnologyKey.Coal] = halfHourlyZero,
                },
                halfHourlyDemand,
                halfHourlyZero,
                halfHourlyZero,
                halfHourlyZero,
                halfHourlyZero,
                halfHourlyZero);

            act.Should().Throw<ArgumentException>()
                .WithParameterName("Demand")
                .WithMessage("Dispatch outcomes must use hourly resolution.*");
        }

        [Fact]
        public void DispatchOutcome_RejectsBlankRegionId()
        {
            var act = () => Outcome(
                generation: [100],
                demand: [100],
                curtailment: [0],
                unserved: [0],
                regionId: " ");

            act.Should().Throw<ArgumentException>()
                .WithParameterName("regionId");
        }

        [Fact]
        public void DispatchOutcome_RejectsNullDemandWithClearParameterName()
        {
            FlowSeries zero = HourlyFlow(0);
            var act = () => new DispatchOutcome(
                "NSW1",
                new Dictionary<TechnologyKey, FlowSeries>(),
                new Dictionary<TechnologyKey, FlowSeries>(),
                null!,
                zero,
                zero,
                zero,
                zero,
                zero);

            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("demand");
        }

        [Fact]
        public void DispatchOutcome_AcceptsSubToleranceFloatingPointResidue()
        {
            var act = () => Outcome(
                generation: [100 - 1e-10],
                demand: [100],
                curtailment: [-1e-10],
                unserved: [1e-10]);

            act.Should().NotThrow();
        }

        private static DispatchOutcome Outcome(
            double[] generation,
            double[] demand,
            double[] curtailment,
            double[] unserved,
            string regionId = "NSW1")
        {
            FlowSeries zero = HourlyFlow(new double[demand.Length]);
            return new DispatchOutcome(
                regionId,
                new Dictionary<TechnologyKey, FlowSeries>
                {
                    [TechnologyKey.Coal] = HourlyFlow(generation),
                },
                new Dictionary<TechnologyKey, FlowSeries>
                {
                    [TechnologyKey.Coal] = HourlyFlow(curtailment),
                },
                HourlyFlow(demand),
                HourlyFlow(unserved),
                zero,
                zero,
                zero,
                zero);
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