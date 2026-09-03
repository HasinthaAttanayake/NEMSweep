using AwesomeAssertions;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Tests.Units;

public sealed class GenerationEmissionsIntensityTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.001)]
    public void FromTonnesPerMwhGenerated_RejectsNonFiniteOrNegative(double intensity)
    {
        var act = () => GenerationEmissionsIntensity.FromTonnesCO2ePerMwhGenerated(intensity);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("tonnesCO2ePerMwhGenerated");
    }

    [Fact]
    public void For_MultipliesGeneratedEnergyByTheIntensity()
    {
        Emissions emissions = GenerationEmissionsIntensity
            .FromTonnesCO2ePerMwhGenerated(0.771)
            .For(Energy.FromMegawattHours(1_000));

        emissions.TonnesCO2e.Should().BeApproximately(771, 1e-9);
    }

    [Fact]
    public void For_OfAZeroIntensityFleetEmitsNothing()
    {
        Emissions emissions = GenerationEmissionsIntensity.Zero
            .For(Energy.FromMegawattHours(50_000));

        emissions.Should().Be(Emissions.Zero);
    }

    [Fact]
    public void For_RejectsNegativeEnergy()
    {
        var act = () => GenerationEmissionsIntensity.Zero.For(Energy.FromMegawattHours(-1));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("energy");
    }

    /// <summary>
    /// A rate per MWh generated and a rate per MWh served share a unit name but not a denominator,
    /// so the compiler, not a comment, is what keeps an assumption out of a result's place. This
    /// test fails to compile rather than fails at run time if that separation is ever collapsed.
    /// </summary>
    [Fact]
    public void IsNotInterchangeableWithAServedIntensity()
    {
        typeof(GenerationEmissionsIntensity).Should().NotBe(typeof(ServedEmissionsIntensity));
        typeof(GenerationEmissionsIntensity).Should()
            .NotBeAssignableTo<ServedEmissionsIntensity>();
    }
}
