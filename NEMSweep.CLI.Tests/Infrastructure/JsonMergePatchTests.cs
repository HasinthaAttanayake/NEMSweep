using System.Text.Json.Nodes;
using AwesomeAssertions;
using NEMSweep.CLI.Infrastructure;

namespace NEMSweep.CLI.Tests.Infrastructure;

public sealed class JsonMergePatchTests
{
    [Fact]
    public void Apply_MergesObjectsRecursively()
    {
        JsonNode result = JsonMergePatch.Apply(
            Parse("""{ "a": { "b": 1, "c": 2 } }"""),
            Parse("""{ "a": { "b": 3, "d": 4 } }"""));

        result.ToJsonString().Should().Be("{\"a\":{\"b\":3,\"c\":2,\"d\":4}}");
    }

    [Fact]
    public void Apply_ReplacesScalarsAndArrays()
    {
        JsonNode result = JsonMergePatch.Apply(
            Parse("""{ "value": 1, "items": [1, 2] }"""),
            Parse("""{ "value": "next", "items": [3] }"""));

        result.ToJsonString().Should().Be("{\"value\":\"next\",\"items\":[3]}");
    }

    [Fact]
    public void Apply_MergesKeyedRegionsAndAppendsUnmatchedItems()
    {
        JsonNode result = JsonMergePatch.Apply(
            Parse("""{ "regions": [{ "regionId": "NSW1", "name": "New South Wales" }, { "regionId": "VIC1", "name": "Victoria" }] }"""),
            Parse("""{ "regions": [{ "regionId": "VIC1", "name": "Updated Victoria" }, { "regionId": "QLD1", "name": "Queensland" }] }"""));

        result.ToJsonString().Should().Be("{\"regions\":[{\"regionId\":\"NSW1\",\"name\":\"New South Wales\"},{\"regionId\":\"VIC1\",\"name\":\"Updated Victoria\"},{\"regionId\":\"QLD1\",\"name\":\"Queensland\"}]}");
    }

    [Fact]
    public void Apply_RemovesKeyedItemsAndMergesNestedFleetsAndMonthlyValues()
    {
        JsonNode result = JsonMergePatch.Apply(
                        Parse("""
                        {
              "regions": [{ "regionId": "NSW1", "generatingFleets": [{ "technology": "Hydro", "capacity": 1, "monthlyCapacityFactors": [{ "month": "2026-01-01", "value": 1 }, { "month": "2026-02-01", "value": 2 }] }], "storageFleets": [{ "technology": "Battery", "capacity": 1 }, { "technology": "PumpedHydro", "capacity": 2 }] }]
            }
            """),
                        Parse("""
                        {
              "regions": [{ "regionId": "NSW1", "generatingFleets": [{ "technology": "Hydro", "capacity": 3, "monthlyCapacityFactors": [{ "month": "2026-02-01", "value": 4 }, { "month": "2026-03-01", "value": 5 }] }], "storageFleets": [{ "technology": "Battery", "$remove": true }, { "technology": "Flow", "capacity": 6 }] }]
            }
            """));

        result.ToJsonString().Should().Be("{\"regions\":[{\"regionId\":\"NSW1\",\"generatingFleets\":[{\"technology\":\"Hydro\",\"capacity\":3,\"monthlyCapacityFactors\":[{\"month\":\"2026-01-01\",\"value\":1},{\"month\":\"2026-02-01\",\"value\":4},{\"month\":\"2026-03-01\",\"value\":5}]}],\"storageFleets\":[{\"technology\":\"PumpedHydro\",\"capacity\":2},{\"technology\":\"Flow\",\"capacity\":6}]}]}");
    }

    [Fact]
    public void Apply_MergesInterconnectorsByEndpointPairAndAppendsNewLinks()
    {
        JsonNode result = JsonMergePatch.Apply(
            Parse("""{ "interconnectors": [{ "fromRegionId": "NSW1", "toRegionId": "QLD1", "capacityMw": 957 }, { "fromRegionId": "QLD1", "toRegionId": "NSW1", "capacityMw": 1610 }] }"""),
            Parse("""{ "interconnectors": [{ "fromRegionId": "NSW1", "toRegionId": "QLD1", "capacityMw": 2000 }, { "fromRegionId": "VIC1", "toRegionId": "NSW1", "capacityMw": 1700, "routeLengthKm": 300 }] }"""));

        result.ToJsonString().Should().Be("{\"interconnectors\":[{\"fromRegionId\":\"NSW1\",\"toRegionId\":\"QLD1\",\"capacityMw\":2000},{\"fromRegionId\":\"QLD1\",\"toRegionId\":\"NSW1\",\"capacityMw\":1610},{\"fromRegionId\":\"VIC1\",\"toRegionId\":\"NSW1\",\"capacityMw\":1700,\"routeLengthKm\":300}]}");
    }

