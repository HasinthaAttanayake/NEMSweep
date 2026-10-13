using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace NEMSweep.CLI.QuickStart;

internal static class StarterArchive
{
    internal const long MaximumDownloadBytes = 32L * 1024 * 1024;
    internal const long MaximumExtractedBytes = 128L * 1024 * 1024;
    internal const int MaximumEntries = 256;
    private static readonly Regex PortableName = new("^[A-Za-z0-9_.#-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex DeviceName = new(
        "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static async Task DownloadAsync(
        HttpClient client, StarterSample sample, string path, CancellationToken cancellationToken)
    {
        if (sample.Url.Scheme != Uri.UriSchemeHttps
            || sample.Length <= 0 || sample.Length > MaximumDownloadBytes
            || sample.Sha256.Length != 64 || !sample.Sha256.All(Uri.IsHexDigit))
        {
            throw new FormatException($"Starter '{sample.Id}' has an invalid pinned download specification.");
        }

        using HttpResponseMessage response = await client.GetAsync(
            sample.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Starter '{sample.Id}' download returned HTTP {(int)response.StatusCode}. "
                + $"Check that samples release '{sample.Revision}' is published: {sample.Url}",
                inner: null, response.StatusCode);
        }
        if (response.RequestMessage?.RequestUri is { Scheme: not "https" })
        {
            throw new IOException("The starter download redirected to a non-HTTPS URL.");
        }
        if (response.Content.Headers.ContentLength is long declared && declared != sample.Length)
        {
            throw new IOException($"Starter '{sample.Id}' download length does not match its pinned length.");
        }

        using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            total += read;
            if (total > sample.Length || total > MaximumDownloadBytes)
            {
                throw new IOException($"Starter '{sample.Id}' exceeds its pinned download size.");
            }
            hash.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (total != sample.Length)
        {
            throw new IOException($"Starter '{sample.Id}' download is incomplete.");
        }
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(sample.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Starter '{sample.Id}' SHA-256 verification failed.");
        }
    }

    public static void Extract(string archivePath, string destination, CancellationToken cancellationToken = default)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count is 0 or > MaximumEntries)
        {
            throw new IOException($"Starter archive must contain 1 to {MaximumEntries} entries.");
        }
        var names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        long length = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool directory = entry.FullName.EndsWith('/');
            string relative = directory ? entry.FullName[..^1] : entry.FullName;
            string[] segments = relative.Split('/');
            if (segments.Any(segment => segment is "" or "." or ".."
                    || segment.Length > 255 || segment.EndsWith('.') || !PortableName.IsMatch(segment)
                    || DeviceName.IsMatch(segment))
                || !names.TryAdd(relative, directory))
            {
                throw new IOException($"Unsafe or colliding starter archive path: '{entry.FullName}'.");
            }
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType is not (0 or 0x8000 or 0x4000)
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                || (directory && entry.Length != 0))
            {
                throw new IOException($"Links and special entries are not allowed: '{entry.FullName}'.");
            }
            if (entry.Length > MaximumExtractedBytes - length)
            {
                throw new IOException($"Starter archive exceeds {MaximumExtractedBytes} extracted bytes.");
            }
            length += entry.Length;
        }

        foreach (string name in names.Keys)
        {
            for (int index = name.IndexOf('/'); index >= 0; index = name.IndexOf('/', index + 1))
            {
                if (names.TryGetValue(name[..index], out bool isDirectory) && !isDirectory)
                {
                    throw new IOException($"Starter archive has a file/directory collision: '{name}'.");
                }
            }
        }

        Directory.CreateDirectory(destination);
        long extracted = 0;
        byte[] buffer = new byte[81920];
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(entry.FullName.Replace('/', Path.DirectorySeparatorChar), destination);
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(path);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using Stream source = entry.Open();
            using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            long entryLength = 0;
            int read;
            while ((read = source.Read(buffer)) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                entryLength += read;
                extracted += read;
                if (entryLength > entry.Length || extracted > MaximumExtractedBytes)
                {
                    throw new IOException("Starter archive expanded beyond its declared size.");
                }
                target.Write(buffer, 0, read);
            }
            if (entryLength != entry.Length)
            {
                throw new IOException($"Starter archive entry is incomplete: '{entry.FullName}'.");
            }
        }
    }
}
