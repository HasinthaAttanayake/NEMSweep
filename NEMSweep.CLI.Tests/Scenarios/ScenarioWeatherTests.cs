using AwesomeAssertions;
using NEMSweep.CLI.Scenarios;
using NEMSweep.CLI.Weather;
using NEMSweep.Contracts;
using NEMSweep.Model.Series;

namespace NEMSweep.CLI.Tests.Scenarios;

public sealed class ScenarioWeatherTests
{
    [Fact]
    public void ReadWeatherForTimeline_RejectsTruncatedSolarZenith()
    {
        WeatherDataDTO weather = Weather(8760, 8759);
        FlowSeries timeline = Timeline(EpwParser.SyntheticNonLeapStart, 8760);

        var act = () => ScenarioRunner.ReadWeatherForTimeline(weather, timeline);

        act.Should().Throw<FormatException>().Which.Message
            .Should().Contain("empty or misaligned");
    }

    [Fact]
    public void ReadWeatherForTimeline_NamesMissingLeapDay()
    {
        WeatherDataDTO weather = Weather(8760, 8760);
        FlowSeries timeline = Timeline(
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.FromHours(10)),
            8784);

        var act = () => ScenarioRunner.ReadWeatherForTimeline(weather, timeline);

        ScenarioRunException exception = act.Should().Throw<ScenarioRunException>().Which;
        exception.Stage.Should().Be(SweepFailureStage.Input);
        exception.Code.Should().Be("weatherMissingLeapDay");
        exception.Message.Should().Contain("29 February");
    }

    private static WeatherDataDTO Weather(int length, int zenithLength)
    {
        double[] zeroes = new double[length];
        return new WeatherDataDTO(
            6,
            "NSW1",
            EpwParser.SyntheticNonLeapStart,
            TimeSpan.FromHours(1),
            new SolarWeatherData(
                "solar.epw",
                new WeatherLocation("Test", "00000", -33.9, 151.2),
                zeroes,
                zeroes,
                zeroes,
                new double[zenithLength],
                Enumerable.Repeat(20d, length).ToArray(),
                zeroes),
            new WindWeatherData(
                "wind.epw",
                new WeatherLocation("Test", "00000", -33.9, 151.2),
                Enumerable.Repeat(5d, length).ToArray(),
                10,
                zeroes));
    }

    private static FlowSeries Timeline(DateTimeOffset start, int length) => new(
        start,
        TimeSpan.FromHours(1),
        new double[length]);
}