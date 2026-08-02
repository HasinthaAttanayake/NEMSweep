using FluentAssertions;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.Units;

namespace NEM.Model.Tests.Simulation
{
    public sealed class HydroEnergyBudgetTests
    {
        private static readonly TimeSpan NemOffset = TimeSpan.FromHours(10);

        [Fact]
        public void Dispatch_FullYear_EnforcesEachMonthlyBudgetSeparately()
        {
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, NemOffset);
            const double capacityMw = 10;
            double[] targetMonthlyMwh = Enumerable.Range(1, 12)
                .Select(month => month * 10.0)
                .ToArray();
            var factors = Enumerable.Range(1, 12).ToDictionary(
                month => new DateOnly(2026, month, 1),
                month => targetMonthlyMwh[month - 1]
                    / (capacityMw * DateTime.DaysInMonth(2026, month) * 24));
            int hours = 365 * 24;

            DispatchOutcome outcome = Dispatch(
                start,
                TimeSpan.FromHours(1),
                Enumerable.Repeat(capacityMw, hours).ToArray(),
                capacityMw,
                factors);

            FlowSeries generation = outcome.PerFleetGeneration[GenerationTechnology.Hydro];
            int firstHour = 0;
            for (int month = 1; month <= 12; month++)
            {
                int hoursInMonth = DateTime.DaysInMonth(2026, month) * 24;
                double generatedMwh = Integrate(generation, firstHour, hoursInMonth);
                generatedMwh.Should().BeApproximately(targetMonthlyMwh[month - 1], 1e-9);
                firstHour += hoursInMonth;
            }
        }

