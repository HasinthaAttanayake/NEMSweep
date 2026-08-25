using AwesomeAssertions;
using NEMSweep.CLI.Infrastructure;

namespace NEMSweep.CLI.Tests.Infrastructure;

public sealed class StagedFileSetWriterTests
{
    [Fact]
    public void Commit_MovesEveryStagedFileIntoTheOutputDirectory()
    {
        using var output = new TemporaryDirectory();
        File.WriteAllText(output.Path("results.json"), "previous system");

        using (var writer = new StagedFileSetWriter(output.Root))
        {
            writer.Stage("results.json", "next system", WriteCreatingDirectories);
            writer.Stage("results-nsw1.json", "next region", WriteCreatingDirectories);
            writer.Commit();
        }

        File.ReadAllText(output.Path("results.json")).Should().Be("next system");
        File.ReadAllText(output.Path("results-nsw1.json")).Should().Be("next region");
        Directory.GetDirectories(output.Root).Should().BeEmpty();
    }

    /// <summary>
    /// A failure part-way through moving the previous versions aside must leave the finals the
    /// commit has not reached yet exactly where they are. Those still belong to the previous
    /// publication, so deleting them would turn a failed publish into permanent data loss.
    /// </summary>
    [Fact]
    public void Commit_LeavesUnbackedUpPreviousFilesInPlace_WhenTheBackupPhaseFails()
    {
        using var output = new TemporaryDirectory();
        Directory.CreateDirectory(output.Path("regions"));
        File.WriteAllText(output.Path("results.json"), "previous system");
        File.WriteAllText(output.Path("regions", "results.json"), "previous region");

        using var writer = new StagedFileSetWriter(output.Root);
        // Both targets share a leaf file name, so the second backup collides with the first
        // inside the backup directory. The backup phase then fails with the first final already
        // moved aside and the second still in place.
        writer.Stage("results.json", "next system", WriteCreatingDirectories);
        writer.Stage(Path.Combine("regions", "results.json"), "next region", WriteCreatingDirectories);

        writer.Invoking(staged => staged.Commit()).Should().Throw<IOException>();

        File.ReadAllText(output.Path("results.json")).Should().Be("previous system");
        File.ReadAllText(output.Path("regions", "results.json")).Should().Be("previous region");
    }

    /// <summary>
    /// A failure once the new files are going in restores the whole previous set, rather than
    /// leaving readers a mixture of new and stale artifacts.
    /// </summary>
    [Fact]
    public void Commit_RestoresThePreviousSet_WhenTheApplyPhaseFails()
    {
        using var output = new TemporaryDirectory();
        File.WriteAllText(output.Path("results.json"), "previous system");
        File.WriteAllText(output.Path("results-nsw1.json"), "previous region");

        using var writer = new StagedFileSetWriter(output.Root);
        writer.Stage("results.json", "next system", WriteCreatingDirectories);
        writer.Stage("results-nsw1.json", "next region", (path, _) =>
        {
            // Staged as a directory, so moving it onto the final path fails after the first
            // target has already been installed.
            Directory.CreateDirectory(path);
        });

        writer.Invoking(staged => staged.Commit()).Should().Throw<IOException>();

        File.ReadAllText(output.Path("results.json")).Should().Be("previous system");
        File.ReadAllText(output.Path("results-nsw1.json")).Should().Be("previous region");
    }

    private static void WriteCreatingDirectories(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"nemsweep-staged-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Path(params string[] segments) =>
            System.IO.Path.Combine([Root, .. segments]);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
