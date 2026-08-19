using AwesomeAssertions;
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

        // The next three tests exercise GenerationBudgetState's pool semantics directly
        // (mirroring GenerationBudgetState_SubHourlyRequests_AccountForIntervalDuration below)
        // rather than through a full Dispatch(...): HydroReservationState paces a budgeted
        // fleet's request against the CALENDAR month, regardless of how short a test's own
        // demand series is, so a 2-4 hour dispatch window can no longer observe "the whole
        // month's budget instantly" through the pacer - that greedy-exhaustion behaviour is
        // exactly what the pacer exists to prevent (NEM-076). What these tests actually
        // validate - independent per-month pools, a full calendar-month budget for a fleet
        // that only starts mid-month, one shared pool across separate dispatch windows in the
        // same month - are properties of the budget pool itself, so they're asserted there.

        [Fact]
        public void GenerationBudgetState_CrossingMonthBoundary_UsesEachMonthsIndependentBudget()
        {
            const double capacityMw = 50;
            var fleet = new GeneratingFleet(
                GenerationTechnology.Hydro,
                Power.FromMegawatts(capacityMw),
                new Dictionary<DateOnly, double>
                {
                    [new DateOnly(2026, 1, 1)] = FactorForBudget(50, capacityMw, 2026, 1),
                    [new DateOnly(2026, 2, 1)] = FactorForBudget(50, capacityMw, 2026, 2),
                });
            var budget = new GenerationBudgetState(fleet);
            var hour = new DateTimeOffset(2026, 1, 31, 22, 0, 0, NemOffset);
            TimeSpan resolution = TimeSpan.FromHours(1);

            Power[] generation =
            [
                budget.Take(Power.FromMegawatts(capacityMw), hour, resolution),
                budget.Take(Power.FromMegawatts(capacityMw), hour.AddHours(1), resolution),
                budget.Take(Power.FromMegawatts(capacityMw), hour.AddHours(2), resolution),
                budget.Take(Power.FromMegawatts(capacityMw), hour.AddHours(3), resolution),
            ];

            AssertApproximatelyEqual(generation.Select(power => power.Megawatts), [50, 0, 50, 0]);
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
        public void GenerationBudgetState_PartialMonth_ReceivesFullCalendarMonthBudget()
        {
            const double capacityMw = 50;
            var fleet = new GeneratingFleet(
                GenerationTechnology.Hydro,
                Power.FromMegawatts(capacityMw),
                new Dictionary<DateOnly, double>
                {
                    [new DateOnly(2026, 7, 1)] = FactorForBudget(100, capacityMw, 2026, 7),
                });
            var budget = new GenerationBudgetState(fleet);
            var hour = new DateTimeOffset(2026, 7, 15, 0, 0, 0, NemOffset);
            TimeSpan resolution = TimeSpan.FromHours(1);

            Power[] generation =
            [
                budget.Take(Power.FromMegawatts(capacityMw), hour, resolution),
                budget.Take(Power.FromMegawatts(capacityMw), hour.AddHours(1), resolution),
                budget.Take(Power.FromMegawatts(capacityMw), hour.AddHours(2), resolution),
            ];

            AssertApproximatelyEqual(generation.Select(power => power.Megawatts), [50, 50, 0]);
        }

        [Fact]
        public void GenerationBudgetState_SeparateWindowsInSameMonth_ShareOnePool()
        {
            const double capacityMw = 50;
            var fleet = new GeneratingFleet(
                GenerationTechnology.Hydro,
                Power.FromMegawatts(capacityMw),
                new Dictionary<DateOnly, double>
                {
                    [new DateOnly(2026, 7, 1)] = FactorForBudget(100, capacityMw, 2026, 7),
                });
            var budget = new GenerationBudgetState(fleet);
            TimeSpan resolution = TimeSpan.FromHours(1);
            var firstWindow = new DateTimeOffset(2026, 7, 1, 0, 0, 0, NemOffset);
            var secondWindow = new DateTimeOffset(2026, 7, 2, 0, 0, 0, NemOffset);

            double firstMwh = new[]
            {
                budget.Take(Power.FromMegawatts(capacityMw), firstWindow, resolution),
                budget.Take(Power.FromMegawatts(capacityMw), firstWindow.AddHours(1), resolution),
            }.Sum(power => (power * resolution).MegawattHours);
            double secondMwh = new[]
            {
                budget.Take(Power.FromMegawatts(capacityMw), secondWindow, resolution),
                budget.Take(Power.FromMegawatts(capacityMw), secondWindow.AddHours(1), resolution),
            }.Sum(power => (power * resolution).MegawattHours);

            // One 100 MWh pool for the whole month, not 100 MWh available again to each window.
            (firstMwh + secondMwh).Should().BeApproximately(100, 1e-9);
            firstMwh.Should().BeApproximately(100, 1e-9);
            secondMwh.Should().BeApproximately(0, 1e-9);
        }

        [Fact]
        public void GenerationBudgetState_SubHourlyRequests_AccountForIntervalDuration()
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
            var budget = new GenerationBudgetState(fleet);
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

        [Fact]
        public void Dispatch_PacedHydro_SpendsSubstantiallyAllOfEachMonthlyBudget()
        {
            // The regression this whole feature exists to prevent, asserted directly: a fleet
            // whose budget the month's demand can absorb must actually SPEND that budget. Both
            // failure modes this has had - greedy SRMC ordering burning it in the opening days,
            // and last-resort ordering stranding 93% of it - leave this assertion intact only
            // if the energy genuinely reaches load. NEM-076.
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, NemOffset);
            const double capacityMw = 100;
            int hours = DateTime.DaysInMonth(2026, 1) * 24;
            const double budgetMwh = 20_000;
            var factors = new Dictionary<DateOnly, double>
            {
                [new DateOnly(2026, 1, 1)] = FactorForBudget(budgetMwh, capacityMw, 2026, 1),
            };

            // A demand shape with real peaks and troughs, so pacing has something to shave.
            double[] demandMw = Enumerable.Range(0, hours)
                .Select(hour => 60 + (40 * Math.Sin(hour * Math.PI / 12.0)))
                .ToArray();

            DispatchOutcome outcome = Dispatch(
                start,
                TimeSpan.FromHours(1),
                demandMw,
                capacityMw,
                factors);

            double generatedMwh = outcome.PerFleetGeneration[GenerationTechnology.Hydro]
                .Integrate().MegawattHours;
            generatedMwh.Should().BeGreaterThan(
                budgetMwh * 0.95,
                "a budget the month's demand can absorb must be spent, not stranded");
            generatedMwh.Should().BeLessThanOrEqualTo(budgetMwh + 1e-6);
        }

        [Fact]
        public void Dispatch_PacedHydro_IsUnaffectedByDemandInLaterIntervals()
        {
            // The no-foresight constraint, asserted as a property rather than by inspection:
            // rewriting the tail of the demand series must not move a single earlier decision.
            // Any pacing rule that peeked ahead - even to compute a monthly threshold - would
            // fail this. NEM-076.
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, NemOffset);
            const double capacityMw = 100;
            const int splitHour = 240;
            int hours = DateTime.DaysInMonth(2026, 1) * 24;
            var factors = new Dictionary<DateOnly, double>
            {
                [new DateOnly(2026, 1, 1)] = FactorForBudget(20_000, capacityMw, 2026, 1),
            };
            double[] baseline = Enumerable.Range(0, hours)
                .Select(hour => 60 + (40 * Math.Sin(hour * Math.PI / 12.0)))
                .ToArray();
            double[] mutatedTail = baseline.ToArray();
            for (int hour = splitHour; hour < hours; hour++)
            {
                mutatedTail[hour] = hour % 2 == 0 ? 0 : capacityMw;
            }

            FlowSeries original = Dispatch(start, TimeSpan.FromHours(1), baseline, capacityMw, factors)
                .PerFleetGeneration[GenerationTechnology.Hydro];
            FlowSeries mutated = Dispatch(start, TimeSpan.FromHours(1), mutatedTail, capacityMw, factors)
                .PerFleetGeneration[GenerationTechnology.Hydro];

            // Guard against the assertion below passing because nothing ran at all.
            Integrate(original, 0, splitHour).Should().BeGreaterThan(
                0, "the compared window must contain real dispatch decisions");

            for (int hour = 0; hour < splitHour; hour++)
            {
                mutated[hour].Megawatts.Should().Be(
                    original[hour].Megawatts,
                    $"hour {hour} precedes the only demand that changed (hour {splitHour} onward)");
            }
        }

        [Fact]
        public void Dispatch_PacedHydro_DeliversTheSameEnergyAtHourlyAndHalfHourlyResolution()
        {
            // Pacing works in intervals, so nothing in it may assume an hour. Same underlying
            // demand profile, two resolutions, same energy delivered.
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, NemOffset);
            const double capacityMw = 100;
            int hours = DateTime.DaysInMonth(2026, 1) * 24;
            var factors = new Dictionary<DateOnly, double>
            {
                [new DateOnly(2026, 1, 1)] = FactorForBudget(20_000, capacityMw, 2026, 1),
            };
            double[] hourly = Enumerable.Range(0, hours)
                .Select(hour => 60 + (40 * Math.Sin(hour * Math.PI / 12.0)))
                .ToArray();
            double[] halfHourly = hourly.SelectMany(value => new[] { value, value }).ToArray();

            double hourlyMwh = Dispatch(start, TimeSpan.FromHours(1), hourly, capacityMw, factors)
                .PerFleetGeneration[GenerationTechnology.Hydro].Integrate().MegawattHours;
            double halfHourlyMwh = Dispatch(
                    start, TimeSpan.FromMinutes(30), halfHourly, capacityMw, factors)
                .PerFleetGeneration[GenerationTechnology.Hydro].Integrate().MegawattHours;

            halfHourlyMwh.Should().BeApproximately(hourlyMwh, hourlyMwh * 0.02);
        }

        [Fact]
        public void GenerationBudgetState_ReleaseUnspentReserve_MovesReserveIntoThePacedPool()
        {
            const double capacityMw = 100;
            var fleet = new GeneratingFleet(
                GenerationTechnology.Hydro,
                Power.FromMegawatts(capacityMw),
                new Dictionary<DateOnly, double>
                {
                    [new DateOnly(2026, 1, 1)] = FactorForBudget(1_000, capacityMw, 2026, 1),
                });
            var budget = new GenerationBudgetState(fleet, reserveFraction: 0.1);
            var instant = new DateTimeOffset(2026, 1, 29, 0, 0, 0, NemOffset);
            TimeSpan resolution = TimeSpan.FromHours(1);

            budget.PacedRemaining(instant).MegawattHours.Should().BeApproximately(900, 1e-9);

            Energy released = budget.ReleaseUnspentReserve(instant);

            released.MegawattHours.Should().BeApproximately(100, 1e-9);
            budget.PacedRemaining(instant).MegawattHours.Should().BeApproximately(1_000, 1e-9);
            budget.ReserveHeadroom(
                Power.FromMegawatts(capacityMw), Power.Zero, instant, resolution)
                .Should().Be(Power.Zero);

            // Idempotent: the reserve is drained, not double-counted.
            budget.ReleaseUnspentReserve(instant).Should().Be(Energy.Zero);
            budget.PacedRemaining(instant).MegawattHours.Should().BeApproximately(1_000, 1e-9);
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

        private static void AssertApproximatelyEqual(
            IEnumerable<double> actual,
            double[] expected)
        {
            double[] actualValues = actual.ToArray();
            actualValues.Length.Should().Be(expected.Length);
            for (int index = 0; index < expected.Length; index++)
            {
                actualValues[index].Should().BeApproximately(expected[index], 1e-9);
            }
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