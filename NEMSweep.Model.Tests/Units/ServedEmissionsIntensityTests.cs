using AwesomeAssertions;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Tests.Units;

public sealed class ServedEmissionsIntensityTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.001)]
    public void FromTonnesPerMwhServed_RejectsNonFiniteOrNegative(double intensity)
    {
        var act = () => ServedEmissionsIntensity.FromTonnesCO2ePerMwhServed(intensity);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("tonnesCO2ePerMwhServed");
    }

    [Fact]
    public void Addition_SumsSharesOfOneSystemsIntensity()
    {
        ServedEmissionsIntensity total =
            ServedEmissionsIntensity.FromTonnesCO2ePerMwhServed(0.386)
            + ServedEmissionsIntensity.FromTonnesCO2ePerMwhServed(0.058);

        total.TonnesCO2ePerMwhServed.Should().BeApproximately(0.444, 1e-12);
    }
}
