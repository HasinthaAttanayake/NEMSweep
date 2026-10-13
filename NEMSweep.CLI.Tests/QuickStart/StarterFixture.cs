using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NEMSweep.CLI.QuickStart;

namespace NEMSweep.CLI.Tests.QuickStart;

internal sealed class StarterFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"nemsweep-starter-{Guid.NewGuid():N}");

    public StarterFixture() => Directory.CreateDirectory(Root);

    public static StarterSample Sample(byte[] bytes, string id = "simple") =>
        new(id, "test-revision", new Uri($"https://example.invalid/{id}.zip"),
            bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)));

    public static byte[] Archive(params (string Name, string Text, int? Attributes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string text, int? attributes) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);
                if (attributes.HasValue)
                {
                    entry.ExternalAttributes = attributes.Value;
                }
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }
        return stream.ToArray();
    }

    public string WriteArchive(byte[] bytes)
    {
        string path = Path.Combine(Root, $"{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public static byte[] PackDirectory(string directory)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                ZipArchiveEntry entry = archive.CreateEntry(
                    Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'));
                using Stream source = File.OpenRead(path);
                using Stream target = entry.Open();
                source.CopyTo(target);
            }
        }
        return stream.ToArray();
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

internal sealed class SampleHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }

    public SampleHandler(byte[] bytes) : this((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes),
    }))
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return respond(request, cancellationToken);
    }
}

internal sealed class StreamingContent(byte[] bytes) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        stream.WriteAsync(bytes).AsTask();

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
