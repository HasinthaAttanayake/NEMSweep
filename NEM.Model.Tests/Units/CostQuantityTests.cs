using FluentAssertions;
using NEM.Model.Units;

namespace NEM.Model.Tests.Units;

public sealed class CostQuantityTests
{
    [Fact]
    public void RawValues_EncodeEachEconomicMeaning()
    {
        Money.FromAud(12.34m).Aud.Should().Be(12.34m);
        EnergyPrice.FromAudPerMwhDelivered(56.78m).AudPerMwhDelivered.Should().Be(56.78m);
        GenerationEnergyCost.FromAudPerMwhGenerated(4.5m).AudPerMwhGenerated.Should().Be(4.5m);
        PowerCapacityCost.FromAudPerMwCapacity(2.3m).AudPerMwCapacity.Should().Be(2.3m);
        EnergyCapacityCost.FromAudPerMwhCapacity(3.4m).AudPerMwhCapacity.Should().Be(3.4m);
        FuelPrice.FromAudPerGjThermal(9.01m).AudPerGjThermal.Should().Be(9.01m);
        AnnualPowerCapacityCost.FromAudPerMwYear(6.7m).AudPerMwYear.Should().Be(6.7m);
    }

    [Fact]
    public void Money_SupportsSignedArithmetic()
    {
        Money result = Money.FromAud(20m) - Money.FromAud(35m);

        result.Aud.Should().Be(-15m);
    }

    [Fact]
    public void EnergyPriceAndGenerationEnergyCost_AreNotSubstitutable()
    {
        typeof(EnergyPrice).IsAssignableFrom(typeof(GenerationEnergyCost)).Should().BeFalse();
        typeof(GenerationEnergyCost).IsAssignableFrom(typeof(EnergyPrice)).Should().BeFalse();
        typeof(EnergyPrice).GetInterfaces()
            .Intersect(typeof(GenerationEnergyCost).GetInterfaces())
            .Should().BeEmpty();
    }

    [Fact]
    public void PowerCapacityCost_ForPhysicalCapacity_ReturnsMoney()
    {
        Money powerCost = PowerCapacityCost.FromAudPerMwCapacity(1_500_000m)
            .For(Power.FromMegawatts(2));

        powerCost.Should().Be(Money.FromAud(3_000_000m));
    }

    [Fact]
    public void PowerCapacityCost_MultipliedByPowerCapacity_ReturnsMoney()
    {
        PowerCapacityCost cost = PowerCapacityCost.FromAudPerMwCapacity(1_500_000m);
        Power capacity = Power.FromMegawatts(2);

        (cost * capacity).Should().Be(Money.FromAud(3_000_000m));
        (capacity * cost).Should().Be(Money.FromAud(3_000_000m));
    }

    [Fact]
    public void EnergyCapacityCost_MultipliedByEnergyCapacity_ReturnsMoney()
    {
        EnergyCapacityCost cost = EnergyCapacityCost.FromAudPerMwhCapacity(400_000m);
        Energy capacity = Energy.FromMegawattHours(3);

        (cost * capacity).Should().Be(Money.FromAud(1_200_000m));
        (capacity * cost).Should().Be(Money.FromAud(1_200_000m));
    }

    [Fact]
    public void AnnualPowerCapacityCost_ForCapacityOverYears_ReturnsMoney()
    {
        AnnualPowerCapacityCost cost = AnnualPowerCapacityCost.FromAudPerMwYear(50_000m);

        Money annualCost = cost.For(Power.FromMegawatts(2), years: 3);

        annualCost.Should().Be(Money.FromAud(300_000m));
    }

    [Fact]
    public void FuelPrice_ForHeatRate_ReturnsGenerationEnergyCost()
    {
        GenerationEnergyCost fuelCost = FuelPrice.FromAudPerGjThermal(12m).ForHeatRate(
            HeatRate.FromGigajoulesPerMegawattHour(8));

        fuelCost.Should().Be(GenerationEnergyCost.FromAudPerMwhGenerated(96m));
    }

    [Fact]
    public void GenerationEnergyCost_MultipliedByGeneratedEnergy_ReturnsMoney()
    {
        GenerationEnergyCost cost = GenerationEnergyCost.FromAudPerMwhGenerated(25m);
        Energy generatedEnergy = Energy.FromMegawattHours(40);

        (cost * generatedEnergy).Should().Be(Money.FromAud(1_000m));
        (generatedEnergy * cost).Should().Be(Money.FromAud(1_000m));
    }

    [Fact]
    public void Money_PerDeliveredEnergy_ReturnsSlcoe()
    {
        EnergyPrice slcoe = Money.FromAud(1_250m).Per(Energy.FromMegawattHours(10));

        slcoe.Should().Be(EnergyPrice.FromAudPerMwhDelivered(125m));
    }

    [Fact]
    public void EnergyPrice_MultipliedByDeliveredEnergy_ReturnsMoney()
    {
        EnergyPrice price = EnergyPrice.FromAudPerMwhDelivered(125m);
        Energy deliveredEnergy = Energy.FromMegawattHours(10);

        (price * deliveredEnergy).Should().Be(Money.FromAud(1_250m));
        (deliveredEnergy * price).Should().Be(Money.FromAud(1_250m));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FuelPrice_ForHeatRate_RejectsNonFinitePhysics(double heatRate)
    {
        var act = () => HeatRate.FromGigajoulesPerMegawattHour(heatRate);

        act.Should().Throw<ArgumentException>().WithParameterName("gigajoulesPerMegawattHour");
    }

    [Fact]
    public void CapacityCost_RejectsNegativePhysicalCapacity()
    {
        var act = () => PowerCapacityCost.FromAudPerMwCapacity(1m)
            .For(Power.FromMegawatts(-1));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("capacity");
    }

    [Fact]
    public void CapacityCost_RejectsFinitePhysicalCapacityOutsideDecimalRange()
    {
        var act = () => PowerCapacityCost.FromAudPerMwCapacity(1m)
            .For(Power.FromMegawatts(double.MaxValue));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("capacity");
    }
}