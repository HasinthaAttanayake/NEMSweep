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
        EnergyCapacityCost.FromAudPerMwhStorage(4.5m).AudPerMwhStorage.Should().Be(4.5m);
        PowerCapacityCost.FromAudPerMwCapacity(2.3m).AudPerMwCapacity.Should().Be(2.3m);
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
    public void EnergyPriceAndEnergyCapacityCost_AreNotSubstitutable()
    {
        typeof(EnergyPrice).IsAssignableFrom(typeof(EnergyCapacityCost)).Should().BeFalse();
        typeof(EnergyCapacityCost).IsAssignableFrom(typeof(EnergyPrice)).Should().BeFalse();
        typeof(EnergyPrice).GetInterfaces()
            .Intersect(typeof(EnergyCapacityCost).GetInterfaces())
            .Should().BeEmpty();
    }

    [Fact]
    public void CapacityCosts_ForPhysicalCapacity_ReturnMoney()
    {
        Money powerCost = PowerCapacityCost.FromAudPerMwCapacity(1_500_000m)
            .For(Power.FromMegawatts(2));
        Money storageCost = EnergyCapacityCost.FromAudPerMwhStorage(300_000m)
            .For(Energy.FromMegawattHours(4));

        powerCost.Should().Be(Money.FromAud(3_000_000m));
        storageCost.Should().Be(Money.FromAud(1_200_000m));
    }

    [Fact]
    public void FuelPrice_ForHeatRate_ReturnsDeliveredEnergyPrice()
    {
        EnergyPrice fuelCost = FuelPrice.FromAudPerGjThermal(12m).ForHeatRate(8);

        fuelCost.Should().Be(EnergyPrice.FromAudPerMwhDelivered(96m));
    }

    [Fact]
    public void Money_PerDeliveredEnergy_ReturnsSlcoe()
    {
        EnergyPrice slcoe = Money.FromAud(1_250m).Per(Energy.FromMegawattHours(10));

        slcoe.Should().Be(EnergyPrice.FromAudPerMwhDelivered(125m));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FuelPrice_ForHeatRate_RejectsNonFinitePhysics(double heatRate)
    {
        var act = () => FuelPrice.FromAudPerGjThermal(12m).ForHeatRate(heatRate);

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