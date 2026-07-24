using FluentAssertions;
using NEM.Model.Generation.Wind;
using NEM.Model.Series;
using NEM.Model.Units;
using NEM.Model.Weather;

namespace NEM.Model.Tests.Generation.Wind
{
    public class WindPowerCurveTests
    {
        private static readonly DateTimeOffset Start =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(10));

        [Fact]
        public void Calculate_InterpolatesReferenceCurveAndScalesInstalledCapacity()
        {
            TraceSeries wind = WindAtHubHeight(6.25);

            FlowSeries result = WindPowerCurve.Calculate(
                wind,
                Power.FromMegawatts(34.0));

            result[0].Megawatts.Should().BeApproximately(6.55, 1e-12);
        }

        [Fact]
        public void ReferenceTurbineCapacity_IdentifiesBrochureCurveUsingTypedPower()
        {
            WindPowerCurve.ReferenceTurbineCapacity.Should().Be(Power.FromMegawatts(3.4));
        }

        [Fact]
        public void Calculate_EnforcesCutInRatedPlateauAndCutOutBoundaries()
        {
            TraceSeries wind = WindAtHubHeight(2.49, 2.5, 11.0, 20.0, 20.01);

            FlowSeries result = WindPowerCurve.Calculate(
                wind,
                Power.FromMegawatts(3.4));

            result[0].Megawatts.Should().Be(0.0);
            result[1].Megawatts.Should().Be(0.0);
            result[2].Megawatts.Should().Be(3.4);
            result[3].Megawatts.Should().Be(3.4);
            result[4].Megawatts.Should().Be(0.0);
        }

        [Fact]
        public void Calculate_ExtendsRatedOutputToSiteSpecificCutOut()
        {
            TraceSeries wind = WindAtHubHeight(20.01, 25.0, 25.01);

            FlowSeries result = WindPowerCurve.Calculate(
                wind,
                Power.FromMegawatts(3.4),
                new WindPowerCurveSettings
                {
                    CutOutWindSpeedMetresPerSecond = 25.0,
                });

            result[0].Megawatts.Should().Be(3.4);
            result[1].Megawatts.Should().Be(3.4);
            result[2].Megawatts.Should().Be(0.0);
        }

        [Fact]
        public void CorrectToHubHeight_AppliesPowerLawAndPreservesTimeline()
        {
            TraceSeries measured = TraceSeries.WindSpeed(
                Start,
                TimeSpan.FromMinutes(30),
                [5.0, 10.0],
                measurementHeightMetres: 10.0);

            TraceSeries corrected = WindPowerCurve.CorrectToHubHeight(
                measured,
                new WindPowerCurveSettings
                {
                    HubHeightMetres = 120.0,
                    ShearExponent = 0.2,
                });

            double factor = Math.Pow(120.0 / 10.0, 0.2);
            corrected[0].Should().BeApproximately(5.0 * factor, 1e-12);
            corrected[1].Should().BeApproximately(10.0 * factor, 1e-12);
            corrected.MeasurementHeightMetres.Should().Be(120.0);
            corrected.Start.Should().Be(Start);
            corrected.Resolution.Should().Be(TimeSpan.FromMinutes(30));
            corrected.Length.Should().Be(2);
        }

        [Fact]
        public void Calculate_CorrectsMeasuredWindToHubHeightBeforeInterpolation()
        {
            double heightFactor = Math.Pow(120.0 / 10.0, 0.2);
            TraceSeries measured = TraceSeries.WindSpeed(
                Start,
                TimeSpan.FromHours(1),
                [6.25 / heightFactor],
                measurementHeightMetres: 10.0);

            FlowSeries result = WindPowerCurve.Calculate(
                measured,
                Power.FromMegawatts(34.0));

            result[0].Megawatts.Should().BeApproximately(6.55, 1e-12);
        }

        [Fact]
        public void Calculate_ProducesOutputBetweenZeroAndInstalledCapacity()
        {
            TraceSeries wind = WindAtHubHeight(
                0.0, 2.5, 3.0, 6.0, 8.5, 11.0, 15.0, 20.0, 20.1);
            Power installedCapacity = Power.FromMegawatts(17.0);

            FlowSeries result = WindPowerCurve.Calculate(
                wind,
                installedCapacity);

            for (int index = 0; index < result.Length; index++)
            {
                result[index].Should().BeGreaterThanOrEqualTo(Power.Zero);
                result[index].Should().BeLessThanOrEqualTo(installedCapacity);
            }
        }

        [Fact]
        public void Calculate_RejectsNonWindTrace()
        {
            TraceSeries radiation = TraceSeries.DirectNormalRadiation(
                Start, TimeSpan.FromHours(1), [100.0]);

            var act = () => WindPowerCurve.Calculate(
                radiation,
                Power.FromMegawatts(3.4));

            act.Should().Throw<ArgumentException>()
                .Which.ParamName.Should().Be("windSpeed");
        }

        [Theory]
        [InlineData(0.0, 0.2, "hubHeightMetres")]
        [InlineData(120.0, -0.1, "shearExponent")]
        public void CorrectToHubHeight_RejectsInvalidParameters(
            double hubHeightMetres,
            double shearExponent,
            string parameterName)
        {
            var act = () => WindPowerCurve.CorrectToHubHeight(
                WindAtHubHeight(5.0),
                new WindPowerCurveSettings
                {
                    HubHeightMetres = hubHeightMetres,
                    ShearExponent = shearExponent,
                });

            act.Should().Throw<ArgumentOutOfRangeException>()
                .Which.ParamName.Should().Be(parameterName);
        }

        [Fact]
        public void Calculate_RejectsNegativeInstalledCapacity()
        {
            var act = () => WindPowerCurve.Calculate(
                WindAtHubHeight(5.0),
                Power.FromMegawatts(-1.0));

            act.Should().Throw<ArgumentOutOfRangeException>()
                .Which.ParamName.Should().Be("installedCapacity");
        }

        [Fact]
        public void Calculate_ReturnsZeroForZeroInstalledCapacity()
        {
            FlowSeries result = WindPowerCurve.Calculate(
                WindAtHubHeight(3.0, 8.0, 11.0, 20.0),
                Power.Zero);

            for (int index = 0; index < result.Length; index++)
            {
                result[index].Should().Be(Power.Zero);
            }
        }

        [Fact]
        public void Calculate_RejectsCutOutBelowBrochureMinimum()
        {
            var act = () => WindPowerCurve.Calculate(
                WindAtHubHeight(5.0),
                Power.FromMegawatts(3.4),
                new WindPowerCurveSettings
                {
                    CutOutWindSpeedMetresPerSecond = 19.9,
                });

            act.Should().Throw<ArgumentOutOfRangeException>()
                .Which.ParamName.Should().Be("CutOutWindSpeedMetresPerSecond");
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void Calculate_RejectsNonFiniteCutOut(double cutOutWindSpeedMetresPerSecond)
        {
            var act = () => WindPowerCurve.Calculate(
                WindAtHubHeight(5.0),
                Power.FromMegawatts(3.4),
                new WindPowerCurveSettings
                {
                    CutOutWindSpeedMetresPerSecond = cutOutWindSpeedMetresPerSecond,
                });

            act.Should().Throw<ArgumentOutOfRangeException>()
                .Which.ParamName.Should().Be("CutOutWindSpeedMetresPerSecond");
        }

        private static TraceSeries WindAtHubHeight(params double[] values) =>
            TraceSeries.WindSpeed(
                Start,
                TimeSpan.FromHours(1),
                values,
                WindPowerCurve.DefaultHubHeightMetres);
    }
}