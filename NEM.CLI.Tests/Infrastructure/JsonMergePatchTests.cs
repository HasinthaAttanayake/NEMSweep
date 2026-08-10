using System.Text.Json.Nodes;
using AwesomeAssertions;
using NEM.CLI.Infrastructure;

namespace NEM.CLI.Tests.Infrastructure;

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