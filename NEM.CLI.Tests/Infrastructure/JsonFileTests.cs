using AwesomeAssertions;
using NEM.CLI.Infrastructure;

namespace NEM.CLI.Tests.Infrastructure;

public sealed class JsonFileTests
{
    [Fact]
    public void Serialize_OrdersPropertiesAndRoundsArtifactUnits()
    {
        string json = JsonFile.Serialize(new
        {
            z = 1,
            nameplateCapacityMw = 123.456,
            energyCapacityMwh = 456.789,
            slcoeAudPerMwh = 12.345,
            renewableShare = 0.123456,
        });

        json.Should().Contain("\"energyCapacityMwh\": 456.8");
        json.Should().Contain("\"nameplateCapacityMw\": 123.5");
        json.Should().Contain("\"renewableShare\": 0.1235");
        json.Should().Contain("\"slcoeAudPerMwh\": 12.35");
        json.IndexOf("energyCapacityMwh", StringComparison.Ordinal).Should()
            .BeLessThan(json.IndexOf("nameplateCapacityMw", StringComparison.Ordinal));
    }

    [Fact]
    public void SerializeExact_PreservesCloseDistinctValues()
    {
        string first = JsonFile.SerializeExact(new { realDiscountRate = 0.070001d });
        string second = JsonFile.SerializeExact(new { realDiscountRate = 0.070002d });

        first.Should().NotBe(second);
    }
}