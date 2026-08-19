using AwesomeAssertions;
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
            // Hydro is deliberately excluded from this fleet set: its request is paced against
            // a calendar-month budget by HydroReservationState (see that type and
            // GenerationMeritOrderTests), so its output over just 3 hours depends on the
            // pacer's bisection/warm-up arithmetic, not simple by-hand merit-order division -
            // see HydroReservationStateTests and Dispatch_PacedHydro_* below for that. This
            // test stays focused on straightforward merit-order arithmetic for the rest of the
            // fleet.
            GeneratingFleet[] fleets =
            [
                Fleet(GenerationTechnology.Gas, 50),
                Fleet(GenerationTechnology.Coal, 40),
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
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Coal], 0, 40, 40);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Gas], 0, 5, 50);
            AssertSeries(outcome.PerFleetCurtailment[GenerationTechnology.Solar], 10, 0, 0);
            AssertSeries(outcome.PerFleetCurtailment[GenerationTechnology.Wind], 10, 0, 0);
            AssertSeries(outcome.Curtailment, 20, 0, 0);
            AssertSeries(outcome.Unserved, 0, 0, 60);
        }

        [Fact]
        public void Dispatch_UsesShortRunMarginalCostBeforeTechnologyOrder()
        {
            FlowSeries demand = HourlyFlow(40);
            var region = new Region(
                "NSW1",
                [
                    Fleet(GenerationTechnology.Coal, 30, shortRunMarginalCostAudPerMwh: 10),
                    Fleet(GenerationTechnology.Gas, 30, shortRunMarginalCostAudPerMwh: 1),
                ],
                demand);

            DispatchOutcome outcome = Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Gas], 30);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Coal], 10);
        }

        [Fact]
        public void Dispatch_ContextIncludesFleetShortRunMarginalCosts()
        {
            var policy = new ContextRecordingPolicy();
            var region = new Region(
                "NSW1",
                [
                    Fleet(GenerationTechnology.Coal, 30, shortRunMarginalCostAudPerMwh: 10),
                    Fleet(GenerationTechnology.Gas, 30, shortRunMarginalCostAudPerMwh: 1),
                ],
                HourlyFlow(20));

            Dispatch(region, policy);

            policy.Contexts.Should().ContainSingle();
            policy.Contexts[0].GenerationFleets.Should().BeEquivalentTo(
                [
                    new GenerationFleetSnapshot(
                        GenerationTechnology.Gas,
                        Power.FromMegawatts(10),
                        GenerationEnergyCost.FromAudPerMwhGenerated(1)),
                    new GenerationFleetSnapshot(
                        GenerationTechnology.Coal,
                        Power.FromMegawatts(30),
                        GenerationEnergyCost.FromAudPerMwhGenerated(10)),
                ],
                options => options.WithStrictOrdering());
        }

        [Theory]
        [InlineData(7)]
        [InlineData(41)]
        [InlineData(2026)]
        public void Dispatch_FullMonth_PreservesIntervalEnergyBalance(int seed)
        {
            // Hydro is deliberately excluded: HydroReservationState paces its request against
            // a calendar-month budget (see that type), so recomputing its expected output here
            // would mean re-implementing the pacer's bisection/warm-up logic rather than
            // testing merit-order balance - covered separately by HydroReservationStateTests
            // and the full-year utilisation test below.
            GeneratingFleet[] fleets =
            [
                Fleet(GenerationTechnology.Gas, 1_500),
                Fleet(GenerationTechnology.Coal, 1_250),
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

                foreach (GeneratingFleet fleet in GenerationMeritOrder.Sort(fleets))
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

        [Theory]
        [InlineData(7)]
        [InlineData(41)]
        [InlineData(2026)]
        public void Dispatch_RandomizedStorageRun_PreservesPerFleetEnergyBalance(int seed)
        {
            DispatchOutcome outcome = RandomizedStorageOutcome(seed);

            for (int hour = 0; hour < outcome.Demand.Length; hour++)
            {
                foreach (GenerationTechnology technology in outcome.PerFleetGeneration.Keys)
                {
                    double generation = outcome.PerFleetGeneration[technology][hour].Megawatts;
                    double outputs = outcome.PerFleetCurtailment[technology][hour].Megawatts
                        + outcome.PerFleetCharge[technology][hour].Megawatts
                        + outcome.PerFleetDelivered[technology][hour].Megawatts;

                    outputs.Should().BeApproximately(generation, 1e-9);
                }
            }
        }

        [Theory]
        [InlineData(7)]
        [InlineData(41)]
        [InlineData(2026)]
        public void Dispatch_RandomizedStorageRun_PerFleetAllocationsClose(int seed)
        {
            DispatchOutcome outcome = RandomizedStorageOutcome(seed);

            for (int hour = 0; hour < outcome.Demand.Length; hour++)
            {
                double allocatedDelivered = outcome.PerFleetDelivered.Values
                    .Sum(flow => flow[hour].Megawatts);
                double allocatedCharge = outcome.PerFleetCharge.Values
                    .Sum(flow => flow[hour].Megawatts);
                double generatorDelivered = outcome.DeliveredToLoad[hour].Megawatts
                    - outcome.Discharge[hour].Megawatts
                    - outcome.Imports[hour].Megawatts
                    + outcome.Exports[hour].Megawatts;

                allocatedDelivered.Should().BeApproximately(generatorDelivered, 1e-9);
                allocatedCharge.Should().BeApproximately(outcome.Charge[hour].Megawatts, 1e-9);
                outcome.DeliveredToLoad[hour].Megawatts.Should().BeApproximately(
                    outcome.Demand[hour].Megawatts - outcome.Unserved[hour].Megawatts,
                    1e-9);
            }
        }

        [Theory]
        [InlineData(7)]
        [InlineData(41)]
        [InlineData(2026)]
        public void Dispatch_RandomizedStorageRun_PreservesSystemEnergyBalance(int seed)
        {
            DispatchOutcome outcome = RandomizedStorageOutcome(seed);

            for (int hour = 0; hour < outcome.Demand.Length; hour++)
            {
                double generation = outcome.PerFleetGeneration.Values
                    .Sum(flow => flow[hour].Megawatts);
                double inputs = generation
                    + outcome.Discharge[hour].Megawatts
                    + outcome.Imports[hour].Megawatts
                    + outcome.Unserved[hour].Megawatts;
                double outputs = outcome.Demand[hour].Megawatts
                    + outcome.Charge[hour].Megawatts
                    + outcome.Exports[hour].Megawatts
                    + outcome.Curtailment[hour].Megawatts;

                inputs.Should().BeApproximately(outputs, 1e-9);
            }
        }

        [Theory]
        [InlineData(7)]
        [InlineData(41)]
        [InlineData(2026)]
        public void Dispatch_RandomizedStorageRun_NeverCoexistsCurtailmentAndUnserved(int seed)
        {
            DispatchOutcome outcome = RandomizedStorageOutcome(seed);

            for (int hour = 0; hour < outcome.Demand.Length; hour++)
            {
                (outcome.Curtailment[hour].Megawatts > 1e-9
                    && outcome.Unserved[hour].Megawatts > 1e-9).Should().BeFalse();
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

            outcome.PerFleetGeneration[GenerationTechnology.Hydro].Integrate().MegawattHours
                .Should().BeApproximately(1_200, 1e-6);
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

            AssertSeries(outcome.Charge, 20, 0);
            AssertSeries(outcome.PerFleetCharge[GenerationTechnology.Solar], 20, 0);
            AssertSeries(outcome.PerFleetDelivered[GenerationTechnology.Solar], 0, 20);
            AssertSeries(outcome.DeliveredToLoad, 0, 30);
            AssertSeries(outcome.Discharge, 0, 10);
            AssertSeries(outcome.Curtailment, 0, 0);
            AssertSeries(outcome.Unserved, 0, 0);
            outcome.StateOfChargeByTechnology[StorageTechnology.Battery][0]
                .Should().Be(Energy.Zero);
            outcome.StateOfChargeByTechnology[StorageTechnology.Battery][1]
                .Should().Be(Energy.FromMegawattHours(17.4));
        }

        [Fact]
        public void Dispatch_SeededStorage_OpensIntervalZeroAtSeedLevelNotZero()
        {
            FlowSeries demand = HourlyFlowAt(NemStart.AddHours(12), 0);
            var seededBattery = new StorageFleet(
                StorageTechnology.Battery,
                Energy.FromMegawattHours(100),
                Power.FromMegawatts(20),
                new StorageTechnologyProfile(15u, 0.87),
                Energy.FromMegawattHours(40));
            var region = new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Coal, 100)],
                demand,
                storageFleets: [seededBattery]);

            DispatchOutcome outcome = Dispatch(region);

            outcome.StateOfChargeByTechnology[StorageTechnology.Battery][0]
                .Should().Be(Energy.FromMegawattHours(40));
        }

        [Fact]
        public void Dispatch_GreedyPolicy_TracksMultiFleetChargeAsCurtailmentIsReduced()
        {
            FlowSeries demand = HourlyFlowAt(NemStart.AddHours(12), 10);
            var region = new Region(
                "NSW1",
                [
                    Fleet(GenerationTechnology.Solar, 20),
                    Fleet(GenerationTechnology.Wind, 10),
                ],
                demand,
                resourceProfile: RegionalResources(demand),
                storageFleets: [Battery(storageCapacityMwh: 20, powerCapacityMw: 15)]);

            DispatchOutcome outcome = Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Solar], 20);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Wind], 10);
            AssertSeries(outcome.PerFleetCurtailment[GenerationTechnology.Solar], 0);
            AssertSeries(outcome.PerFleetCurtailment[GenerationTechnology.Wind], 5);
            AssertSeries(outcome.PerFleetCharge[GenerationTechnology.Solar], 10);
            AssertSeries(outcome.PerFleetCharge[GenerationTechnology.Wind], 5);
            AssertSeries(outcome.PerFleetDelivered[GenerationTechnology.Solar], 10);
            AssertSeries(outcome.PerFleetDelivered[GenerationTechnology.Wind], 0);
            AssertSeries(outcome.Charge, 15);
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
            AssertSeries(outcome.PerFleetCharge[GenerationTechnology.Coal], 0, 0);
            AssertSeries(outcome.PerFleetCharge[GenerationTechnology.Gas], 10, 0);
            AssertSeries(outcome.PerFleetDelivered[GenerationTechnology.Coal], 0, 20);
            AssertSeries(outcome.PerFleetDelivered[GenerationTechnology.Gas], 0, 20);
            AssertSeries(outcome.Charge, 10, 0);
            outcome.Discharge[1].Megawatts.Should().BeApproximately(8.7, 1e-10);
            outcome.Unserved[1].Megawatts.Should().BeApproximately(1.3, 1e-10);
        }

        [Fact]
        public void Dispatch_DefaultPolicy_ClosesEnergyBalanceWithIncrementalGenerationCharging()
        {
            FlowSeries demand = HourlyFlowAt(NemStart.AddHours(12), 0, 27.4);
            var region = new Region(
                "NSW1",
                [
                    Fleet(GenerationTechnology.Solar, 10),
                    Fleet(GenerationTechnology.Coal, 10, shortRunMarginalCostAudPerMwh: 20),
                    Fleet(GenerationTechnology.Gas, 0, shortRunMarginalCostAudPerMwh: 80),
                ],
                demand,
                resourceProfile: RegionalResources(demand, directNormalRadiation: [2_000, 0]),
                storageFleets: [Battery(storageCapacityMwh: 20, powerCapacityMw: 20)]);

            DispatchOutcome outcome = Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Solar], 10, 0);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Coal], 10, 10);
            AssertSeries(outcome.PerFleetCharge[GenerationTechnology.Solar], 10, 0);
            AssertSeries(outcome.PerFleetCharge[GenerationTechnology.Coal], 10, 0);
            AssertSeries(outcome.Charge, 20, 0);
            AssertSeries(outcome.Discharge, 0, 17.4);
            AssertSeries(outcome.Unserved, 0, 0);
            AssertSeries(outcome.Curtailment, 0, 0);

            for (int hour = 0; hour < demand.Length; hour++)
            {
                double inputs = outcome.PerFleetGeneration.Values.Sum(
                        flow => flow[hour].Megawatts)
                    + outcome.Discharge[hour].Megawatts
                    + outcome.Imports[hour].Megawatts
                    + outcome.Unserved[hour].Megawatts;
                double outputs = outcome.Demand[hour].Megawatts
                    + outcome.Charge[hour].Megawatts
                    + outcome.Exports[hour].Megawatts
                    + outcome.Curtailment[hour].Megawatts;

                inputs.Should().BeApproximately(outputs, 1e-9);
                (outcome.Curtailment[hour] > Power.Zero
                    && outcome.Unserved[hour] > Power.Zero).Should().BeFalse();
            }
        }

        [Fact]
        public void Dispatch_PolicyNamesHydroAsIncrementalChargeSource_ChargesUpToPacedAllowance()
        {
            // Hydro's incremental headroom is capped to the SAME per-interval pace as local
            // dispatch (see RegionalDispatchRun.IncrementalHeadroom), not excluded outright.
            // Giving Hydro a higher SRMC than Coal means Coal covers all of this interval's
            // local demand before Hydro's turn, so Hydro's local dispatch is 0 even though its
            // paced allowance for the interval (computed from residual demand alone - Coal's
            // contribution is invisible to it) is comfortably above the requested charge.
            FlowSeries demand = HourlyFlowAt(NemStart.AddHours(12), 20);
            var hydro = new GeneratingFleet(
                GenerationTechnology.Hydro,
                Power.FromMegawatts(100),
                new Dictionary<DateOnly, double> { [new DateOnly(2026, 7, 1)] = 1 },
                shortRunMarginalCost: GenerationEnergyCost.FromAudPerMwhGenerated(50));
            var region = new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Coal, 100, shortRunMarginalCostAudPerMwh: 1), hydro],
                demand,
                storageFleets: [Battery(storageCapacityMwh: 20, powerCapacityMw: 10)]);
            var policy = new IncrementalGenerationChargingPolicy(
                GenerationTechnology.Hydro,
                chargeMw: 10);

            DispatchOutcome outcome = Dispatch(region, policy);

            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Coal], 20);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Hydro], 10);
            AssertSeries(outcome.Charge, 10);
            AssertSeries(outcome.Unserved, 0);
        }

        [Fact]
        public void Dispatch_HydroFallback_ReserveCoversWhateverPacedDispatchAndStorageCouldNot()
        {
            // A 744 MWh July budget makes the paced share's warm-up pace a clean 0.9 MW
            // (90% * 744 MWh / 744 intervals left in the month at hour 0 - see
            // HydroReservationState). Demand (20 MW) exceeds paced dispatch (0.9 MW) plus the
            // fully-charged battery's discharge headroom (5 MW), so the 4.4% reserve share
            // (74.4 MWh, effectively unconstrained for one hour) closes the rest via
            // RegionalDispatchRun.DispatchHydroFallback - the true last-resort backstop this
            // change preserves from the sort-based version.
            FlowSeries demand = HourlyFlow(20);
            var hydro = new GeneratingFleet(
                GenerationTechnology.Hydro,
                Power.FromMegawatts(100),
                new Dictionary<DateOnly, double> { [new DateOnly(2026, 7, 1)] = 744.0 / (100 * 31 * 24) });
            var region = new Region(
                "NSW1",
                [hydro],
                demand,
                storageFleets: [SeededBattery(storageCapacityMwh: 20, powerCapacityMw: 5)]);

            DispatchOutcome outcome = Dispatch(region);

            outcome.PerFleetGeneration[GenerationTechnology.Hydro][0].Megawatts
                .Should().BeApproximately(15, 1e-9);
            AssertSeries(outcome.Discharge, 5);
            AssertSeries(outcome.Unserved, 0);
        }

        [Fact]
        public void Dispatch_HydroFallback_ReserveNotTouchedWhenPacedDispatchAndStorageAreEnough()
        {
            FlowSeries demand = HourlyFlow(5);
            var hydro = new GeneratingFleet(
                GenerationTechnology.Hydro,
                Power.FromMegawatts(100),
                new Dictionary<DateOnly, double> { [new DateOnly(2026, 7, 1)] = 744.0 / (100 * 31 * 24) });
            var region = new Region(
                "NSW1",
                [hydro],
                demand,
                storageFleets: [SeededBattery(storageCapacityMwh: 20, powerCapacityMw: 5)]);

            DispatchOutcome outcome = Dispatch(region);

            // If the reserve had contributed anything, Hydro's total would exceed the 0.9 MW
            // paced (warm-up-pace-bound) share alone.
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Hydro], 0.9);
            AssertSeries(outcome.Discharge, 4.1);
            AssertSeries(outcome.Unserved, 0);
        }

        [Fact]
        public void Dispatch_FullMonth_HydroDeliversMostOfItsBudgetWhenDemandIsPersistent()
        {
            // The regression test for the finding that made this change necessary: nothing
            // previously asserted that a budgeted fleet actually USES a reasonable share of
            // its monthly budget, only that it never exceeds it - so dispatching Hydro
            // strictly after storage (an earlier version of this change) silently stranded
            // ~93% of it and nothing caught that. A modest budget against persistently-high
            // demand (NSW1/QLD1/VIC1's hydro is a peaking reserve at well under 5% of demand -
            // see docs/domain-model.md) means the pacer always has somewhere to spend it, so
            // utilisation should end up close to 100%, not near zero. NEM-076.
            var random = new Random(4);
            double[] demand = Enumerable.Range(0, HoursInJuly)
                .Select(_ => 200.0 + random.Next(0, 100))
                .ToArray();
            const double hydroCapacityMw = 50;
            const double capacityFactor = 0.3;
            var hydro = new GeneratingFleet(
                GenerationTechnology.Hydro,
                Power.FromMegawatts(hydroCapacityMw),
                new Dictionary<DateOnly, double> { [new DateOnly(2026, 7, 1)] = capacityFactor });
            var region = new Region(
                "NSW1",
                [hydro, Fleet(GenerationTechnology.Gas, 400)],
                HourlyFlow(demand));

            DispatchOutcome outcome = Dispatch(region);

            double allocatedBudgetMwh = hydroCapacityMw * HoursInJuly * capacityFactor;
            double deliveredMwh = outcome.PerFleetGeneration[GenerationTechnology.Hydro]
                .Integrate().MegawattHours;

            // Ample Gas backup means the deficit never reaches DispatchHydroFallback, so only
            // the 90% paced pool is ever spent (and not quite fully, since the bisection still
            // leaves some slack) - utilisation lands around 90%, not the ~7% the stranding bug
            // this test guards against would produce.
            (deliveredMwh / allocatedBudgetMwh).Should().BeGreaterThan(
                0.85,
                "the pacer should spend most of a modest budget when demand is "
                + "persistently available to absorb it");
        }

        [Fact]
        public void Dispatch_HydroPacing_DecisionAtEachHourIsUnaffectedByLaterHoursDemand()
        {
            // Strict causality: RegionalDispatchRun must call HydroReservationState.Observe
            // AFTER an interval's own dispatch decision, never before - otherwise a later
            // hour's demand could leak into an earlier hour's pacing, which would be
            // foresight. Two runs that agree up to hour 50 and diverge wildly after it must
            // produce byte-identical Hydro dispatch for every hour before the divergence.
            var random = new Random(99);
            double[] baseDemand = Enumerable.Range(0, 100)
                .Select(_ => (double)random.Next(0, 60))
                .ToArray();
            double[] demandA = (double[])baseDemand.Clone();
            double[] demandB = (double[])baseDemand.Clone();
            for (int hour = 50; hour < demandB.Length; hour++)
            {
                demandB[hour] = 1_000;
            }

            DispatchOutcome outcomeA = Dispatch(HydroOnlyRegion(demandA, TimeSpan.FromHours(1)));
            DispatchOutcome outcomeB = Dispatch(HydroOnlyRegion(demandB, TimeSpan.FromHours(1)));

            for (int hour = 0; hour < 50; hour++)
            {
                outcomeA.PerFleetGeneration[GenerationTechnology.Hydro][hour].Megawatts.Should().Be(
                    outcomeB.PerFleetGeneration[GenerationTechnology.Hydro][hour].Megawatts,
                    $"hour {hour} must not depend on demand at or after hour 50");
            }
        }

        [Fact]
        public void Dispatch_HydroPacing_DeliversComparableEnergyAtHourlyAndHalfHourlyResolution()
        {
            // The pacer's trailing window is a fixed number of INTERVALS (336), so at 30-minute
            // resolution it only spans 7 days of history instead of 14 - the reviewer's own
            // simulation found this insensitive to within ~2% for window lengths from 168 to
            // 720, so "comparable" (a tolerance), not "identical", is the right bar here.
            var random = new Random(2024);
            double[] hourlyDemand = Enumerable.Range(0, HoursInJuly)
                .Select(_ => (double)random.Next(0, 60))
                .ToArray();
            double[] halfHourlyDemand = hourlyDemand
                .SelectMany(megawatts => new[] { megawatts, megawatts })
                .ToArray();

            DispatchOutcome hourlyOutcome = Dispatch(
                HydroOnlyRegion(hourlyDemand, TimeSpan.FromHours(1)));
            DispatchOutcome halfHourlyOutcome = Dispatch(
                HydroOnlyRegion(halfHourlyDemand, TimeSpan.FromMinutes(30)));

            double hourlyMwh = hourlyOutcome.PerFleetGeneration[GenerationTechnology.Hydro]
                .Integrate().MegawattHours;
            double halfHourlyMwh = halfHourlyOutcome.PerFleetGeneration[GenerationTechnology.Hydro]
                .Integrate().MegawattHours;

            (halfHourlyMwh / hourlyMwh).Should().BeApproximately(1.0, 0.05);
        }

        [Fact]
        public void Dispatch_RegionWithoutHydro_IsUnaffectedByThePacer()
        {
            FlowSeries demand = HourlyFlow(50);
            var region = new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Coal, 30), Fleet(GenerationTechnology.Gas, 30)],
                demand);

            DispatchOutcome outcome = Dispatch(region);

            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Coal], 30);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Gas], 20);
            AssertSeries(outcome.Unserved, 0);
        }

        private static Region HydroOnlyRegion(double[] demandMw, TimeSpan resolution)
        {
            var hydro = new GeneratingFleet(
                GenerationTechnology.Hydro,
                Power.FromMegawatts(60),
                new Dictionary<DateOnly, double> { [new DateOnly(2026, 7, 1)] = 0.3 });
            return new Region(
                "NSW1",
                [hydro],
                new FlowSeries(NemStart, resolution, demandMw));
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
        public void DispatchOutcome_CopiesAndExposesReadOnlyPerFleetFlows()
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
            var delivered = new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = HourlyFlow(10),
            };
            var charge = new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = zero,
            };
            var outcome = new DispatchOutcome(
                "NSW1",
                generation,
                curtailment,
                delivered,
                charge,
                HourlyFlow(10),
                zero,
                zero,
                zero,
                zero,
                zero);

            generation.Clear();
            curtailment.Clear();
            delivered.Clear();
            charge.Clear();
            var mutableView = (IDictionary<GenerationTechnology, FlowSeries>)outcome.PerFleetGeneration;
            var mutableDelivered = (IDictionary<GenerationTechnology, FlowSeries>)outcome.PerFleetDelivered;
            var mutableCharge = (IDictionary<GenerationTechnology, FlowSeries>)outcome.PerFleetCharge;
            var addGeneration = () => mutableView.Add(GenerationTechnology.Gas, HourlyFlow(0));
            var addDelivered = () => mutableDelivered.Add(GenerationTechnology.Gas, HourlyFlow(0));
            var addCharge = () => mutableCharge.Add(GenerationTechnology.Gas, HourlyFlow(0));

            outcome.PerFleetGeneration.Should().ContainKey(GenerationTechnology.Coal);
            outcome.PerFleetCurtailment.Should().ContainKey(GenerationTechnology.Coal);
            addGeneration.Should().Throw<NotSupportedException>();
            addDelivered.Should().Throw<NotSupportedException>();
            addCharge.Should().Throw<NotSupportedException>();
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
        public void DispatchOutcome_UsesComposedDemandForEnergyBalance()
        {
            var demandProfile = new DemandProfile(
                HourlyFlow(100),
                [new DemandComponent("Firm load", HourlyFlow(500))]);

            var act = () => Outcome(
                generation: [500],
                demand: [0],
                curtailment: [0],
                unserved: [0],
                demandProfile: demandProfile);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("Energy balance failed at index 0*");
        }

        [Fact]
        public void Dispatch_UsesAdditiveDemandComponentsInServedDemand()
        {
            var region = new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Coal, 1_000)],
                HourlyFlow(100),
                [new DemandComponent("Firm load", HourlyFlow(500))]);

            DispatchOutcome outcome = Dispatch(region);

            AssertSeries(outcome.Demand, 600);
            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Coal], 600);
            AssertSeries(outcome.Unserved, 0);
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
        public void DispatchOutcome_DerivesDeliveredToLoadAndAcceptsConsistentPerFleetFlows()
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
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = HourlyFlow(7),
                },
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = HourlyFlow(3),
                },
                HourlyFlow(7),
                zero,
                HourlyFlow(3),
                zero,
                zero,
                zero);

            AssertSeries(outcome.Charge, 3);
            AssertSeries(outcome.DeliveredToLoad, 7);
            AssertSeries(outcome.PerFleetCharge[GenerationTechnology.Coal], 3);
            AssertSeries(outcome.PerFleetDelivered[GenerationTechnology.Coal], 7);
        }

        [Fact]
        public void DispatchOutcome_RejectsInconsistentSuppliedPerFleetFlows()
        {
            FlowSeries zero = HourlyFlow(0);
            var act = () => new DispatchOutcome(
                "NSW1",
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = HourlyFlow(10),
                },
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = zero,
                },
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = HourlyFlow(8),
                },
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = HourlyFlow(1),
                },
                HourlyFlow(9),
                zero,
                HourlyFlow(1),
                zero,
                zero,
                zero);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("Per-fleet energy balance failed at index 0*");
        }

        [Fact]
        public void DispatchOutcome_RejectsIncrementalGenerationChargeDoubleCount()
        {
            FlowSeries zero = HourlyFlow(0);
            var act = () => new DispatchOutcome(
                "NSW1",
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = HourlyFlow(10),
                },
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = zero,
                },
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = HourlyFlow(10),
                },
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = HourlyFlow(10),
                },
                HourlyFlow(10),
                zero,
                HourlyFlow(10),
                zero,
                zero,
                zero);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("Per-fleet energy balance failed at index 0*");
        }

        [Fact]
        public void Dispatch_BrokenPolicyWithDuplicateIncrementalSource_Throws()
        {
            var region = new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Coal, 10)],
                HourlyFlow(0),
                storageFleets: [Battery(storageCapacityMwh: 20, powerCapacityMw: 10)]);

            var act = () => Dispatch(region, new DuplicateIncrementalGenerationChargingPolicy());

            act.Should().Throw<ArgumentException>()
                .WithParameterName("intents")
                .WithMessage("A decision cannot contain multiple incremental-generation charge intents*");
        }

        private static DispatchOutcome Outcome(
            double[] generation,
            double[] demand,
            double[] curtailment,
            double[] unserved,
            string regionId = "NSW1",
            DemandProfile? demandProfile = null)
        {
            FlowSeries zero = HourlyFlow(new double[demand.Length]);
            FlowSeries generationFlow = HourlyFlow(generation);
            FlowSeries curtailmentFlow = HourlyFlow(curtailment);
            return new DispatchOutcome(
                regionId,
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = generationFlow,
                },
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = curtailmentFlow,
                },
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = generationFlow.Subtract(curtailmentFlow),
                },
                new Dictionary<GenerationTechnology, FlowSeries>
                {
                    [GenerationTechnology.Coal] = zero,
                },
                demandProfile?.TotalDemand ?? HourlyFlow(demand),
                HourlyFlow(unserved),
                zero,
                zero,
                zero,
                zero,
                demandProfile: demandProfile);
        }

        private static DispatchOutcome Dispatch(
            Region region,
            IStoragePolicy? storagePolicy = null) =>
            (storagePolicy is null
                ? Dispatcher.Dispatch(PowerSystem(region))
                : Dispatcher.Dispatch(PowerSystem(region), storagePolicy)).Single();

        private static DispatchOutcome RandomizedStorageOutcome(int seed) =>
            Dispatch(RandomizedStorageRegion(seed));

        private static Region RandomizedStorageRegion(int seed)
        {
            var random = new Random(seed);
            double[] demandValues = Enumerable.Range(0, 96)
                .Select(hour => hour % 4 == 0
                    ? 0
                    : (double)random.Next(10, 101))
                .ToArray();
            FlowSeries demand = HourlyFlowAt(NemStart.AddHours(12), demandValues);
            var region = new Region(
                "NSW1",
                [
                    Fleet(GenerationTechnology.Solar, 30),
                    Fleet(GenerationTechnology.Coal, 40),
                    Fleet(GenerationTechnology.Gas, 20),
                ],
                demand,
                resourceProfile: RegionalResources(demand),
                storageFleets: [Battery(storageCapacityMwh: 60, powerCapacityMw: 20)]);

            return region;
        }

        private static PowerSystem PowerSystem(params Region[] regions) =>
            new(
                new PowerSystemId("test-power-system"),
                new ScenarioId("test-scenario"),
                regions);

        private static GeneratingFleet Fleet(
            GenerationTechnology technology,
            double capacityMw,
            decimal shortRunMarginalCostAudPerMwh = 0m) =>
            new(
                technology,
                Power.FromMegawatts(capacityMw),
                monthlyCapacityFactors: technology == GenerationTechnology.Hydro
                    ? new Dictionary<DateOnly, double>
                    {
                        [new DateOnly(2026, 7, 1)] = 1,
                    }
                    : null,
                shortRunMarginalCost: GenerationEnergyCost.FromAudPerMwhGenerated(
                    shortRunMarginalCostAudPerMwh));

        private static StorageFleet Battery(double storageCapacityMwh, double powerCapacityMw) =>
            new(
                StorageTechnology.Battery,
                Energy.FromMegawattHours(storageCapacityMwh),
                Power.FromMegawatts(powerCapacityMw),
                new StorageTechnologyProfile(15u, 0.87),
                Energy.Zero);

        /// <summary>A Battery fleet seeded fully charged, so it has discharge headroom from hour 0.</summary>
        private static StorageFleet SeededBattery(double storageCapacityMwh, double powerCapacityMw) =>
            new(
                StorageTechnology.Battery,
                Energy.FromMegawattHours(storageCapacityMwh),
                Power.FromMegawatts(powerCapacityMw),
                new StorageTechnologyProfile(15u, 0.87),
                Energy.FromMegawattHours(storageCapacityMwh));

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

        private sealed class ContextRecordingPolicy : IStoragePolicy
        {
            public List<DispatchContext> Contexts { get; } = [];

            public StorageDecision Decide(DispatchContext context)
            {
                Contexts.Add(context);
                return StorageDecision.None;
            }
        }

        private sealed class DuplicateIncrementalGenerationChargingPolicy : IStoragePolicy
        {
            public StorageDecision Decide(DispatchContext context) => new(
            [
                new StorageIntent(
                    StorageTechnology.Battery,
                    Power.FromMegawatts(-1),
                    ChargeSource.IncrementalGeneration(GenerationTechnology.Coal)),
                new StorageIntent(
                    StorageTechnology.Battery,
                    Power.FromMegawatts(-1),
                    ChargeSource.IncrementalGeneration(GenerationTechnology.Coal)),
            ]);
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