    [Fact]
    public void Apply_RemovesAnInterconnectorByEndpointPairWithoutTouchingItsReverse()
    {
        JsonNode result = JsonMergePatch.Apply(
            Parse("""{ "interconnectors": [{ "fromRegionId": "TAS1", "toRegionId": "VIC1", "capacityMw": 594 }, { "fromRegionId": "VIC1", "toRegionId": "TAS1", "capacityMw": 478 }] }"""),
            Parse("""{ "interconnectors": [{ "fromRegionId": "TAS1", "toRegionId": "VIC1", "$remove": true }] }"""));

        result.ToJsonString().Should().Be("{\"interconnectors\":[{\"fromRegionId\":\"VIC1\",\"toRegionId\":\"TAS1\",\"capacityMw\":478}]}");
    }

    [Fact]
    public void Apply_RejectsInterconnectorRemoveItemCarryingAnExtraField()
    {
        var act = () => JsonMergePatch.Apply(
            Parse("""{ "interconnectors": [] }"""),
            Parse("""{ "interconnectors": [{ "fromRegionId": "TAS1", "toRegionId": "VIC1", "capacityMw": 594, "$remove": true }] }"""));

        act.Should().Throw<FormatException>()
            .WithMessage("*interconnectors*remove item 0*'fromRegionId', 'toRegionId'*$remove*");
    }

    [Fact]
    public void Apply_RejectsInterconnectorPatchItemMissingAnEndpoint()
    {
        var act = () => JsonMergePatch.Apply(
            Parse("""{ "interconnectors": [] }"""),
            Parse("""{ "interconnectors": [{ "fromRegionId": "NSW1", "capacityMw": 957 }] }"""));

        act.Should().Throw<FormatException>()
            .WithMessage("*interconnectors*item 0*toRegionId*");
    }

    [Fact]
    public void Apply_RejectsMalformedKeyedPatchItems()
    {
        var act = () => JsonMergePatch.Apply(
            Parse("{ \"regions\": [] }"),
            Parse("{ \"regions\": [{ \"name\": \"missing key\" }] }"));

        act.Should().Throw<FormatException>()
            .WithMessage("*regions*item 0*regionId*");
    }

    [Fact]
    public void Apply_RejectsFalseRemoveMarker()
    {
        var act = () => JsonMergePatch.Apply(
            Parse("{ \"regions\": [] }"),
            Parse("{ \"regions\": [{ \"regionId\": \"NSW1\", \"$remove\": false }] }"));

        act.Should().Throw<FormatException>()
            .WithMessage("*$remove*must be true*");
    }

    [Fact]
    public void Apply_ReplacesUnregisteredArraysEvenWhenNested()
    {
        JsonNode result = JsonMergePatch.Apply(
            Parse("{ \"regions\": [{ \"regionId\": \"NSW1\", \"otherValues\": [1, 2] }] }"),
            Parse("{ \"regions\": [{ \"regionId\": \"NSW1\", \"otherValues\": [3] }] }"));

        result.ToJsonString().Should().Be("{\"regions\":[{\"regionId\":\"NSW1\",\"otherValues\":[3]}]}");
    }

    [Fact]
    public void Apply_RemovesNullProperties()
    {
        JsonNode result = JsonMergePatch.Apply(
            Parse("""{ "keep": 1, "remove": 2 }"""),
            Parse("""{ "remove": null }"""));

        result.ToJsonString().Should().Be("{\"keep\":1}");
    }

    [Fact]
    public void Apply_EmptyPatchIsIdentity()
    {
        JsonNode result = JsonMergePatch.Apply(
            Parse("""{ "a": { "b": 1 } }"""),
            Parse("""{}"""));

        result.ToJsonString().Should().Be("{\"a\":{\"b\":1}}");
    }

    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;
}