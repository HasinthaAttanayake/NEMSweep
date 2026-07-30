using System.Text.Json;

namespace NEM.CLI.Infrastructure;

internal static class JsonFile
{
    internal static JsonSerializerOptions ReadOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static JsonSerializerOptions WriteOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, WriteOptions);

    public static void Write<T>(T value, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Serialize(value));
    }
}