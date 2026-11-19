using AwesomeAssertions;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Tests.Grid;

public sealed class GenerationTechnologyClassificationTests
{
    // Every technology is listed with its expected answer, so adding one to the enum fails
    // EveryTechnology_HasAnExpectedIntermittencyClassification until someone decides it.
    public static TheoryData<GenerationTechnology, bool> ExpectedIntermittency => new()
    {
        { GenerationTechnology.Solar, true },
        { GenerationTechnology.Wind, true },
        { GenerationTechnology.Hydro, false },
        { GenerationTechnology.Nuclear, false },
        { GenerationTechnology.Coal, false },
        { GenerationTechnology.Gas, false },
    };

    [Theory]
    [MemberData(nameof(ExpectedIntermittency))]
    public void IsIntermittentRenewable_IsTrueOnlyForWeatherDrivenTechnologies(
        GenerationTechnology technology,
        bool expected)
    {
        technology.IsIntermittentRenewable().Should().Be(expected);
    }

    [Fact]
    public void EveryTechnology_HasAnExpectedIntermittencyClassification()
    {
        ExpectedIntermittency.Select(row => (GenerationTechnology)row[0])
            .Should().BeEquivalentTo(Enum.GetValues<GenerationTechnology>());
    }

    [Fact]
    public void Hydro_IsRenewableButNotIntermittent_BecauseItIsDispatchedWithinABudget()
    {
        GenerationTechnology.Hydro.IsIntermittentRenewable().Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(ExpectedIntermittency))]
    public void GeneratingFleet_IsIntermittentRenewable_AgreesWithItsTechnology(
        GenerationTechnology technology,
        bool expected)
    {
        IReadOnlyDictionary<DateOnly, double>? monthlyCapacityFactors =
            technology == GenerationTechnology.Hydro
                ? new Dictionary<DateOnly, double> { [new DateOnly(2026, 7, 1)] = 0.5 }
                : null;
        var fleet = new GeneratingFleet(technology, Power.FromMegawatts(100), monthlyCapacityFactors);

        fleet.IsIntermittentRenewable.Should().Be(expected);
    }
}
