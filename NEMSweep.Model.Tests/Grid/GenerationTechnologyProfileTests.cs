using AwesomeAssertions;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Tests.Grid;

public sealed class GenerationTechnologyProfileTests
{
    [Fact]
    public void Constructor_StoresHeatRateTechnicalLifeAndEmissionsIntensity()
    {
        var profile = new GenerationTechnologyProfile(
            HeatRate.FromGigajoulesPerMegawattHour(7.2),
            technicalLifeYears: 30u,
            GenerationEmissionsIntensity.FromTonnesCO2ePerMwhGenerated(0.37));

        profile.HeatRate.GigajoulesPerMegawattHour.Should().Be(7.2);
        profile.TechnicalLifeYears.Should().Be(30);
        profile.EmissionsIntensity.TonnesCO2ePerMwhGenerated.Should().Be(0.37);
    }

    [Fact]
    public void Constructor_RejectsZeroTechnicalLife()
    {
        var act = () => new GenerationTechnologyProfile(
            HeatRate.FromGigajoulesPerMegawattHour(0),
            technicalLifeYears: 0u,
            GenerationEmissionsIntensity.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("technicalLifeYears");
    }
}