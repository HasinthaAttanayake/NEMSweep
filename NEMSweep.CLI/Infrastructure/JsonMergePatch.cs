using System.Text;
using System.Text.Json.Nodes;

namespace NEMSweep.CLI.Infrastructure;

internal static class JsonMergePatch
{
    /// <summary>
    /// Arrays merged by key rather than replaced wholesale. Most take a single key field; an
    /// interconnector is only identified by both of its endpoints, so it takes two.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> KeyedArrayKeys =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["regions"] = ["regionId"],
            ["regions[].generatingFleets"] = ["technology"],
            ["regions[].storageFleets"] = ["technology"],
            ["monthlyCapacityFactors"] = ["month"],
            ["interconnectors"] = ["fromRegionId", "toRegionId"],
        };

    public static JsonNode Apply(JsonNode target, JsonNode patch)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(patch);

        return Apply(target, patch, string.Empty);
    }

    private static JsonNode Apply(JsonNode target, JsonNode patch, string path)
    {
        if (patch is not JsonObject patchObject)
        {
            return patch.DeepClone();
        }

        JsonObject result = target is JsonObject targetObject
            ? (JsonObject)targetObject.DeepClone()
            : [];
        foreach ((string propertyName, JsonNode? patchValue) in patchObject)
        {
            if (patchValue is null)
            {
                result.Remove(propertyName);
                continue;
            }

            string propertyPath = path.Length == 0 ? propertyName : $"{path}.{propertyName}";
            if (patchValue is JsonArray patchArray
                && TryGetKeyProperties(propertyPath, out string[] keyProperties))
            {
                result[propertyName] = MergeKeyedArray(
                    result[propertyName] as JsonArray,
                    patchArray,
                    propertyPath,
                    keyProperties);
                continue;
            }

            result[propertyName] = result[propertyName] is JsonNode targetValue
                ? Apply(targetValue, patchValue, propertyPath)
                : Apply(new JsonObject(), patchValue, propertyPath);
        }

        return result;
    }

    private static JsonArray MergeKeyedArray(
        JsonArray? target,
        JsonArray patch,
        string path,
        string[] keyProperties)
    {
        JsonArray result = target is null ? [] : (JsonArray)target.DeepClone();
        var targetIndexesByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < result.Count; index++)
        {
            JsonObject targetItem = RequireKeyedItem(result[index], path, index, keyProperties);
            string key = KeyOf(targetItem, path, index, keyProperties);
            if (!targetIndexesByKey.TryAdd(key, index))
            {
                throw new FormatException(
                    $"JSON keyed array '{path}' contains duplicate key {key} in target item {index}.");
            }
        }

        for (int index = 0; index < patch.Count; index++)
        {
            JsonObject patchItem = RequireKeyedItem(patch[index], path, index, keyProperties);
            string key = KeyOf(patchItem, path, index, keyProperties);
            bool remove = ReadRemoveFlag(patchItem, path, index);
            if (remove)
            {
                if (patchItem.Count != keyProperties.Length + 1)
                {
                    throw new FormatException(
                        $"JSON keyed array '{path}' remove item {index} must contain only "
                        + $"{DescribeKeys(keyProperties)} and '$remove'.");
                }

                if (targetIndexesByKey.TryGetValue(key, out int targetIndex))
                {
                    result.RemoveAt(targetIndex);
                    targetIndexesByKey.Remove(key);
                    foreach (string remainingKey in targetIndexesByKey.Keys.ToArray())
                    {
                        if (targetIndexesByKey[remainingKey] > targetIndex)
                        {
                            targetIndexesByKey[remainingKey]--;
                        }
                    }
                }

                continue;
            }

            if (targetIndexesByKey.TryGetValue(key, out int existingIndex))
            {
                result[existingIndex] = Apply(result[existingIndex]!, patchItem, $"{path}[]");
            }
            else
            {
                result.Add(Apply(new JsonObject(), patchItem, $"{path}[]"));
                targetIndexesByKey.Add(key, result.Count - 1);
            }
        }

        return result;
    }

    private static JsonObject RequireKeyedItem(
        JsonNode? item,
        string path,
        int index,
        string[] keyProperties)
    {
        if (item is not JsonObject itemObject)
        {
            throw new FormatException(
                $"JSON keyed array '{path}' item {index} must be an object containing "
                + $"{DescribeKeys(keyProperties)}.");
        }

        foreach (string keyProperty in keyProperties)
        {
            if (!itemObject.ContainsKey(keyProperty) || itemObject[keyProperty] is null)
            {
                throw new FormatException(
                    $"JSON keyed array '{path}' item {index} must contain a non-null '{keyProperty}'.");
            }
        }

        return itemObject;
    }

    /// <summary>
    /// A stable, collision-free identity for a keyed item. Each field is rendered as its JSON token
    /// and length-prefixed, so no two distinct field tuples can produce the same string.
    /// </summary>
    private static string KeyOf(JsonObject item, string path, int index, string[] keyProperties)
    {
        var key = new StringBuilder();
        foreach (string keyProperty in keyProperties)
        {
            JsonNode field = item[keyProperty]!;
            if (field is JsonObject or JsonArray)
            {
                throw new FormatException(
                    $"JSON keyed array '{path}' item {index} field '{keyProperty}' must be a scalar value.");
            }

            string token = field.ToJsonString();
            key.Append(token.Length).Append(':').Append(token);
        }

        return key.ToString();
    }

    private static bool ReadRemoveFlag(JsonObject item, string path, int index)
    {
        if (!item.TryGetPropertyValue("$remove", out JsonNode? value))
        {
            return false;
        }

        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<bool>(out bool remove))
        {
            throw new FormatException(
                $"JSON keyed array '{path}' item {index} field '$remove' must be a boolean.");
        }

        if (!remove)
        {
            throw new FormatException(
                $"JSON keyed array '{path}' item {index} field '$remove' must be true when present.");
        }

        return remove;
    }

    private static string DescribeKeys(string[] keyProperties) =>
        string.Join(", ", keyProperties.Select(key => $"'{key}'"));

    private static bool TryGetKeyProperties(string path, out string[] keyProperties)
    {
        if (KeyedArrayKeys.TryGetValue(path, out string[]? exactKeyProperties)
            && exactKeyProperties is not null)
        {
            keyProperties = exactKeyProperties;
            return true;
        }

        if (path.EndsWith(".monthlyCapacityFactors", StringComparison.Ordinal))
        {
            keyProperties = ["month"];
            return true;
        }

        keyProperties = [];
        return false;
    }
}
