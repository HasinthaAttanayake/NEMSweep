using AwesomeAssertions;
using NEM.Model.Units;

namespace NEM.Model.Tests.Units
{
    public class IrradiationTests
    {
        [Fact]
        public void DividedByInterval_ProducesAverageIrradiance()
        {
            Irradiation irradiation = Irradiation.FromWattHoursPerSquareMetre(500.0);

            Irradiance irradiance = irradiation / TimeSpan.FromMinutes(30);

            irradiance.WattsPerSquareMetre.Should().BeApproximately(1000.0, 1e-9);
        }
    }
}