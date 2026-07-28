using NEM.Contracts;
using System.Text.Json;

namespace NEM.CLI;

internal static class GenerationInformationExport
{
    public static GenerationInformationDTO Create(
        string sourcePath,
        IReadOnlyList<GenerationInformationRow> rows)
    {
        return new GenerationInformationDTO(
            1,
            Path.GetFileName(sourcePath),
            DateTimeOffset.UtcNow,
            rows.ToArray());
    }

    public static void WriteJson(GenerationInformationDTO data, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }));
    }
}