        [Fact]
        public void Dispatch_CrossingMonthBoundary_UsesEachMonthsIndependentBudget()
        {
            var start = new DateTimeOffset(2026, 1, 31, 22, 0, 0, NemOffset);
            const double capacityMw = 50;
            var factors = new Dictionary<DateOnly, double>
            {
                [new DateOnly(2026, 1, 1)] = FactorForBudget(50, capacityMw, 2026, 1),
                [new DateOnly(2026, 2, 1)] = FactorForBudget(50, capacityMw, 2026, 2),
            };

            DispatchOutcome outcome = Dispatch(
                start,
                TimeSpan.FromHours(1),
                [50, 50, 50, 50],
                capacityMw,
                factors);

            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Hydro], 50, 0, 50, 0);
        }

        [Fact]
        public void Dispatch_LeapYearFebruary_UsesTwentyNineDayBudget()
        {
            var start = new DateTimeOffset(2028, 2, 1, 0, 0, 0, NemOffset);
            const double capacityMw = 100;
            const double capacityFactor = 0.25;
            int hours = 29 * 24;

            DispatchOutcome outcome = Dispatch(
                start,
                TimeSpan.FromHours(1),
                Enumerable.Repeat(capacityMw, hours).ToArray(),
                capacityMw,
                new Dictionary<DateOnly, double>
                {
                    [new DateOnly(2028, 2, 1)] = capacityFactor,
                });

            outcome.PerFleetGeneration[GenerationTechnology.Hydro].Integrate().MegawattHours
                .Should().BeApproximately(capacityMw * hours * capacityFactor, 1e-9);
        }

        [Fact]
        public void Dispatch_ZeroCapacityFactor_ProducesNoHydroGeneration()
        {
            var start = new DateTimeOffset(2026, 7, 1, 0, 0, 0, NemOffset);

            DispatchOutcome outcome = Dispatch(
                start,
                TimeSpan.FromHours(1),
                [50, 50],
                capacityMw: 50,
                new Dictionary<DateOnly, double>
                {
                    [new DateOnly(2026, 7, 1)] = 0,
                });

            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Hydro], 0, 0);
            AssertSeries(outcome.Unserved, 50, 50);
        }

        [Fact]
        public void Dispatch_PartialMonth_ReceivesFullCalendarMonthBudget()
        {
            var start = new DateTimeOffset(2026, 7, 15, 0, 0, 0, NemOffset);
            const double capacityMw = 50;
            var factors = new Dictionary<DateOnly, double>
            {
                [new DateOnly(2026, 7, 1)] = FactorForBudget(100, capacityMw, 2026, 7),
            };

            DispatchOutcome outcome = Dispatch(
                start,
                TimeSpan.FromHours(1),
                [50, 50, 50],
                capacityMw,
                factors);

            AssertSeries(outcome.PerFleetGeneration[GenerationTechnology.Hydro], 50, 50, 0);
        }

        [Fact]
        public void Dispatch_SeparateWindowsInSameMonth_EachReceivesFullBudget()
        {
            const double capacityMw = 50;
            var factors = new Dictionary<DateOnly, double>
            {
                [new DateOnly(2026, 7, 1)] = FactorForBudget(100, capacityMw, 2026, 7),
            };

            DispatchOutcome first = Dispatch(
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, NemOffset),
                TimeSpan.FromHours(1),
                [50, 50, 50],
                capacityMw,
                factors);
            DispatchOutcome second = Dispatch(
                new DateTimeOffset(2026, 7, 2, 0, 0, 0, NemOffset),
                TimeSpan.FromHours(1),
                [50, 50, 50],
                capacityMw,
                factors);

            first.PerFleetGeneration[GenerationTechnology.Hydro].Integrate().MegawattHours
                .Should().BeApproximately(100, 1e-9);
            second.PerFleetGeneration[GenerationTechnology.Hydro].Integrate().MegawattHours
                .Should().BeApproximately(100, 1e-9);
        }

        [Fact]
        public void GenerationEnergyBudget_SubHourlyRequests_AccountForIntervalDuration()
        {
            var start = new DateTimeOffset(2026, 7, 1, 0, 0, 0, NemOffset);
            const double capacityMw = 100;
            var fleet = new GeneratingFleet(
                GenerationTechnology.Hydro,
                Power.FromMegawatts(capacityMw),
                new Dictionary<DateOnly, double>
                {
                    [new DateOnly(2026, 7, 1)] = FactorForBudget(75, capacityMw, 2026, 7),
                });
            GenerationEnergyBudget budget = fleet.CreateEnergyBudget();
            TimeSpan resolution = TimeSpan.FromMinutes(30);

            Power[] generation =
            [
                budget.Take(Power.FromMegawatts(100), start, resolution),
                budget.Take(Power.FromMegawatts(100), start.Add(resolution), resolution),
                budget.Take(Power.FromMegawatts(100), start.Add(resolution * 2), resolution),
            ];

            generation.Select(power => power.Megawatts).Should().Equal(100, 50, 0);
            generation.Sum(power => (power * resolution).MegawattHours).Should().Be(75);
        }

        private static DispatchOutcome Dispatch(
            DateTimeOffset start,
            TimeSpan resolution,
            double[] demandMw,
            double capacityMw,
            IReadOnlyDictionary<DateOnly, double> factors)
        {
            var hydro = new GeneratingFleet(
                GenerationTechnology.Hydro,
                Power.FromMegawatts(capacityMw),
                factors);
            var region = new Region(
                "NSW1",
                [hydro],
                new FlowSeries(start, resolution, demandMw));
            var powerSystem = new PowerSystem(
                new PowerSystemId("test-power-system"),
                new ScenarioId("test-scenario"),
                [region]);
            return Dispatcher.Dispatch(powerSystem).Single();
        }

        private static double FactorForBudget(
            double budgetMwh,
            double capacityMw,
            int year,
            int month) =>
            budgetMwh / (capacityMw * DateTime.DaysInMonth(year, month) * 24);

        private static double Integrate(FlowSeries series, int startIndex, int length)
        {
            double megawattHours = 0;
            for (int index = startIndex; index < startIndex + length; index++)
            {
                megawattHours += series[index].Megawatts * series.Resolution.TotalHours;
            }

            return megawattHours;
        }

        private static void AssertSeries(FlowSeries actual, params double[] expected)
        {
            actual.Length.Should().Be(expected.Length);
            for (int index = 0; index < expected.Length; index++)
            {
                actual[index].Megawatts.Should().BeApproximately(expected[index], 1e-9);
            }
        }
    }
}