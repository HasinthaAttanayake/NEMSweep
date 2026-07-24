using FluentAssertions;
using NEM.Model.Generation.Solar;
using NEM.Model.Weather;

namespace NEM.Model.Tests.Generation.Solar
{
    public class GlobalTiltedIrradiationSeriesTests
    {
        private const double RelativeTolerance = 1e-12;
        private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
        private static readonly TimeSpan HalfHour = TimeSpan.FromMinutes(30);
        private static DateTimeOffset DaytimeStart =>
            new(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(10));
        private static DateTimeOffset NightStart =>
            new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(10));

        [Fact]
        public void Calculate_UsesDualAxisIsotropicFormula()
        {
            TraceSeries globalHorizontal = GlobalHorizontal(DaytimeStart, Hour, 800.0);
            TraceSeries directNormal = DirectNormal(DaytimeStart, Hour, 700.0);
            TraceSeries diffuseHorizontal = DiffuseHorizontal(DaytimeStart, Hour, 100.0);
            SolarZenithSeries solarZenith = Zenith(DaytimeStart, Hour);

            GlobalTiltedIrradiationSeries result = GlobalTiltedIrradiationSeries.Calculate(
                globalHorizontal, directNormal, diffuseHorizontal, solarZenith);

            double panelTiltRadians = solarZenith[0].Degrees * Math.PI / 180.0;
            double expected =
                700.0
                + 100.0 * ((1.0 + Math.Cos(panelTiltRadians)) / 2.0)
                + 800.0 * 0.2 * ((1.0 - Math.Cos(panelTiltRadians)) / 2.0);

            result[0].WattHoursPerSquareMetre.Should().BeApproximately(
                expected, Tolerance(expected));
            result.Start.Should().Be(DaytimeStart);
            result.Resolution.Should().Be(Hour);
        }

        [Fact]
        public void Calculate_IsZeroWhenSunIsAtOrBelowHorizon()
        {
            SolarZenithSeries solarZenith = Zenith(NightStart, Hour);
            solarZenith[0].Degrees.Should().BeGreaterThanOrEqualTo(90.0);

            GlobalTiltedIrradiationSeries result = GlobalTiltedIrradiationSeries.Calculate(
                GlobalHorizontal(NightStart, Hour, 800.0),
                DirectNormal(NightStart, Hour, 700.0),
                DiffuseHorizontal(NightStart, Hour, 100.0),
                solarZenith);

            result[0].WattHoursPerSquareMetre.Should().Be(0.0);
        }

        [Fact]
        public void Calculate_RejectsWrongTraceUnit()
        {
            TraceSeries wrongGlobalHorizontal = DirectNormal(DaytimeStart, Hour, 800.0);

            var act = () => GlobalTiltedIrradiationSeries.Calculate(
                wrongGlobalHorizontal,
                DirectNormal(DaytimeStart, Hour, 700.0),
                DiffuseHorizontal(DaytimeStart, Hour, 100.0),
                Zenith(DaytimeStart, Hour));

            act.Should().Throw<ArgumentException>()
                .Which.ParamName.Should().Be("globalHorizontalRadiation");
        }

        [Fact]
        public void Calculate_RejectsNegativeRadiation()
        {
            var act = () => GlobalTiltedIrradiationSeries.Calculate(
                GlobalHorizontal(DaytimeStart, Hour, -1.0),
                DirectNormal(DaytimeStart, Hour, 700.0),
                DiffuseHorizontal(DaytimeStart, Hour, 100.0),
                Zenith(DaytimeStart, Hour));

            act.Should().Throw<ArgumentOutOfRangeException>()
                .Which.ParamName.Should().Be("globalHorizontalRadiation");
        }

        [Theory]
        [InlineData("start")]
        [InlineData("resolution")]
        [InlineData("length")]
        public void Calculate_RejectsMisalignedWeather(string mismatch)
        {
            TraceSeries directNormal = mismatch switch
            {
                "start" => DirectNormal(DaytimeStart + Hour, Hour, 700.0),
                "resolution" => DirectNormal(DaytimeStart, HalfHour, 700.0),
                "length" => TraceSeries.DirectNormalRadiation(
                    DaytimeStart, Hour, new[] { 700.0, 700.0 }),
                _ => throw new ArgumentOutOfRangeException(nameof(mismatch)),
            };

            var act = () => GlobalTiltedIrradiationSeries.Calculate(
                GlobalHorizontal(DaytimeStart, Hour, 800.0),
                directNormal,
                DiffuseHorizontal(DaytimeStart, Hour, 100.0),
                Zenith(DaytimeStart, Hour));

            act.Should().Throw<ArgumentException>()
                .WithMessage($"*misaligned on {mismatch}*");
        }

        private static TraceSeries GlobalHorizontal(
            DateTimeOffset start, TimeSpan resolution, params double[] values) =>
            TraceSeries.GlobalHorizontalRadiation(start, resolution, values);

        private static TraceSeries DirectNormal(
            DateTimeOffset start, TimeSpan resolution, params double[] values) =>
            TraceSeries.DirectNormalRadiation(start, resolution, values);

        private static TraceSeries DiffuseHorizontal(
            DateTimeOffset start, TimeSpan resolution, params double[] values) =>
            TraceSeries.DiffuseHorizontalRadiation(start, resolution, values);

        private static SolarZenithSeries Zenith(DateTimeOffset start, TimeSpan resolution) =>
            SolarZenithSeries.Calculate(
                start, resolution, 1, latitude: -33.8688, longitude: 151.2093);

        private static double Tolerance(double expected) =>
            Math.Max(Math.Abs(expected) * RelativeTolerance, double.Epsilon);
    }
}