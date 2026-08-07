using FluentAssertions;
using NEM.Model.Scenarios;
using NEM.Model.Units;

namespace NEM.Model.Tests.Scenarios;

public sealed class CostParametersTests
{
    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void Constructor_RejectsNegativeCosts(
        decimal capitalCost,
        decimal fixedOperatingCost,
        decimal variableOperatingCost,
        decimal fuelPrice)
    {
        var act = () => new GenerationCostParameters(
            PowerCapacityCost.FromAudPerMwCapacity(capitalCost),
            AnnualPowerCapacityCost.FromAudPerMwYear(fixedOperatingCost),
            GenerationEnergyCost.FromAudPerMwhGenerated(variableOperatingCost),
            FuelPrice.FromAudPerGjThermal(fuelPrice));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void StorageConstructor_RejectsNegativeCosts(
        decimal powerCapitalCost,
        decimal energyCapitalCost,
        decimal fixedOperatingCost)
    {
        var act = () => new StorageCostParameters(
            PowerCapacityCost.FromAudPerMwCapacity(powerCapitalCost),
            EnergyCapacityCost.FromAudPerMwhCapacity(energyCapitalCost),
            AnnualPowerCapacityCost.FromAudPerMwYear(fixedOperatingCost));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}