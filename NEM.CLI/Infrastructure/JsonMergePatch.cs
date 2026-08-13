using System.Text.Json.Nodes;

namespace NEM.CLI.Infrastructure;

internal static class JsonMergePatch
{
    private static readonly IReadOnlyDictionary<string, string> KeyedArrayKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["regions"] = "regionId",
            ["regions[].generatingFleets"] = "technology",
            ["regions[].storageFleets"] = "technology",
            ["monthlyCapacityFactors"] = "month",
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
                && TryGetKeyProperty(propertyPath, out string keyProperty))
            {
                result[propertyName] = MergeKeyedArray(
                    result[propertyName] as JsonArray,
                    patchArray,
                    propertyPath,
                    keyProperty);
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
        string keyProperty)
    {
        JsonArray result = target is null ? [] : (JsonArray)target.DeepClone();
        var targetIndexesByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < result.Count; index++)
        {
            JsonObject targetItem = RequireKeyedItem(result[index], path, index, keyProperty);
            string key = KeyOf(targetItem[keyProperty]!, path, index, keyProperty);
            if (!targetIndexesByKey.TryAdd(key, index))
            {
                throw new FormatException(
                    $"JSON keyed array '{path}' contains duplicate key '{key}' in target item {index}.");
            }
        }

        for (int index = 0; index < patch.Count; index++)
        {
            JsonObject patchItem = RequireKeyedItem(patch[index], path, index, keyProperty);
            string key = KeyOf(patchItem[keyProperty]!, path, index, keyProperty);
            bool remove = ReadRemoveFlag(patchItem, path, index);
            if (remove)
            {
                if (patchItem.Count != 2)
                {
                    throw new FormatException(
                        $"JSON keyed array '{path}' remove item {index} must contain only '{keyProperty}' and '$remove'.");
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
        string keyProperty)
    {
        if (item is not JsonObject itemObject)
        {
            throw new FormatException(
                $"JSON keyed array '{path}' item {index} must be an object containing '{keyProperty}'.");
        }

        if (!itemObject.ContainsKey(keyProperty) || itemObject[keyProperty] is null)
        {
            throw new FormatException(
                $"JSON keyed array '{path}' item {index} must contain a non-null '{keyProperty}'.");
        }

        return itemObject;
    }

    private static string KeyOf(JsonNode key, string path, int index, string keyProperty)
    {
        if (key is JsonObject or JsonArray)
        {
            throw new FormatException(
                $"JSON keyed array '{path}' item {index} field '{keyProperty}' must be a scalar value.");
        }

        return key.ToJsonString();
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

    private static bool TryGetKeyProperty(string path, out string keyProperty)
    {
        if (KeyedArrayKeys.TryGetValue(path, out string? exactKeyProperty)
            && exactKeyProperty is not null)
        {
            keyProperty = exactKeyProperty;
            return true;
        }

        if (path.EndsWith(".monthlyCapacityFactors", StringComparison.Ordinal))
        {
            keyProperty = "month";
            return true;
        }

        keyProperty = string.Empty;
        return false;
    }
}