using AwesomeAssertions;
using NEM.Model.Grid;
using NEM.Model.Units;

namespace NEM.Model.Tests.Grid;

public sealed class InterconnectorTests
{
    [Fact]
    public void Construction_RetainsDirectedCapacity()
    {
        var interconnector = new Interconnector(
            "NSW1",
            "VIC1",
            Power.FromMegawatts(700));

        interconnector.FromRegionId.Should().Be("NSW1");
        interconnector.ToRegionId.Should().Be("VIC1");
        interconnector.Capacity.Should().Be(Power.FromMegawatts(700));
    }

    [Fact]
    public void Construction_RejectsSelfConnection()
    {
        var act = () => new Interconnector("NSW1", "nsw1", Power.Zero);

        act.Should().Throw<ArgumentException>().WithParameterName("toRegionId");
    }

    [Fact]
    public void Construction_RejectsNegativeCapacity()
    {
        var act = () => new Interconnector(
            "NSW1",
            "VIC1",
            Power.FromMegawatts(-1));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("capacity");
    }

    [Fact]
    public void Construction_RejectsBlankRegionId()
    {
        var act = () => new Interconnector(" ", "VIC1", Power.Zero);

        act.Should().Throw<ArgumentException>().WithParameterName("fromRegionId");
    }

    [Fact]
    public void Construction_AllowsZeroCapacity()
    {
        var act = () => new Interconnector("NSW1", "VIC1", Power.Zero);

        act.Should().NotThrow("a zero-capacity link is the documented way to disable transfer");
    }
}
