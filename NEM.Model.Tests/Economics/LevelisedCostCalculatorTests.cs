using AwesomeAssertions;
using NEM.Model.Economics;
using NEM.Model.Units;

namespace NEM.Model.Tests.Economics;

public sealed class LevelisedCostCalculatorTests
{
    [Theory]
    [InlineData(10, 20, 117_459.624772545)]
    [InlineData(10, 30, 106_079.248252543)]
    public void Annuitise_MatchesReferenceCases(
        decimal discountRatePercent,
        uint assetLifeYears,
        decimal expectedAnnualCost)
    {
        Money annualCost = LevelisedCostCalculator.Annuitise(
            Money.FromAud(1_000_000m),
            discountRatePercent / 100m,
            assetLifeYears);

        annualCost.Aud.Should().BeApproximately(expectedAnnualCost, 0.000001m);
    }

    [Fact]
    public void Annuitise_AtZeroDiscountRate_SpreadsCapitalAcrossAssetLife()
    {
        LevelisedCostCalculator.Annuitise(Money.FromAud(1_000_000m), 0m, 20)
            .Should().Be(Money.FromAud(50_000m));
    }

    [Fact]
    public void CapitalRecoveryFactor_RejectsZeroAssetLife()
    {
        var act = () => LevelisedCostCalculator.CapitalRecoveryFactor(0.05m, 0);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("years");
    }
}