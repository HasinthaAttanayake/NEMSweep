using AwesomeAssertions;
using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.Units;

namespace NEM.Model.Tests.Simulation
{
    public sealed class ReliabilityMetricsTests
    {
        private static readonly DateTimeOffset NemStart =
            new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

        [Fact]
        public void FromOutcome_CalculatesUnservedEnergyInMwhAndPercentageOfDemand()
        {
            ReliabilityMetrics metrics = ReliabilityMetrics.FromOutcome(
                Outcome(
                    demand: [100, 100],
                    unserved: [0, 20]));

            metrics.UnservedEnergy.Should().Be(Energy.FromMegawattHours(20));
            metrics.PeakUnservedPower.Should().Be(Power.FromMegawatts(20));
            metrics.UnservedEnergyPercentageOfDemand.Should().Be(10);
        }

        [Fact]
        public void FromOutcome_CalculatesHoursServedSeparately()
        {
            ReliabilityMetrics metrics = ReliabilityMetrics.FromOutcome(
                Outcome(
                    demand: [100, 100, 100, 100],
                    unserved: [0, 1, 0, 1]));

            metrics.UnservedHours.Should().Be(2);
            metrics.HoursServedFraction.Should().Be(0.5);
        }

        [Fact]
        public void FromOutcome_UnservedEnergyAndHoursAreNotInterchangeable()
        {
            ReliabilityMetrics lowUse = ReliabilityMetrics.FromOutcome(
                Outcome(
                    demand: [100, 100],
                    unserved: [10, 0]));
            ReliabilityMetrics highUse = ReliabilityMetrics.FromOutcome(
                Outcome(
                    demand: [100, 100],
                    unserved: [50, 0]));

            lowUse.UnservedHours.Should().Be(highUse.UnservedHours).And.Be(1);
            lowUse.HoursServedFraction.Should().Be(highUse.HoursServedFraction).And.Be(0.5);
            lowUse.UnservedEnergy.Should().Be(Energy.FromMegawattHours(10));
            highUse.UnservedEnergy.Should().Be(Energy.FromMegawattHours(50));
            lowUse.UnservedEnergyPercentageOfDemand.Should().Be(5);
            highUse.UnservedEnergyPercentageOfDemand.Should().Be(25);
        }

        [Fact]
        public void FromOutcome_ZeroDemandHasNoUnservedPercentage()
        {
            ReliabilityMetrics metrics = ReliabilityMetrics.FromOutcome(
                Outcome(demand: [0, 0], unserved: [0, 0]));

            metrics.UnservedEnergyPercentageOfDemand.Should().Be(0);
            metrics.PeakUnservedPower.Should().Be(Power.Zero);
            metrics.UnservedHours.Should().Be(0);
            metrics.HoursServedFraction.Should().Be(1);
        }

        [Fact]
        public void FromOutcome_RejectsNullOutcome()
        {
            var act = () => ReliabilityMetrics.FromOutcome(null!);

            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("dispatchOutcome");
        }

        [Fact]
        public void DispatchOutcome_CalculatesDeliveredRenewableSharesWithNativeDemandDenominator()
        {
            FlowSeries baseDemand = HourlyFlow([100]);
            FlowSeries additiveDemand = HourlyFlow([100]);
            FlowSeries totalDemand = baseDemand.Add(additiveDemand);
            FlowSeries zero = HourlyFlow([0]);
            var delivered = new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Solar] = HourlyFlow([40]),
                [GenerationTechnology.Wind] = HourlyFlow([20]),
                [GenerationTechnology.Hydro] = HourlyFlow([30]),
                [GenerationTechnology.Gas] = HourlyFlow([110]),
            };
            var outcome = new DispatchOutcome(
                "NSW1",
                delivered,
                delivered.ToDictionary(entry => entry.Key, _ => zero),
                delivered,
                delivered.ToDictionary(entry => entry.Key, _ => zero),
                totalDemand,
                zero,
                zero,
                zero,
                zero,
                zero,
                demandProfile: new DemandProfile(
                    baseDemand,
                    [new DemandComponent("Data centres", additiveDemand)]));

            outcome.RenewableShare.GridScaleShare.Should().Be(0.45);
            outcome.RenewableShare.NativeShare.Should().Be(0.6);
        }

        [Fact]
        public void DispatchOutcome_ReturnsZeroRenewableSharesWhenNoRenewableFleetExists()
        {
            DispatchOutcome outcome = Outcome(demand: [100], unserved: [0]);

            outcome.RenewableShare.GridScaleShare.Should().Be(0);
            outcome.RenewableShare.NativeShare.Should().Be(0);
        }

        private static DispatchOutcome Outcome(double[] demand, double[] unserved)
        {
            FlowSeries demandFlow = HourlyFlow(demand);
            FlowSeries unservedFlow = HourlyFlow(unserved);
            FlowSeries zero = HourlyFlow(new double[demand.Length]);
            var generation = new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = demandFlow.Subtract(unservedFlow),
            };
            var curtailment = new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = zero,
            };

            return new DispatchOutcome(
                "NSW1",
                generation,
                curtailment,
                generation,
                curtailment,
                demandFlow,
                unservedFlow,
                zero,
                zero,
                zero,
                zero);
        }

        private static FlowSeries HourlyFlow(double[] megawatts) =>
            new(NemStart, TimeSpan.FromHours(1), megawatts);
    }
}