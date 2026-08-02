using FluentAssertions;
using NEM.Model.Generation.Solar;
using NEM.Model.Generation.Wind;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
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
                Fleet(GenerationTechnology.Gas, 50),
                Fleet(GenerationTechnology.Coal, 40),
                Fleet(GenerationTechnology.Hydro, 30),
                Fleet(GenerationTechnology.Wind, 10),
                Fleet(GenerationTechnology.Solar, 20),
            ];
            FlowSeries demand = HourlyFlowAt(NemStart.AddHours(12), 10, 75, 180);
            var region = new Region(
                "NSW1",
                fleets,
                demand,
                resourceProfile: RegionalResources(demand));

            DispatchOutcome outcome = Dispatch(region);

            outcome.RegionId.Should().Be("NSW1");
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Solar], 20, 20, 20);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Wind], 10, 10, 10);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Hydro], 0, 30, 30);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Coal], 0, 15, 40);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Gas], 0, 0, 50);
            AssertSeries(outcome.PerFleetCurtailment[GenerationTechnology.Solar], 10, 0, 0);
            AssertSeries(outcome.PerFleetCurtailment[GenerationTechnology.Wind], 10, 0, 0);
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
                Fleet(GenerationTechnology.Gas, 1_500),
                Fleet(GenerationTechnology.Coal, 1_250),
                Fleet(GenerationTechnology.Hydro, 1_000),
                Fleet(GenerationTechnology.Wind, 750),
                Fleet(GenerationTechnology.Solar, 500),
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
                fleet => fleet.GenerationTechnology,
                fleet => ExpectedAvailableCapacity(fleet, resources, demandSeries));

            DispatchOutcome outcome = Dispatch(region);

            for (int hour = 0; hour < HoursInJuly; hour++)
            {
                double residual = demand[hour];
                double generation = 0;
                double expectedCurtailment = 0;

                foreach (GeneratingFleet fleet in fleets.OrderBy(fleet => fleet.ShortRunMarginalCost))
                {
                    double fleetOutput = outcome.PerFleetGeneration[fleet.GenerationTechnology][hour].Megawatts;
                    double available = availableByFleet[fleet.GenerationTechnology][hour].Megawatts;
                    double expectedDelivered = Math.Min(residual, available);
                    double expectedOutput = fleet.IsIntermittentRenewable
                        ? available
                        : expectedDelivered;
                    double fleetCurtailment = outcome.PerFleetCurtailment[fleet.GenerationTechnology][hour].Megawatts;
                    double expectedFleetCurtailment = fleet.IsIntermittentRenewable
                        ? available - expectedDelivered
                        : 0;

                    fleetOutput.Should().Be(expectedOutput, $"fleet {fleet.GenerationTechnology} must follow merit order at hour {hour}");
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
                [Fleet(GenerationTechnology.Coal, 40), Fleet(GenerationTechnology.Wind, 10), Fleet(GenerationTechnology.Solar, 20)],
                demand,
                resourceProfile: RegionalResources(demand));

            DispatchOutcome outcome = Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Solar], 20);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Wind], 10);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Coal], 0);
            AssertSeries(outcome.PerFleetCurtailment[GenerationTechnology.Solar], 20);
            AssertSeries(outcome.PerFleetCurtailment[GenerationTechnology.Wind], 10);
            AssertSeries(outcome.PerFleetCurtailment[GenerationTechnology.Coal], 0);
            AssertSeries(outcome.Curtailment, 30);
            AssertSeries(outcome.Unserved, 0);
        }

        [Fact]
        public void Dispatch_DemandEqualToCumulativeCapacity_HasNoCurtailmentOrUnserved()
        {
            FlowSeries demand = HourlyFlowAt(NemStart.AddHours(12), 60);
            var region = new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Hydro, 30), Fleet(GenerationTechnology.Wind, 10), Fleet(GenerationTechnology.Solar, 20)],
                demand,
                resourceProfile: RegionalResources(demand));

            DispatchOutcome outcome = Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Solar], 20);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Wind], 10);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Hydro], 30);
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
                GenerationTechnology.Hydro,
                Power.FromMegawatts(50),
                monthlyCapacityFactors: monthlyCapacityFactors);
            var region = new Region(
                "NSW1",
                [hydro],
                new FlowSeries(start, TimeSpan.FromHours(1), Enumerable.Repeat(50.0, hours).ToArray()));

            DispatchOutcome outcome = Dispatch(region);

            outcome.PerFleetGeneration[GenerationTechnology.Hydro].Integrate()
                .Should().Be(Energy.FromMegawattHours(1_200));
        }

        [Fact]
        public void Dispatch_HydroBudgetMissingForDemandMonth_Throws()
        {
            var hydro = new GeneratingFleet(
                GenerationTechnology.Hydro,
                Power.FromMegawatts(50),
                monthlyCapacityFactors: new Dictionary<DateOnly, double>
                {
                    [new DateOnly(2026, 6, 1)] = 0.5,
                });
            var region = new Region("NSW1", [hydro], HourlyFlow(50));

            var act = () => Dispatch(region);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Hydro has no energy budget for 2026-07*");
        }

        [Fact]
        public void Dispatch_ZeroCapacityFleet_ProducesZeroGeneration()
        {
            FlowSeries demand = HourlyFlow(10);
            var region = new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Solar, 0)],
                demand,
                resourceProfile: RegionalResources(demand));

            DispatchOutcome outcome = Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Solar], 0);
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
                [Fleet(GenerationTechnology.Wind, 10)],
                demand,
                resourceProfile: resources);

            DispatchOutcome outcome = Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Wind], 0, 10, 0);
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
                [Fleet(GenerationTechnology.Wind, 10)],
                subHourlyDemand,
                resourceProfile: RegionalResources(dispatchTimeline));

            DispatchOutcome outcome = Dispatch(region);

            region.Demand.BaseDemand.Resolution.Should().Be(DemandProfile.Resolution);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Wind], 10, 10);
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
                [Fleet(GenerationTechnology.Solar, 100)],
                demand,
                resourceProfile: resources);

            DispatchOutcome outcome = Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Solar], 0, 95, 100);
            AssertSeries(outcome.Unserved, 200, 105, 100);
        }

        [Fact]
        public void Dispatch_GreedyPolicy_ChargesFromSurplusThenDischargesIntoDeficit()
        {
            FlowSeries demand = HourlyFlowAt(NemStart.AddHours(12), 0, 30);
            var region = new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Solar, 20)],
                demand,
                resourceProfile: RegionalResources(demand),
                storageFleets: [Battery(storageCapacityMwh: 20, powerCapacityMw: 20)]);

            DispatchOutcome outcome = Dispatch(region);

            AssertSeries(outcome.SurplusCharge, 20, 0);
            AssertSeries(outcome.IncrementalGenerationCharge, 0, 0);
            AssertSeries(outcome.Charge, 20, 0);
            AssertSeries(outcome.Discharge, 0, 10);
            AssertSeries(outcome.Curtailment, 0, 0);
            AssertSeries(outcome.Unserved, 0, 0);
        }

        [Fact]
        public void Dispatch_GreedyPolicy_ClampsAtEnergyLimitAndBooksRemainingDemandShortfall()
        {
            FlowSeries demand = HourlyFlowAt(NemStart.AddHours(12), 0, 40);
            var region = new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Solar, 20)],
                demand,
                resourceProfile: RegionalResources(demand),
                storageFleets: [Battery(storageCapacityMwh: 8.7, powerCapacityMw: 20)]);

            DispatchOutcome outcome = Dispatch(region);

            outcome.Charge[0].Megawatts.Should().BeApproximately(10, 1e-10);
            outcome.Discharge[1].Megawatts.Should().BeApproximately(8.7, 1e-10);
            outcome.Unserved[1].Megawatts.Should().BeApproximately(11.3, 1e-10);
            outcome.Unserved[0].Should().Be(Power.Zero);
        }

        [Fact]
        public void Dispatch_TotalUnservedEnergy_IsNonIncreasingAsStorageEnergyCapacityIncreases()
        {
            var random = new Random(34025);
            double[] storageCapacitiesMwh = Enumerable.Range(0, 100)
                .Select(_ => 0.1 + (random.NextDouble() * 79.9))
                .Append(0.1)
                .Append(80)
                .Order()
                .ToArray();
            FlowSeries demand = HourlyFlow(0, 40, 0, 40, 40);
            GeneratingFleet wind = Fleet(GenerationTechnology.Wind, 20);
            RegionalResourceProfile resources = RegionalResources(demand);

            (double CapacityMwh, Energy TotalUse)[] results = storageCapacitiesMwh
                .Select(storageCapacityMwh =>
                {
                    var region = new Region(
                        "NSW1",
                        [wind],
                        demand,
                        resourceProfile: resources,
                        storageFleets: [Battery(storageCapacityMwh, powerCapacityMw: 20)]);
                    Energy totalUse = Dispatch(region).Reliability.UnservedEnergy;

                    return (storageCapacityMwh, totalUse);
                })
                .ToArray();

            for (int index = 1; index < results.Length; index++)
            {
                results[index].TotalUse.Should().BeLessThanOrEqualTo(
                    results[index - 1].TotalUse,
                    "increasing storage energy capacity from {0} MWh to {1} MWh "
                    + "must not increase total USE",
                    results[index - 1].CapacityMwh,
                    results[index].CapacityMwh);
            }
        }

        [Fact]
        public void Dispatch_FailedIncrementalGenerationChargingIsSilentWhileDemandShortfallIsUnserved()
        {
            FlowSeries demand = HourlyFlow(0, 10);
            var region = new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Gas, 0)],
                demand,
                storageFleets: [Battery(storageCapacityMwh: 20, powerCapacityMw: 10)]);
            var policy = new IncrementalGenerationChargingPolicy(
                GenerationTechnology.Gas,
                chargeMw: 10);

            DispatchOutcome outcome = Dispatch(region, policy);

            AssertSeries(outcome.IncrementalGenerationCharge, 0, 0);
            AssertSeries(outcome.Charge, 0, 0);
            AssertSeries(outcome.Unserved, 0, 10);
        }

        [Fact]
        public void Dispatch_IncrementalGenerationChargingUsesNamedFleetAndAppliesRoundTripLoss()
        {
            FlowSeries demand = HourlyFlow(0, 50);
            var region = new Region(
                "NSW1",
                [
                    Fleet(GenerationTechnology.Coal, 20),
                    Fleet(GenerationTechnology.Gas, 20),
                ],
                demand,
                storageFleets: [Battery(storageCapacityMwh: 20, powerCapacityMw: 10)]);
            var policy = new IncrementalGenerationChargingPolicy(
                GenerationTechnology.Gas,
                chargeMw: 10);

            DispatchOutcome outcome = Dispatch(region, policy);

            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Coal], 0, 20);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Gas], 10, 20);
            AssertSeries(outcome.SurplusCharge, 0, 0);
            AssertSeries(outcome.IncrementalGenerationCharge, 10, 0);
            outcome.Discharge[1].Megawatts.Should().BeApproximately(8.7, 1e-10);
            outcome.Unserved[1].Megawatts.Should().BeApproximately(1.3, 1e-10);
        }

        [Fact]
        public void Dispatch_IncrementalHydroChargingConsumesMonthlyEnergyBudget()
        {
            FlowSeries demand = HourlyFlow(0, 10);
            const double hydroCapacityMw = 10;
            var hydro = new GeneratingFleet(
                GenerationTechnology.Hydro,
                Power.FromMegawatts(hydroCapacityMw),
                new Dictionary<DateOnly, double>
                {
                    [new DateOnly(2026, 7, 1)] = 10.0 / (hydroCapacityMw * 31 * 24),
                });
            var region = new Region(
                "NSW1",
                [hydro],
                demand,
                storageFleets: [Battery(storageCapacityMwh: 20, powerCapacityMw: 10)]);
            var policy = new IncrementalGenerationChargingPolicy(
                GenerationTechnology.Hydro,
                chargeMw: 10);

            DispatchOutcome outcome = Dispatch(region, policy);

            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Hydro], 10, 0);
            AssertSeries(outcome.IncrementalGenerationCharge, 10, 0);
            outcome.Discharge[1].Megawatts.Should().BeApproximately(8.7, 1e-10);
            outcome.Unserved[1].Megawatts.Should().BeApproximately(1.3, 1e-10);
        }

        [Fact]
        public void Dispatch_RejectsNullPowerSystem()
        {
            var act = () => Dispatcher.Dispatch(null!);

            act.Should().Throw<ArgumentNullException>().WithParameterName("powerSystem");
        }

        [Fact]
        public void Dispatch_PowerSystem_ProducesOutcomeAndReliabilityForEachRegion()
        {
            var nsw = new Region("NSW1", [Fleet(GenerationTechnology.Coal, 80)], HourlyFlow(100));
            var qld = new Region("QLD1", [Fleet(GenerationTechnology.Gas, 100)], HourlyFlow(100));

            IReadOnlyList<DispatchOutcome> outcomes = Dispatcher.Dispatch(PowerSystem(nsw, qld));

            outcomes.Select(outcome => outcome.RegionId).Should().Equal("NSW1", "QLD1");
            outcomes[0].Reliability.UnservedEnergy.Should().Be(Energy.FromMegawattHours(20));
            outcomes[1].Reliability.UnservedEnergy.Should().Be(Energy.Zero);
            var mutableOutcomes = (IList<DispatchOutcome>)outcomes;
            var act = () => mutableOutcomes.Clear();
            act.Should().Throw<NotSupportedException>();
        }

        [Fact]
        public void DispatchOutcome_CopiesAndExposesReadOnlyFleetGeneration()
        {
            var generation = new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = HourlyFlow(10),
            };
            var curtailment = new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = HourlyFlow(0),
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
            var mutableView = (IDictionary<GenerationTechnology, FlowSeries>)outcome.PerFleetGeneration;
            var act = () => mutableView.Add(GenerationTechnology.Gas, HourlyFlow(0));

            outcome.PerFleetGeneration.Should().ContainKey(GenerationTechnology.Coal);
            outcome.PerFleetCurtailment.Should().ContainKey(GenerationTechnology.Coal);
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
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = halfHourlyDemand,
                },
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = halfHourlyZero,
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
                new Dictionary<GenerationTechnology, FlowSeries>(),
                new Dictionary<GenerationTechnology, FlowSeries>(),
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

        [Fact]
        public void DispatchOutcome_ChargeIsSumOfSurplusAndIncrementalGenerationSources()
        {
            FlowSeries zero = HourlyFlow(0);
            var outcome = new DispatchOutcome(
                "NSW1",
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = HourlyFlow(10),
                },
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = zero,
                },
                HourlyFlow(7),
                zero,
                HourlyFlow(2),
                zero,
                zero,
                zero,
                HourlyFlow(1));

            AssertSeries(outcome.SurplusCharge, 2);
            AssertSeries(outcome.IncrementalGenerationCharge, 1);
            AssertSeries(outcome.Charge, 3);
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
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = HourlyFlow(generation),
                },
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = HourlyFlow(curtailment),
                },
                HourlyFlow(demand),
                HourlyFlow(unserved),
                zero,
                zero,
                zero,
                zero);
        }

        private static DispatchOutcome Dispatch(
            Region region,
            IStoragePolicy? storagePolicy = null) =>
            (storagePolicy is null
                ? Dispatcher.Dispatch(PowerSystem(region))
                : Dispatcher.Dispatch(PowerSystem(region), storagePolicy)).Single();

        private static PowerSystem PowerSystem(params Region[] regions) =>
            new(
                new PowerSystemId("test-power-system"),
                new ScenarioId("test-scenario"),
                regions);

        private static GeneratingFleet Fleet(GenerationTechnology technology, double capacityMw) =>
            new(
                technology,
                Power.FromMegawatts(capacityMw),
                monthlyCapacityFactors: technology == GenerationTechnology.Hydro
                    ? new Dictionary<DateOnly, double>
                    {
                        [new DateOnly(2026, 7, 1)] = 1,
                    }
                    : null);

        private static StorageFleet Battery(double storageCapacityMwh, double powerCapacityMw) =>
            new(
                StorageTechnology.Battery,
                Energy.FromMegawattHours(storageCapacityMwh),
                Power.FromMegawatts(powerCapacityMw));

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
            if (fleet.GenerationTechnology == GenerationTechnology.Solar)
            {
                return DualAxisSolarPowerCurve.Calculate(
                    resources.GlobalHorizontalRadiation,
                    resources.DirectNormalRadiation,
                    resources.DiffuseHorizontalRadiation,
                    resources.DryBulbTemperature,
                    resources.SolarZenith,
                    fleet.NameplateCapacity);
            }

            if (fleet.GenerationTechnology == GenerationTechnology.Wind)
            {
                return WindPowerCurve.Calculate(resources.WindSpeed, fleet.NameplateCapacity);
            }

            return new FlowSeries(
                timeline.Start,
                timeline.Resolution,
                Enumerable.Repeat(fleet.NameplateCapacity.Megawatts, timeline.Length).ToArray());
        }

        private sealed class IncrementalGenerationChargingPolicy(
            GenerationTechnology sourceTechnology,
            double chargeMw) : IStoragePolicy
        {
            public StorageDecision Decide(DispatchContext context)
            {
                StorageFleetSnapshot? fleet = context.StorageFleets
                    .OrderBy(candidate => candidate.StorageTechnology)
                    .Cast<StorageFleetSnapshot?>()
                    .FirstOrDefault();
                if (fleet is null)
                {
                    return StorageDecision.None;
                }

                if (context.Residual > Power.Zero)
                {
                    return fleet.Value.DischargeHeadroom == Power.Zero
                        ? StorageDecision.None
                        : new StorageDecision([
                            new StorageIntent(
                                fleet.Value.StorageTechnology,
                                context.Residual),
                        ]);
                }

                return new StorageDecision([
                    new StorageIntent(
                        fleet.Value.StorageTechnology,
                        Power.FromMegawatts(-chargeMw),
                        ChargeSource.IncrementalGeneration(sourceTechnology)),
                ]);
            }
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