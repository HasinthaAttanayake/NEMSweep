using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NEM.CLI.Infrastructure;

internal static class JsonFile
{
    internal static JsonSerializerOptions ReadOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static JsonSerializerOptions StrictReadOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static JsonSerializerOptions WriteOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string Serialize<T>(T value)
    {
        JsonNode node = JsonSerializer.SerializeToNode(value, WriteOptions)
            ?? throw new InvalidOperationException("JSON serialization produced no value.");
        return Canonicalize(node, propertyName: null).ToJsonString(WriteOptions);
    }

    public static string SerializeExact(JsonNode value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return CanonicalizeExact(value).ToJsonString(WriteOptions);
    }

    public static string SerializeExact<T>(T value)
    {
        JsonNode node = JsonSerializer.SerializeToNode(value, WriteOptions)
            ?? throw new InvalidOperationException("JSON serialization produced no value.");
        return SerializeExact(node);
    }

    public static void Write<T>(T value, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Serialize(value));
    }

    public static void WriteExact(JsonNode value, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, SerializeExact(value));
    }

    private static JsonNode CanonicalizeExact(JsonNode node)
    {
        if (node is JsonObject sourceObject)
        {
            var result = new JsonObject();
            foreach ((string name, JsonNode? child) in sourceObject.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                result[name] = child is null ? null : CanonicalizeExact(child);
            }

            return result;
        }

        if (node is JsonArray sourceArray)
        {
            var result = new JsonArray();
            foreach (JsonNode? item in sourceArray)
            {
                result.Add(item is null ? null : CanonicalizeExact(item));
            }

            return result;
        }

        return node.DeepClone();
    }

    private static JsonNode Canonicalize(JsonNode node, string? propertyName)
    {
        if (node is JsonObject sourceObject)
        {
            var result = new JsonObject();
            foreach ((string name, JsonNode? child) in sourceObject.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                string childPropertyName = HasExplicitUnit(propertyName) ? propertyName! : name;
                result[name] = child is null ? null : Canonicalize(child, childPropertyName);
            }

            return result;
        }

        if (node is JsonArray sourceArray)
        {
            var result = new JsonArray();
            foreach (JsonNode? item in sourceArray)
            {
                result.Add(item is null ? null : Canonicalize(item, propertyName));
            }

            return result;
        }

        if (node is JsonValue value && propertyName is not null)
        {
            int decimalPlaces = DecimalPlaces(propertyName);
            if (value.TryGetValue<double>(out double doubleValue))
            {
                return JsonValue.Create(Math.Round(doubleValue, decimalPlaces, MidpointRounding.AwayFromZero))!;
            }

            if (value.TryGetValue<decimal>(out decimal decimalValue))
            {
                return JsonValue.Create(Math.Round(decimalValue, decimalPlaces, MidpointRounding.AwayFromZero))!;
            }
        }

        return node.DeepClone();
    }

    private static int DecimalPlaces(string propertyName) =>
        propertyName.Contains("Aud", StringComparison.OrdinalIgnoreCase) ? 2
        : propertyName.EndsWith("Mw", StringComparison.OrdinalIgnoreCase)
            || propertyName.EndsWith("Mwh", StringComparison.OrdinalIgnoreCase) ? 1
        : propertyName.Contains("share", StringComparison.OrdinalIgnoreCase) ? 4
        : 15;

    private static bool HasExplicitUnit(string? propertyName) => propertyName is not null
        && (propertyName.EndsWith("Mw", StringComparison.OrdinalIgnoreCase)
            || propertyName.EndsWith("Mwh", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("share", StringComparison.OrdinalIgnoreCase));
}