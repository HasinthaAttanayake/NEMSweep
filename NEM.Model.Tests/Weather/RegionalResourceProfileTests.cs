using FluentAssertions;
using NEM.Model.Weather;

namespace NEM.Model.Tests.Weather
{
    public sealed class RegionalResourceProfileTests
    {
        private static readonly DateTimeOffset Start =
            new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

        [Fact]
        public void Construction_RejectsMisalignedTraces()
        {
            var act = () => new RegionalResourceProfile(
                TraceSeries.GlobalHorizontalRadiation(Start, TimeSpan.FromHours(1), [0]),
                TraceSeries.DirectNormalRadiation(Start.AddHours(1), TimeSpan.FromHours(1), [0]),
                TraceSeries.DiffuseHorizontalRadiation(Start, TimeSpan.FromHours(1), [0]),
                SolarZenithSeries.Calculate(Start, TimeSpan.FromHours(1), 1, -33.8688, 151.2093),
                TraceSeries.DryBulbTemperature(Start, TimeSpan.FromHours(1), [20]),
                TraceSeries.WindSpeed(Start, TimeSpan.FromHours(1), [5], 10));

            act.Should().Throw<ArgumentException>().WithMessage("*misaligned on start*");
        }

        [Fact]
        public void Construction_RejectsTraceWithWrongResourceUnit()
        {
            TraceSeries directNormal = TraceSeries.DirectNormalRadiation(
                Start,
                TimeSpan.FromHours(1),
                [0]);

            var act = () => new RegionalResourceProfile(
                directNormal,
                directNormal,
                TraceSeries.DiffuseHorizontalRadiation(Start, TimeSpan.FromHours(1), [0]),
                SolarZenithSeries.Calculate(Start, TimeSpan.FromHours(1), 1, -33.8688, 151.2093),
                TraceSeries.DryBulbTemperature(Start, TimeSpan.FromHours(1), [20]),
                TraceSeries.WindSpeed(Start, TimeSpan.FromHours(1), [5], 10));

            act.Should().Throw<ArgumentException>()
                .WithParameterName("globalHorizontalRadiation");
        }
    }
}