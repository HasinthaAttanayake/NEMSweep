namespace NEMSweep.CLI.Infrastructure;

/// <summary>
/// Publishes a set of files as close to atomically as the filesystem allows: every file is
/// written to a staging directory first, and only once all of them exist is the previous
/// version moved aside and the new set moved into place.
/// </summary>
/// <remarks>
/// A dispatch publication is many files that are read together. Writing them one by one leaves
/// a window in which a reader sees the new system artifact alongside a stale regional one, and
/// a failure part-way through leaves that mixture permanently. Staging first means a failure
/// during generation touches nothing, and a failure during the move is rolled back to the
/// previous set rather than left half-applied.
/// </remarks>
internal sealed class StagedFileSetWriter : IDisposable
{
    private readonly string _outputDirectory;
    private readonly string _stagingDirectory;
    private readonly string _backupDirectory;
    private readonly List<(string Staged, string Final)> _targets = [];

    /// <summary>Opens a staging area alongside <paramref name="outputDirectory"/>.</summary>
    /// <param name="outputDirectory">Directory the staged files are ultimately moved into.</param>
    public StagedFileSetWriter(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        _outputDirectory = outputDirectory;
        _stagingDirectory = Path.Combine(outputDirectory, $".dispatch-results-{Guid.NewGuid():N}");
        _backupDirectory = Path.Combine(_stagingDirectory, "previous");
        Directory.CreateDirectory(_stagingDirectory);
    }

    /// <summary>
    /// Stages one file's content, to land at <paramref name="fileName"/> in the output
    /// directory when <see cref="Commit"/> succeeds.
    /// </summary>
    /// <param name="fileName">File name, relative to the output directory.</param>
    /// <param name="content">The file's full content.</param>
    /// <param name="writeText">
    /// How the bytes are written, so callers can substitute the filesystem in tests.
    /// </param>
    public void Stage(string fileName, string content, Action<string, string> writeText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(writeText);
        string stagedPath = Path.Combine(_stagingDirectory, fileName);
        writeText(stagedPath, content);
        _targets.Add((stagedPath, Path.Combine(_outputDirectory, fileName)));
    }

    /// <summary>
    /// Moves every staged file into the output directory, displacing the previous version.
    /// On any failure the previous version is restored and the exception rethrown, so the
    /// output directory is never left holding a partial set.
    /// </summary>
    public void Commit()
    {
        Directory.CreateDirectory(_backupDirectory);
        var backups = new List<(string Backup, string Final)>();
        var installed = new List<string>();
        try
        {
            foreach ((_, string finalPath) in _targets)
            {
                if (File.Exists(finalPath))
                {
                    string backupPath = Path.Combine(_backupDirectory, Path.GetFileName(finalPath));
                    File.Move(finalPath, backupPath);
                    backups.Add((backupPath, finalPath));
                }
            }

            foreach ((string stagedPath, string finalPath) in _targets)
            {
                File.Move(stagedPath, finalPath);
                installed.Add(finalPath);
            }
        }
        catch
        {
            Rollback(backups, installed);
            throw;
        }
    }

    /// <summary>Removes the staging area, including any backup the commit no longer needs.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_stagingDirectory))
        {
            Directory.Delete(_stagingDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Clears whatever the failed commit managed to put in place, then restores the files it
    /// had already moved aside.
    /// </summary>
    /// <param name="backups">Previous versions already moved into the backup directory.</param>
    /// <param name="installed">
    /// Finals this commit actually wrote. Only these are deleted: a failure during the backup
    /// phase leaves untouched finals from the previous publication still in place, and deleting
    /// those would destroy files this commit never owned.
    /// </param>
    private static void Rollback(List<(string Backup, string Final)> backups, List<string> installed)
    {
        foreach (string finalPath in installed)
        {
            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }
        }

        foreach ((string backupPath, string finalPath) in backups)
        {
            if (File.Exists(backupPath))
            {
                File.Move(backupPath, finalPath);
            }
        }
    }
}
