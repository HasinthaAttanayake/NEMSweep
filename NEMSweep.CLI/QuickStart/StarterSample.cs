using System.Text.Json;

namespace NEMSweep.CLI.QuickStart;

internal sealed record StarterSample(string Id, string Revision, Uri Url, long Length, string Sha256)
{
    public static IReadOnlyList<StarterSample> LoadCatalogue()
    {
        using Stream stream = typeof(StarterSample).Assembly.GetManifestResourceStream("Starter.Catalogue")
            ?? throw new InvalidOperationException("The starter catalogue is missing from this CLI build.");
        return JsonSerializer.Deserialize<StarterSample[]>(stream, Infrastructure.JsonFile.StrictReadOptions)
            ?? throw new FormatException("The starter catalogue is empty.");
    }
}
