using AwesomeAssertions;
using NEM.Model.Units;

namespace NEM.Model.Tests.Units
{
    public class IrradianceTests
    {
        [Fact]
        public void DividedByIrradiance_ProducesDimensionlessRatio()
        {
            Irradiance actual = Irradiance.FromWattsPerSquareMetre(750.0);
            Irradiance reference = Irradiance.FromWattsPerSquareMetre(1000.0);

            double ratio = actual / reference;

            ratio.Should().BeApproximately(0.75, 1e-9);
        }
    }
}