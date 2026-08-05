using FluentAssertions;
using NEM.Model.Units;
using EconomicsCalculator = NEM.Model.Economics.Economics;

namespace NEM.Model.Tests.Economics;

public sealed class EconomicsTests
{
    public static TheoryData<decimal, decimal, uint, decimal> AnnuityReferenceCases => new()
    {
        { 1_000_000m, 10m, 20u, 117_459.624772545m },
        { 1_000_000m, 10m, 30u, 106_079.248252543m },
    };

    [Theory]
    [MemberData(nameof(AnnuityReferenceCases))]
    public void Annuitise_MatchesSpreadsheetCapitalRecoveryFactorForDifferentAssetLives(
        decimal capex,
        decimal discountRatePercent,
        uint assetLifeYears,
        decimal expectedAnnualCost)
    {
        Money annualCost = EconomicsCalculator.Annuitise(
            Money.FromAud(capex),
            discountRatePercent / 100m,
            assetLifeYears);

        annualCost.Aud.Should().BeApproximately(expectedAnnualCost, 0.000001m);
    }

    [Fact]
    public void LevelisedCostOfElectricity_CombinesAnnualisedCapitalAndAnnualOperatingCosts()
    {
        EnergyPrice cost = EconomicsCalculator.LevelisedCostOfElectricity(
            Money.FromAud(2_000_000m),
            Money.FromAud(50_000m),
            Money.FromAud(25_000m),
            Energy.FromMegawattHours(10_000),
            discountRate: 0.05m,
            assetLifetime: 20);

        cost.AudPerMwhDelivered.Should().BeApproximately(23.548517977385m, 0.000001m);
    }

    [Fact]
    public void Annuitise_AtZeroDiscountRate_SpreadsCapitalEvenlyOverAssetLife()
    {
        Money annualCost = EconomicsCalculator.Annuitise(Money.FromAud(1_000_000m), 0m, 20);

        annualCost.Should().Be(Money.FromAud(50_000m));
    }

    [Fact]
    public void CapitalRecoveryFactor_RejectsZeroAssetLife()
    {
        var act = () => EconomicsCalculator.CapitalRecoveryFactor(0.05m, 0);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("years");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LevelisedCostOfElectricity_RejectsNonPositiveAnnualGeneration(double annualGenerationMwh)
    {
        var act = () => EconomicsCalculator.LevelisedCostOfElectricity(
            Money.FromAud(1_000_000m),
            Money.Zero,
            Money.Zero,
            Energy.FromMegawattHours(annualGenerationMwh),
            discountRate: 0.05m,
            assetLifetime: 20);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("annualGeneration");
    }
}