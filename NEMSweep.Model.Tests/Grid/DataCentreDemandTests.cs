using AwesomeAssertions;
using NEMSweep.Model.Grid;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Tests.Grid;

public sealed class DataCentreDemandTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    public void Expand_ProducesFlatFullNameplateDemandForWholeYear()
    {
        var demand = DataCentreDemand.Expand(
            Power.FromMegawatts(1_000),
            Start,
            TimeSpan.FromHours(1),
            8_760);

        demand.Length.Should().Be(8_760);
        for (int index = 0; index < demand.Length; index++)
        {
            demand[index].Should().Be(Power.FromMegawatts(1_000));
        }

        demand.Integrate().MegawattHours.Should().Be(8_760_000);
    }

    [Fact]
    public void Expand_ZeroNameplateProducesAlignedZeroDemand()
    {
        var demand = DataCentreDemand.Expand(
            Power.Zero,
            Start,
            TimeSpan.FromHours(1),
            2);

        demand.Length.Should().Be(2);
        for (int index = 0; index < demand.Length; index++)
        {
            demand[index].Should().Be(Power.Zero);
        }
    }

    [Fact]
    public void Expand_RejectsNegativeNameplate()
    {
        Action act = () => DataCentreDemand.Expand(
            Power.FromMegawatts(-1),
            Start,
            TimeSpan.FromHours(1),
            1);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("nameplate");
    }
}