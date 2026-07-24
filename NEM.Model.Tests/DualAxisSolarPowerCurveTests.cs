using FluentAssertions;
using NEM.Model.PowerCurves;
using NEM.Model.Series;
using NEM.Model.Units;

namespace NemSim.Tests
{
    public class DualAxisSolarPowerCurveTests
    {
        private const double RelativeTolerance = 1e-12;
        private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
        private static readonly TimeSpan HalfHour = TimeSpan.FromMinutes(30);
        private static DateTimeOffset DaytimeStart =>
            new(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(10));

        [Fact]
        public void Calculate_ConvertsHalfHourlyIrradiationToAverageIrradiance()
        {
            TraceSeries dryBulb = DryBulb(DaytimeStart, HalfHour, 0.0);

            FlowSeries result = DualAxisSolarPowerCurve.Calculate(
                GlobalHorizontal(DaytimeStart, HalfHour, 0.0),
                DirectNormal(DaytimeStart, HalfHour, 500.0),
                DiffuseHorizontal(DaytimeStart, HalfHour, 0.0),
                dryBulb,
                Zenith(DaytimeStart, HalfHour),
                Power.FromMegawatts(100.0));

            // 500 Wh/m² over half an hour is 1000 W/m²; Tcell is 25°C, so only the 0.95 system factor applies.
            result[0].Megawatts.Should().BeApproximately(95.0, Tolerance(95.0));
            result.Integrate().MegawattHours.Should().BeApproximately(47.5, Tolerance(47.5));
        }

        [Fact]
        public void Calculate_AppliesExplicitCellTemperatureAssumptionAndTemperatureDegradation()
        {
            GlobalTiltedIrradiationSeries irradiation = CalculateIrradiation(
                DaytimeStart, Hour, globalHorizontal: 0.0, directNormal: 1000.0, diffuseHorizontal: 0.0);
            TraceSeries dryBulb = DryBulb(DaytimeStart, Hour, 25.0);

            FlowSeries result = DualAxisSolarPowerCurve.Calculate(
                irradiation, dryBulb, Power.FromMegawatts(100.0));

            double cellTemperature = 25.0 + 25.0;
            double temperatureFactor = 1.0 + (-0.0027) * (cellTemperature - 25.0);
            double expected = 100.0 / 1000.0 * 1000.0 * 0.95 * temperatureFactor;
            result[0].Megawatts.Should().BeApproximately(expected, Tolerance(expected));
        }

        [Fact]
        public void Calculate_CapsOutputAtAcCapacity()
        {
            GlobalTiltedIrradiationSeries irradiation = CalculateIrradiation(
                DaytimeStart, Hour, globalHorizontal: 0.0, directNormal: 2000.0, diffuseHorizontal: 0.0);
            TraceSeries dryBulb = DryBulb(DaytimeStart, Hour, -70.0);

            FlowSeries result = DualAxisSolarPowerCurve.Calculate(
                irradiation, dryBulb, Power.FromMegawatts(100.0));

            result[0].Megawatts.Should().Be(100.0);
        }

        [Fact]
        public void Calculate_RejectsMisalignedTemperature()
        {
            GlobalTiltedIrradiationSeries irradiation = CalculateIrradiation(
                DaytimeStart, Hour, globalHorizontal: 0.0, directNormal: 1000.0, diffuseHorizontal: 0.0);
            TraceSeries dryBulb = DryBulb(DaytimeStart + Hour, Hour, 25.0);

            var act = () => DualAxisSolarPowerCurve.Calculate(
                irradiation, dryBulb, Power.FromMegawatts(100.0));

            act.Should().Throw<ArgumentException>().WithMessage("*misaligned on start*");
        }

        [Fact]
        public void Calculate_RejectsNegativeAcCapacity()
        {
            GlobalTiltedIrradiationSeries irradiation = CalculateIrradiation(
                DaytimeStart, Hour, globalHorizontal: 0.0, directNormal: 1000.0, diffuseHorizontal: 0.0);

            var act = () => DualAxisSolarPowerCurve.Calculate(
                irradiation,
                DryBulb(DaytimeStart, Hour, 25.0),
                Power.FromMegawatts(-1.0));

            act.Should().Throw<ArgumentOutOfRangeException>()
                .Which.ParamName.Should().Be("acCapacity");
        }

        private static GlobalTiltedIrradiationSeries CalculateIrradiation(
            DateTimeOffset start,
            TimeSpan resolution,
            double globalHorizontal,
            double directNormal,
            double diffuseHorizontal) =>
            GlobalTiltedIrradiationSeries.Calculate(
                GlobalHorizontal(start, resolution, globalHorizontal),
                DirectNormal(start, resolution, directNormal),
                DiffuseHorizontal(start, resolution, diffuseHorizontal),
                Zenith(start, resolution));

        private static TraceSeries GlobalHorizontal(
            DateTimeOffset start, TimeSpan resolution, params double[] values) =>
            TraceSeries.GlobalHorizontalRadiation(start, resolution, values);

        private static TraceSeries DirectNormal(
            DateTimeOffset start, TimeSpan resolution, params double[] values) =>
            TraceSeries.DirectNormalRadiation(start, resolution, values);

        private static TraceSeries DiffuseHorizontal(
            DateTimeOffset start, TimeSpan resolution, params double[] values) =>
            TraceSeries.DiffuseHorizontalRadiation(start, resolution, values);

        private static TraceSeries DryBulb(
            DateTimeOffset start, TimeSpan resolution, params double[] values) =>
            TraceSeries.DryBulbTemperature(start, resolution, values);

        private static SolarZenithSeries Zenith(DateTimeOffset start, TimeSpan resolution) =>
            SolarZenithSeries.Calculate(
                start, resolution, 1, latitude: -33.8688, longitude: 151.2093);

        private static double Tolerance(double expected) =>
            Math.Max(Math.Abs(expected) * RelativeTolerance, double.Epsilon);
    }
}