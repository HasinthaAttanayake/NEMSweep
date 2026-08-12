using AwesomeAssertions;
using NEM.Model.Grid;
using NEM.Model.Simulation;
using NEM.Model.Units;

namespace NEM.Model.Tests.Simulation
{
    public sealed class GreedySurplusAndIncrementalGenerationChargingPolicyTests
    {
        private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);
        private readonly GreedySurplusAndIncrementalGenerationChargingPolicy _policy = new();

        [Fact]
        public void Decide_BalancedSystem_ChargesFromEligibleIncrementalGenerationInCostOrder()
        {
            StorageDecision decision = _policy.Decide(Context(
                residualMw: 0,
                storageFleets: [Storage(StorageTechnology.Battery, chargeMw: 10, dischargeMw: 10)],
                generationFleets:
                [
                    Generation(GenerationTechnology.Hydro, headroomMw: 100, costAudPerMwh: 1),
                    Generation(GenerationTechnology.Coal, headroomMw: 6, costAudPerMwh: 20),
                    Generation(GenerationTechnology.Gas, headroomMw: 9, costAudPerMwh: 80),
                ]));

            decision.Intents.Should().Equal(
                new StorageIntent(
                    StorageTechnology.Battery,
                    Power.FromMegawatts(-6),
                    ChargeSource.IncrementalGeneration(GenerationTechnology.Coal)),
                new StorageIntent(
                    StorageTechnology.Battery,
                    Power.FromMegawatts(-4),
                    ChargeSource.IncrementalGeneration(GenerationTechnology.Gas)));
        }

        [Fact]
        public void Decide_Deficit_DischargesStorageInTechnologyPriorityOrder()
        {
            StorageDecision decision = _policy.Decide(Context(
                residualMw: 15,
                storageFleets:
                [
                    Storage(StorageTechnology.PumpedHydro, chargeMw: 10, dischargeMw: 10),
                    Storage(StorageTechnology.Battery, chargeMw: 10, dischargeMw: 10),
                ]));

            decision.Intents.Should().Equal(
                new StorageIntent(StorageTechnology.Battery, Power.FromMegawatts(10)),
                new StorageIntent(StorageTechnology.PumpedHydro, Power.FromMegawatts(5)));
        }

        [Fact]
        public void Decide_Surplus_ChargesFromSurplusThenEligibleIncrementalGenerationInCostOrder()
        {
            StorageDecision decision = _policy.Decide(Context(
                residualMw: -5,
                storageFleets:
                [
                    Storage(StorageTechnology.PumpedHydro, chargeMw: 10, dischargeMw: 10),
                    Storage(StorageTechnology.Battery, chargeMw: 10, dischargeMw: 10),
                ],
                generationFleets:
                [
                    Generation(GenerationTechnology.Hydro, headroomMw: 100, costAudPerMwh: 1),
                    Generation(GenerationTechnology.Coal, headroomMw: 6, costAudPerMwh: 20),
                    Generation(GenerationTechnology.Gas, headroomMw: 9, costAudPerMwh: 80),
                ]));

            decision.Intents.Should().Equal(
                new StorageIntent(
                    StorageTechnology.Battery,
                    Power.FromMegawatts(-5),
                    ChargeSource.Surplus),
                new StorageIntent(
                    StorageTechnology.Battery,
                    Power.FromMegawatts(-5),
                    ChargeSource.IncrementalGeneration(GenerationTechnology.Coal)),
                new StorageIntent(
                    StorageTechnology.PumpedHydro,
                    Power.FromMegawatts(-1),
                    ChargeSource.IncrementalGeneration(GenerationTechnology.Coal)),
                new StorageIntent(
                    StorageTechnology.PumpedHydro,
                    Power.FromMegawatts(-9),
                    ChargeSource.IncrementalGeneration(GenerationTechnology.Gas)));
        }

        private static DispatchContext Context(
            double residualMw,
            IReadOnlyList<StorageFleetSnapshot> storageFleets,
            IReadOnlyList<GenerationFleetSnapshot>? generationFleets = null) =>
            new(
                Power.FromMegawatts(residualMw),
                storageFleets,
                generationFleets ?? [],
                OneHour);

        private static StorageFleetSnapshot Storage(
            StorageTechnology technology,
            double chargeMw,
            double dischargeMw) =>
            new(
                technology,
                Energy.Zero,
                Power.FromMegawatts(chargeMw),
                Power.FromMegawatts(dischargeMw));

        private static GenerationFleetSnapshot Generation(
            GenerationTechnology technology,
            double headroomMw,
            decimal costAudPerMwh) =>
            new(
                technology,
                Power.FromMegawatts(headroomMw),
                GenerationEnergyCost.FromAudPerMwhGenerated(costAudPerMwh));
    }
}