using System.IO.Compression;
using System.Net;
using AwesomeAssertions;
using NEMSweep.CLI.QuickStart;

namespace NEMSweep.CLI.Tests.QuickStart;

public sealed class StarterArchiveTests
{
    [Fact]
    public async Task Download_VerifiesExactPinnedBytes()
    {
        using var fixture = new StarterFixture();
        byte[] bytes = [1, 2, 3];
        using var client = new HttpClient(new SampleHandler(bytes));
        string path = Path.Combine(fixture.Root, "download.zip");

        await StarterArchive.DownloadAsync(client, StarterFixture.Sample(bytes), path, default);

        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Fact]
    public async Task Download_RejectsHashMismatch()
    {
        using var fixture = new StarterFixture();
        byte[] bytes = [1, 2, 3];
        using var client = new HttpClient(new SampleHandler(bytes));
        StarterSample sample = StarterFixture.Sample(bytes) with { Sha256 = new string('0', 64) };

        Func<Task> act = () => StarterArchive.DownloadAsync(
            client, sample, Path.Combine(fixture.Root, "download.zip"), default);

        await act.Should().ThrowAsync<IOException>().WithMessage("*SHA-256*");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Download_RejectsIncorrectStreamLengthWithoutContentLength(int pinnedLength)
    {
        using var fixture = new StarterFixture();
        byte[] bytes = [1, 2, 3];
        using var client = new HttpClient(new SampleHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamingContent(bytes) })));
        StarterSample sample = StarterFixture.Sample(bytes) with { Length = pinnedLength };

        Func<Task> act = () => StarterArchive.DownloadAsync(
            client, sample, Path.Combine(fixture.Root, "download.zip"), default);

        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task Download_RejectsIncorrectContentLength()
    {
        using var fixture = new StarterFixture();
        byte[] bytes = [1, 2, 3];
        using var client = new HttpClient(new SampleHandler(bytes));
        StarterSample sample = StarterFixture.Sample(bytes) with { Length = 4 };

        Func<Task> act = () => StarterArchive.DownloadAsync(
            client, sample, Path.Combine(fixture.Root, "download.zip"), default);

        await act.Should().ThrowAsync<IOException>().WithMessage("*pinned length*");
    }

    [Theory]
    [InlineData("http://example.invalid/simple.zip", 3, 64)]
    [InlineData("https://example.invalid/simple.zip", 0, 64)]
    [InlineData("https://example.invalid/simple.zip", 33554433, 64)]
    [InlineData("https://example.invalid/simple.zip", 3, 63)]
    public async Task Download_RejectsInvalidPinsBeforeHttp(string url, long length, int hashLength)
    {
        using var fixture = new StarterFixture();
        using var handler = new SampleHandler([1, 2, 3]);
        using var client = new HttpClient(handler);
        var sample = new StarterSample("simple", "test", new Uri(url), length, new string('0', hashLength));

        Func<Task> act = () => StarterArchive.DownloadAsync(
            client, sample, Path.Combine(fixture.Root, "download.zip"), default);

        await act.Should().ThrowAsync<FormatException>();
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Download_AcceptsExactlyThirtyTwoMib()
    {
        using var fixture = new StarterFixture();
        byte[] bytes = new byte[StarterArchive.MaximumDownloadBytes];
        using var client = new HttpClient(new SampleHandler(bytes));
        string path = Path.Combine(fixture.Root, "download.zip");

        await StarterArchive.DownloadAsync(client, StarterFixture.Sample(bytes), path, default);

        new FileInfo(path).Length.Should().Be(32L * 1024 * 1024);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Download_ExplainsUnavailableRelease(HttpStatusCode status)
    {
        using var fixture = new StarterFixture();
        using var client = new HttpClient(new SampleHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(status))));

        Func<Task> act = () => StarterArchive.DownloadAsync(client, StarterFixture.Sample([1]),
            Path.Combine(fixture.Root, "download.zip"), default);

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage($"*HTTP {(int)status}*published*");
    }

    [Fact]
    public async Task Download_RejectsNonHttpsRedirect()
    {
        using var fixture = new StarterFixture();
        byte[] bytes = [1];
        using var client = new HttpClient(new SampleHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.invalid/redirect.zip"),
            })));

        Func<Task> act = () => StarterArchive.DownloadAsync(client, StarterFixture.Sample(bytes),
            Path.Combine(fixture.Root, "download.zip"), default);

        await act.Should().ThrowAsync<IOException>().WithMessage("*non-HTTPS*");
    }

    [Fact]
    public void Extract_PreservesOriginalAemoNamesAndEpwRoleDirectories()
    {
        using var fixture = new StarterFixture();
        const string demand = "demand/operational-demand-hh/PUBLIC_ARCHIVE#DEMANDOPERATIONALACTUAL#FILE01#202507010000.zip";
        const string solar = "weather/NSW1/solar/AUS_NSW_Dubbo.City.Rgnl.AP.957190_TMYx.2011-2025.epw";
        const string wind = "weather/NSW1/wind/AUS_NSW_Armidale.AP.957730_TMYx.2011-2025.epw";
        string path = fixture.WriteArchive(StarterFixture.Archive(
            (demand, "original demand bytes", null),
            (solar, "original solar bytes", null),
            (wind, "original wind bytes", null)));
        string destination = Path.Combine(fixture.Root, "bundle");

        StarterArchive.Extract(path, destination);

        File.ReadAllText(Path.Combine(destination, demand)).Should().Be("original demand bytes");
        File.ReadAllText(Path.Combine(destination, solar)).Should().Be("original solar bytes");
        File.ReadAllText(Path.Combine(destination, wind)).Should().Be("original wind bytes");
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/rooted.txt")]
    [InlineData("C:/rooted.txt")]
    [InlineData("dir\\escape.txt")]
    [InlineData("dir/../escape.txt")]
    [InlineData("./manifest.json")]
    [InlineData("CON.txt")]
    [InlineData("dir/LPT1.epw")]
    [InlineData("file.")]
    [InlineData("file ")]
    [InlineData("dir//file")]
    public void Extract_RejectsNonportableNamesBeforeWriting(string name)
    {
        using var fixture = new StarterFixture();
        string path = fixture.WriteArchive(StarterFixture.Archive((name, "text", null)));
        string destination = Path.Combine(fixture.Root, "bundle");

        Action act = () => StarterArchive.Extract(path, destination);

        act.Should().Throw<IOException>();
        Directory.Exists(destination).Should().BeFalse();
    }

    [Theory]
    [InlineData("manifest.json", "Manifest.json")]
    [InlineData("dir", "dir/file.txt")]
    [InlineData("dir/", "DIR")]
    public void Extract_RejectsDuplicateAndAncestorCollisions(string first, string second)
    {
        using var fixture = new StarterFixture();
        byte[] bytes = StarterFixture.Archive((first, "", null), (second, "text", null));
        string destination = Path.Combine(fixture.Root, "bundle");

        Action act = () => StarterArchive.Extract(fixture.WriteArchive(bytes), destination);

        act.Should().Throw<IOException>();
        Directory.Exists(destination).Should().BeFalse();
    }

    [Theory]
    [InlineData(unchecked((int)0xA1FF0000))]
    [InlineData(unchecked((int)0x61FF0000))]
    [InlineData((int)FileAttributes.ReparsePoint)]
    public void Extract_RejectsLinksAndSpecialEntries(int attributes)
    {
        using var fixture = new StarterFixture();
        string path = fixture.WriteArchive(StarterFixture.Archive(("link", "target", attributes)));
        string destination = Path.Combine(fixture.Root, "bundle");

        Action act = () => StarterArchive.Extract(path, destination);

        act.Should().Throw<IOException>().WithMessage("*Links and special*");
        Directory.Exists(destination).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void Extract_EnforcesExactEntryCountBoundary(int count, bool accepted)
    {
        using var fixture = new StarterFixture();
        byte[] bytes = StarterFixture.Archive(
            Enumerable.Range(0, count).Select(index => ($"file-{index}", "", (int?)null)).ToArray());
        string destination = Path.Combine(fixture.Root, "bundle");

        Action act = () => StarterArchive.Extract(fixture.WriteArchive(bytes), destination);

        if (accepted)
        {
            act.Should().NotThrow();
            Directory.GetFiles(destination).Should().HaveCount(256);
        }
        else
        {
            act.Should().Throw<IOException>();
            Directory.Exists(destination).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Extract_EnforcesExactExpandedByteBoundary(int additionalBytes, bool accepted)
    {
        using var fixture = new StarterFixture();
        string archivePath = Path.Combine(fixture.Root, "large.zip");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using Stream entry = archive.CreateEntry("data").Open();
            byte[] block = new byte[1024 * 1024];
            for (int index = 0; index < 128; index++)
            {
                entry.Write(block);
            }
            entry.Write(new byte[additionalBytes]);
        }
        string destination = Path.Combine(fixture.Root, "bundle");

        Action act = () => StarterArchive.Extract(archivePath, destination);

        if (accepted)
        {
            act.Should().NotThrow();
            new FileInfo(Path.Combine(destination, "data")).Length.Should().Be(128L * 1024 * 1024);
        }
        else
        {
            act.Should().Throw<IOException>().WithMessage("*extracted bytes*");
            Directory.Exists(destination).Should().BeFalse();
        }
    }

    [Fact]
    public void Extract_CancellationWritesNothing()
    {
        using var fixture = new StarterFixture();
        string path = fixture.WriteArchive(StarterFixture.Archive(("manifest.json", "{}", null)));
        string destination = Path.Combine(fixture.Root, "bundle");

        Action act = () => StarterArchive.Extract(path, destination, new CancellationToken(canceled: true));

        act.Should().Throw<OperationCanceledException>();
        Directory.Exists(destination).Should().BeFalse();
    }
}
