using AwesomeAssertions;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Tests.Grid;

public sealed class GenerationTechnologyProfileTests
{
    [Fact]
    public void Constructor_StoresHeatRateAndTechnicalLife()
    {
        var profile = new GenerationTechnologyProfile(
            HeatRate.FromGigajoulesPerMegawattHour(7.2),
            technicalLifeYears: 30u);

        profile.HeatRate.GigajoulesPerMegawattHour.Should().Be(7.2);
        profile.TechnicalLifeYears.Should().Be(30);
    }

    [Fact]
    public void Constructor_RejectsZeroTechnicalLife()
    {
        var act = () => new GenerationTechnologyProfile(
            HeatRate.FromGigajoulesPerMegawattHour(0),
            technicalLifeYears: 0u);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("technicalLifeYears");
    }
}