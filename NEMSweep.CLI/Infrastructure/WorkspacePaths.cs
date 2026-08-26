using NEMSweep.CLI.Configuration;

namespace NEMSweep.CLI.Infrastructure;

/// <summary>
/// The directories one invocation reads from and writes to. Every root is supplied by the caller,
/// so nothing here searches the filesystem for a repository: that is what lets the CLI run from an
/// install directory, a container, or any working directory rather than only from inside a clone.
/// </summary>
internal sealed class WorkspacePaths
{
    private WorkspacePaths(string workingRoot, string dataRoot, string outputRoot)
    {
        WorkingRoot = workingRoot;
        DataRoot = dataRoot;
        OutputRoot = outputRoot;
    }

    /// <summary>Base for resolving relative paths given on the command line or in settings.</summary>
    public string WorkingRoot { get; }

    /// <summary>Where scenario inputs are read from, and where <c>--ingest</c> writes them.</summary>
    public string DataRoot { get; }

    /// <summary>Where dispatch results and sweep artifacts are written.</summary>
    public string OutputRoot { get; }

    public string DispatchResultsPath => OutputPath("results.json");

    /// <summary>
    /// Builds the workspace for one invocation. Each root takes the first of: an explicit
    /// command-line override, the environment variable, then the configured setting. Environment
    /// variables matter because a container's settings file sits in a read-only image layer.
    /// </summary>
    /// <param name="settings">Settings loaded for this invocation.</param>
    /// <param name="workingRoot">Base for relative paths, normally the current directory.</param>
    /// <param name="dataRootOverride">Value of <c>--data-root</c>, or <see langword="null"/>.</param>
    /// <param name="outputRootOverride">Value of <c>--output</c>, or <see langword="null"/>.</param>
    public static WorkspacePaths Create(
        CliSettings settings,
        string workingRoot,
        string? dataRootOverride,
        string? outputRootOverride)
    {
        string fullWorkingRoot = Path.GetFullPath(workingRoot);
        return new WorkspacePaths(
            fullWorkingRoot,
            Resolve(dataRootOverride, "NEMSWEEP_DATA_ROOT", settings.DataRoot, fullWorkingRoot),
            Resolve(outputRootOverride, "NEMSWEEP_OUTPUT", settings.OutputRoot, fullWorkingRoot));
    }

    /// <summary>Builds a workspace from explicit roots, for tests and for nested runs.</summary>
    /// <param name="workingRoot">Base for relative command-line paths.</param>
    /// <param name="dataRoot">Where inputs are read from.</param>
    /// <param name="outputRoot">Where results are written.</param>
    public static WorkspacePaths FromRoots(string workingRoot, string dataRoot, string outputRoot) =>
        new(
            Path.GetFullPath(workingRoot),
            Path.GetFullPath(dataRoot, workingRoot),
            Path.GetFullPath(outputRoot, workingRoot));

    /// <summary>Resolves a configured or command-line path against the working root.</summary>
    /// <param name="path">An absolute path, or one relative to the working root.</param>
    public string ResolveConfiguredPath(string path) => Path.GetFullPath(path, WorkingRoot);

    /// <summary>Path to a named artifact under the data root.</summary>
    /// <param name="fileName">File name or relative path beneath the data root.</param>
    public string DataPath(string fileName) => Path.Combine(DataRoot, fileName);

    /// <summary>Path to a named artifact under the output root.</summary>
    /// <param name="fileName">File name or relative path beneath the output root.</param>
    public string OutputPath(string fileName) => Path.Combine(OutputRoot, fileName);

    /// <summary>Path to one region's weather artifact under the data root.</summary>
    /// <param name="regionId">NEM region identifier.</param>
    public string WeatherDataPath(string regionId) =>
        DataPath($"weather-{regionId.ToLowerInvariant()}.json");

    /// <summary>
    /// Expresses a path for provenance: relative to the data root where the artifact came from
    /// there, otherwise relative to the working root, which is what keeps a scenario config or sweep
    /// definition citing the directory it lives in. Falls back to the bare file name for anything
    /// under neither. The digest is the reproducibility boundary, so an absolute path here would only
    /// record where one machine happened to keep a file.
    /// </summary>
    /// <param name="fullPath">The absolute path an artifact was read from.</param>
    public string DescribeInputPath(string fullPath) =>
        RelativeTo(DataRoot, fullPath)
        ?? RelativeTo(WorkingRoot, fullPath)
        ?? Path.GetFileName(fullPath);

    /// <summary>Path relative to a root, or <see langword="null"/> when it falls outside it.</summary>
    private static string? RelativeTo(string root, string fullPath)
    {
        string relative = Path.GetRelativePath(root, fullPath);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? null
            : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string Resolve(
        string? commandLineOverride,
        string environmentVariable,
        string configured,
        string workingRoot)
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable(environmentVariable);
        string chosen = Blank(commandLineOverride)
            ? Blank(fromEnvironment) ? configured : fromEnvironment!
            : commandLineOverride!;
        return Path.GetFullPath(chosen, workingRoot);
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}
