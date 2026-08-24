using AwesomeAssertions;
using NEMSweep.Model.Simulation;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Tests.Simulation
{
    public sealed class HydroReservationStateTests
    {
        [Fact]
        public void OfftakeCap_DuringWarmUp_RunsFlatAtAffordablePaceRatherThanZeroOrFlatOut()
        {
            var pacer = new HydroReservationState();

            // Fewer than 48 observations (none, here) means warm-up: dispatch is bounded by
            // the affordable average (remainingBudget / intervalsLeft = 480/480 = 1 MW), not
            // by zero (which would waste a fresh fleet's early hours) or by nameplate/residual
            // (which would burn the whole budget immediately).
            Power cap = pacer.OfftakeCap(
                nameplateCapacity: Power.FromMegawatts(100),
                remainingBudget: Energy.FromMegawattHours(480),
                intervalsLeftInMonth: 480,
                residualDemand: Power.FromMegawatts(50),
                resolution: TimeSpan.FromHours(1));

            cap.Megawatts.Should().BeApproximately(1, 1e-9);
        }

        [Fact]
        public void OfftakeCap_ThresholdRisesWhenRecentDemandRanWellAheadOfPace()
        {
            var pacer = new HydroReservationState();
            for (int interval = 0; interval < 400; interval++)
            {
                pacer.Observe(Power.FromMegawatts(100));
            }

            // A 900 MWh budget over 900 remaining intervals affords only 1 MW/interval on
            // average, but the trailing window shows the fleet has been asked for 100 MW every
            // interval - a threshold well above zero must have been found to bring the paced
            // average back down, even though nameplate and residual demand alone would allow
            // full output this interval.
            Power cap = pacer.OfftakeCap(
                nameplateCapacity: Power.FromMegawatts(100),
                remainingBudget: Energy.FromMegawattHours(900),
                intervalsLeftInMonth: 900,
                residualDemand: Power.FromMegawatts(100),
                resolution: TimeSpan.FromHours(1));

            cap.Megawatts.Should().BeLessThan(50);
        }

        [Fact]
        public void OfftakeCap_NoGatingNeededWhenHistoricalDemandNeverExceededPace()
        {
            var pacer = new HydroReservationState();
            for (int interval = 0; interval < 400; interval++)
            {
                pacer.Observe(Power.FromMegawatts(0.5));
            }

            // The window shows demand (0.5 MW) comfortably below the affordable pace
            // (900/900 = 1 MW) - even running flat-out whenever there was any residual
            // demand wouldn't have used the full budget, so no threshold is needed and
            // dispatch simply follows residual demand up to nameplate.
            Power cap = pacer.OfftakeCap(
                nameplateCapacity: Power.FromMegawatts(100),
                remainingBudget: Energy.FromMegawattHours(900),
                intervalsLeftInMonth: 900,
                residualDemand: Power.FromMegawatts(10),
                resolution: TimeSpan.FromHours(1));

            cap.Megawatts.Should().BeApproximately(10, 1e-6);
        }

        [Fact]
        public void OfftakeCap_ReturnsZero_WhenRemainingBudgetIsExhausted()
        {
            var pacer = new HydroReservationState();

            Power cap = pacer.OfftakeCap(
                nameplateCapacity: Power.FromMegawatts(100),
                remainingBudget: Energy.Zero,
                intervalsLeftInMonth: 400,
                residualDemand: Power.FromMegawatts(50),
                resolution: TimeSpan.FromHours(1));

            cap.Should().Be(Power.Zero);
        }

        [Fact]
        public void OfftakeCap_ReturnsZero_WhenNoIntervalsRemainInTheMonth()
        {
            var pacer = new HydroReservationState();

            Power cap = pacer.OfftakeCap(
                nameplateCapacity: Power.FromMegawatts(100),
                remainingBudget: Energy.FromMegawattHours(50),
                intervalsLeftInMonth: 0,
                residualDemand: Power.FromMegawatts(50),
                resolution: TimeSpan.FromHours(1));

            cap.Should().Be(Power.Zero);
        }

        [Fact]
        public void OfftakeCap_NeverExceedsNameplateCapacity()
        {
            var pacer = new HydroReservationState();
            for (int interval = 0; interval < 400; interval++)
            {
                pacer.Observe(Power.FromMegawatts(1));
            }

            Power cap = pacer.OfftakeCap(
                nameplateCapacity: Power.FromMegawatts(10),
                remainingBudget: Energy.FromMegawattHours(1_000_000),
                intervalsLeftInMonth: 1,
                residualDemand: Power.FromMegawatts(500),
                resolution: TimeSpan.FromHours(1));

            cap.Megawatts.Should().BeLessThanOrEqualTo(10);
        }
    }
}
