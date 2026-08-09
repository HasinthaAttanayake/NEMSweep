using System.Text.Json.Nodes;

namespace NEM.CLI.Infrastructure;

internal static class JsonMergePatch
{
    public static JsonNode Apply(JsonNode target, JsonNode patch)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(patch);

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

            result[propertyName] = result[propertyName] is JsonNode targetValue
                ? Apply(targetValue, patchValue)
                : Apply(new JsonObject(), patchValue);
        }

        return result;
    }
}