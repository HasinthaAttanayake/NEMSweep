using AwesomeAssertions;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Simulation;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Tests.Simulation
{
    public sealed class StoragePolicyTests
    {
        private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);
        private readonly GreedyPolicy _policy = new();

        [Fact]
        public void Decide_Deficit_RequestsDischargeBoundedByHeadroom()
        {
            DispatchContext context = Context(
                residualMw: 30,
                Storage(StorageTechnology.Battery, levelMwh: 20, chargeMw: 10, dischargeMw: 12));

            StorageDecision decision = _policy.Decide(context);

            decision.Intents.Should().ContainSingle().Which.Should().Be(
                new StorageIntent(StorageTechnology.Battery, Power.FromMegawatts(12)));
        }

        [Fact]
        public void Decide_Surplus_RequestsSourceQualifiedChargeBoundedByHeadroom()
        {
            DispatchContext context = Context(
                residualMw: -30,
                Storage(StorageTechnology.Battery, levelMwh: 20, chargeMw: 12, dischargeMw: 10));

            StorageDecision decision = _policy.Decide(context);

            decision.Intents.Should().ContainSingle().Which.Should().Be(
                new StorageIntent(
                    StorageTechnology.Battery,
                    Power.FromMegawatts(-12),
                    ChargeSource.Surplus));
        }

        [Fact]
        public void Decide_FullOrEmptyFleet_EmitsNoIntent()
        {
            DispatchContext full = Context(
                residualMw: -10,
                Storage(StorageTechnology.Battery, levelMwh: 40, chargeMw: 0, dischargeMw: 10));
            DispatchContext empty = Context(
                residualMw: 10,
                Storage(StorageTechnology.Battery, levelMwh: 0, chargeMw: 10, dischargeMw: 0));

            _policy.Decide(full).Intents.Should().BeEmpty();
            _policy.Decide(empty).Intents.Should().BeEmpty();
        }

        [Fact]
        public void Decide_MultipleFleets_UsesTechnologyOrderAndThreadsResidual()
        {
            DispatchContext context = Context(
                residualMw: 15,
                Storage(StorageTechnology.PumpedHydro, levelMwh: 20, chargeMw: 10, dischargeMw: 10),
                Storage(StorageTechnology.Battery, levelMwh: 20, chargeMw: 10, dischargeMw: 10));

            StorageDecision decision = _policy.Decide(context);

            decision.Intents.Should().Equal(
                new StorageIntent(StorageTechnology.Battery, Power.FromMegawatts(10)),
                new StorageIntent(StorageTechnology.PumpedHydro, Power.FromMegawatts(5)));
        }

        [Fact]
        public void Decide_RepeatedWithSameContext_IsDeterministicAndDoesNotMutateContext()
        {
            var source = new List<StorageFleetSnapshot>
            {
                Storage(StorageTechnology.Battery, levelMwh: 20, chargeMw: 10, dischargeMw: 10),
            };
            var context = new DispatchContext(Power.FromMegawatts(8), source, [], OneHour);

            StorageDecision first = _policy.Decide(context);
            source.Clear();
            StorageDecision second = _policy.Decide(context);

            second.Should().BeEquivalentTo(first);
            context.StorageFleets.Should().ContainSingle();
        }

        [Fact]
        public void StorageIntent_RequiresChargeSourceOnlyForCharging()
        {
            var missingSource = () => new StorageIntent(
                StorageTechnology.Battery,
                Power.FromMegawatts(-1));
            var dischargeSource = () => new StorageIntent(
                StorageTechnology.Battery,
                Power.FromMegawatts(1),
                ChargeSource.Surplus);

            missingSource.Should().Throw<ArgumentException>().WithParameterName("chargeSource");
            dischargeSource.Should().Throw<ArgumentException>().WithParameterName("chargeSource");
        }

        [Fact]
        public void StorageDecision_AllowsOneSurplusAndMultipleIncrementalChargesForOneFleet()
        {
            var intents = new[]
            {
                new StorageIntent(
                    StorageTechnology.Battery,
                    Power.FromMegawatts(-10),
                    ChargeSource.Surplus),
                new StorageIntent(
                    StorageTechnology.Battery,
                    Power.FromMegawatts(-5),
                    ChargeSource.IncrementalGeneration(GenerationTechnology.Coal)),
                new StorageIntent(
                    StorageTechnology.Battery,
                    Power.FromMegawatts(-3),
                    ChargeSource.IncrementalGeneration(GenerationTechnology.Gas)),
            };

            StorageDecision decision = new(intents);

            decision.Intents.Should().Equal(intents);
        }

        [Fact]
        public void StorageDecision_RejectsMultipleSurplusChargesForOneFleet()
        {
            var action = () => new StorageDecision(
            [
                new StorageIntent(
                    StorageTechnology.Battery,
                    Power.FromMegawatts(-10),
                    ChargeSource.Surplus),
                new StorageIntent(
                    StorageTechnology.Battery,
                    Power.FromMegawatts(-5),
                    ChargeSource.Surplus),
            ]);

            action.Should().Throw<ArgumentException>().WithParameterName("intents");
        }

        [Fact]
        public void StorageDecision_RejectsMultipleIncrementalChargesFromOneGeneratorForOneFleet()
        {
            var action = () => new StorageDecision(
            [
                new StorageIntent(
                    StorageTechnology.Battery,
                    Power.FromMegawatts(-10),
                    ChargeSource.IncrementalGeneration(GenerationTechnology.Coal)),
                new StorageIntent(
                    StorageTechnology.Battery,
                    Power.FromMegawatts(-5),
                    ChargeSource.IncrementalGeneration(GenerationTechnology.Coal)),
            ]);

            action.Should().Throw<ArgumentException>().WithParameterName("intents");
        }

        [Fact]
        public void StorageDecision_RejectsMultipleDischargesForOneFleet()
        {
            var action = () => new StorageDecision(
            [
                new StorageIntent(StorageTechnology.Battery, Power.FromMegawatts(10)),
                new StorageIntent(StorageTechnology.Battery, Power.FromMegawatts(5)),
            ]);

            action.Should().Throw<ArgumentException>().WithParameterName("intents");
        }

        private static DispatchContext Context(
            double residualMw,
            params StorageFleetSnapshot[] storageFleets) =>
            new(Power.FromMegawatts(residualMw), storageFleets, [], OneHour);

        private static StorageFleetSnapshot Storage(
            StorageTechnology technology,
            double levelMwh,
            double chargeMw,
            double dischargeMw) =>
            new(
                technology,
                Energy.FromMegawattHours(levelMwh),
                Power.FromMegawatts(chargeMw),
                Power.FromMegawatts(dischargeMw));
    }
}