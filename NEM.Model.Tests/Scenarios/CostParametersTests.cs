using FluentAssertions;
using NEM.Model.Scenarios;
using NEM.Model.Units;

namespace NEM.Model.Tests.Scenarios;

public sealed class CostParametersTests
{
    [Theory]
    [InlineData(-1, 0, 0, 0, 0)]
    [InlineData(0, -1, 0, 0, 0)]
    [InlineData(0, 0, -1, 0, 0)]
    [InlineData(0, 0, 0, -1, 0)]
    [InlineData(0, 0, 0, 0, -1)]
    public void Constructor_RejectsNegativeCosts(
        decimal capitalCost,
        decimal energyCapitalCost,
        decimal fixedOperatingCost,
        decimal variableOperatingCost,
        decimal fuelPrice)
    {
        var act = () => new CostParameters(
            PowerCapacityCost.FromAudPerMwCapacity(capitalCost),
            EnergyCapacityCost.FromAudPerMwhStorage(energyCapitalCost),
            AnnualPowerCapacityCost.FromAudPerMwYear(fixedOperatingCost),
            EnergyPrice.FromAudPerMwhDelivered(variableOperatingCost),
            FuelPrice.FromAudPerGjThermal(fuelPrice));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}