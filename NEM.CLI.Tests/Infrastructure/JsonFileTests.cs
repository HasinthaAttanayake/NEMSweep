using System.Text.Json.Nodes;
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

        json.Should().Contain("\"energyCapacityMwh\":456.8");
        json.Should().Contain("\"nameplateCapacityMw\":123.5");
        json.Should().Contain("\"renewableShare\":0.1235");
        json.Should().Contain("\"slcoeAudPerMwh\":12.35");
        json.IndexOf("energyCapacityMwh", StringComparison.Ordinal).Should()
            .BeLessThan(json.IndexOf("nameplateCapacityMw", StringComparison.Ordinal));
    }

    /// <summary>
    /// A published artifact carries no whitespace. It is fetched rather than read, and indenting it
    /// costs about seventy percent of its bytes and puts every value on a line of its own.
    /// </summary>
    [Fact]
    public void Serialize_WritesAPublishedArtifactWithoutIndentation()
    {
        string json = JsonFile.Serialize(new { alpha = 1, beta = new { gamma = 2 } });

        json.Should().Be("{\"alpha\":1,\"beta\":{\"gamma\":2}}");
    }

    /// <summary>Rounding and ordering are the artifact's; only the layout differs.</summary>
    [Fact]
    public void SerializeReadable_LaysOutTheSameCanonicalJson()
    {
        var value = new { nameplateCapacityMw = 123.456, alpha = 1 };

        string readable = JsonFile.SerializeReadable(value);

        readable.Should().Contain(Environment.NewLine);
        readable.Should().Contain("\"nameplateCapacityMw\": 123.5");
        JsonNode.Parse(readable)!.ToJsonString().Should()
            .Be(JsonNode.Parse(JsonFile.Serialize(value))!.ToJsonString());
    }

    /// <summary>The scenario config a sweep writes for each point is read by people.</summary>
    [Fact]
    public void SerializeExact_StaysLaidOut()
    {
        JsonFile.SerializeExact(new { alpha = 1 }).Should().Contain(Environment.NewLine);
    }

    [Fact]
    public void SerializeExact_PreservesCloseDistinctValues()
    {
        string first = JsonFile.SerializeExact(new { realDiscountRate = 0.070001d });
        string second = JsonFile.SerializeExact(new { realDiscountRate = 0.070002d });

        first.Should().NotBe(second);
    }
}