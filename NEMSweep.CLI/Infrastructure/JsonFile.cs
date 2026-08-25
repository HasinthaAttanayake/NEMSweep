using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NEMSweep.CLI.Infrastructure;

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

    /// <summary>
    /// How a published artifact is written. Indentation is roughly seventy percent of the bytes in
    /// one of these (a quarter of a gigabyte across a sweep) spent on whitespace that nothing
    /// reads: the CLI writes them, the site fetches them, and no one edits them by hand. It also
    /// puts every value on its own line, which turns a rerun into millions of changed lines and is
    /// what stops a diff being reviewable at all.
    /// </summary>
    private static JsonSerializerOptions WriteOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// The same JSON laid out for a person: the scenario config a sweep fans out to each point, the
    /// schema the CLI prints, the report it writes to the console. These are small and are read.
    /// </summary>
    private static JsonSerializerOptions ReadableWriteOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string Serialize<T>(T value) =>
        Canonicalized(value).ToJsonString(WriteOptions);

    /// <summary>Canonicalised and rounded exactly as <see cref="Serialize"/>, but laid out.</summary>
    public static string SerializeReadable<T>(T value) =>
        Canonicalized(value).ToJsonString(ReadableWriteOptions);

    public static string SerializeExact(JsonNode value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Canonicalize(value, propertyName: null, round: false).ToJsonString(ReadableWriteOptions);
    }

    public static string SerializeExact<T>(T value)
    {
        JsonNode node = JsonSerializer.SerializeToNode(value, WriteOptions)
            ?? throw new InvalidOperationException("JSON serialization produced no value.");
        return SerializeExact(node);
    }

    private static JsonNode Canonicalized<T>(T value)
    {
        JsonNode node = JsonSerializer.SerializeToNode(value, WriteOptions)
            ?? throw new InvalidOperationException("JSON serialization produced no value.");
        return Canonicalize(node, propertyName: null, round: true);
    }

    public static void Write<T>(T value, string path) =>
        WriteToPath(path, Serialize(value));

    public static void WriteExact(JsonNode value, string path) =>
        WriteToPath(path, SerializeExact(value));

    /// <summary>Writes <paramref name="contents"/>, creating the target directory if needed.</summary>
    private static void WriteToPath(string path, string contents)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, contents);
    }

    /// <summary>
    /// Orders every object's properties so a rerun produces a comparable file, and where
    /// <paramref name="round"/> is set, rounds each numeric leaf to the precision its property
    /// name implies. Rounding off is the "exact" form, used where a value must survive
    /// untouched; the property name is still threaded through but goes unread.
    /// </summary>
    private static JsonNode Canonicalize(JsonNode node, string? propertyName, bool round)
    {
        if (node is JsonObject sourceObject)
        {
            var result = new JsonObject();
            foreach ((string name, JsonNode? child) in sourceObject.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                string childPropertyName = HasExplicitUnit(propertyName) ? propertyName! : name;
                result[name] = child is null ? null : Canonicalize(child, childPropertyName, round);
            }

            return result;
        }

        if (node is JsonArray sourceArray)
        {
            var result = new JsonArray();
            foreach (JsonNode? item in sourceArray)
            {
                result.Add(item is null ? null : Canonicalize(item, propertyName, round));
            }

            return result;
        }

        if (round && node is JsonValue value && propertyName is not null)
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
        propertyName.Equals("TransmissionSlcotAudPerMwh", StringComparison.OrdinalIgnoreCase) ? 4
        : propertyName.Contains("Aud", StringComparison.OrdinalIgnoreCase) ? 2
        : propertyName.EndsWith("Mw", StringComparison.OrdinalIgnoreCase)
            || propertyName.EndsWith("Mwh", StringComparison.OrdinalIgnoreCase) ? 1
        : propertyName.Contains("share", StringComparison.OrdinalIgnoreCase) ? 4
        : 15;

    private static bool HasExplicitUnit(string? propertyName) => propertyName is not null
        && (propertyName.EndsWith("Mw", StringComparison.OrdinalIgnoreCase)
            || propertyName.EndsWith("Mwh", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("share", StringComparison.OrdinalIgnoreCase));
}