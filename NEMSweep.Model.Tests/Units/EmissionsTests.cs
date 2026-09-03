using AwesomeAssertions;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Tests.Units;

public sealed class EmissionsTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    public void FromTonnes_RejectsNonFiniteOrNegative(double tonnes)
    {
        var act = () => Emissions.FromTonnesCO2e(tonnes);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("tonnesCO2e");
    }

    [Fact]
    public void Addition_SumsAcrossTechnologiesAndRegions()
    {
        Emissions total = Emissions.FromTonnesCO2e(120)
            + Emissions.FromTonnesCO2e(80);

        total.TonnesCO2e.Should().Be(200);
    }

    [Fact]
    public void Per_EnergyServed_GivesTheIntensityOfTheLoadItServed()
    {
        ServedEmissionsIntensity intensity = Emissions.FromTonnesCO2e(500)
            .Per(Energy.FromMegawattHours(1_000));

        intensity.TonnesCO2ePerMwhServed.Should().Be(0.5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Per_NonPositiveEnergyServed_Throws(double megawattHours)
    {
        var act = () => Emissions.FromTonnesCO2e(1).Per(Energy.FromMegawattHours(megawattHours));

        act.Should().Throw<DivideByZeroException>();
    }
}
