using NEMSweep.Contracts;

namespace NEMSweep.Web.Services;

/// <summary>
/// What kind of thing the reader currently has open.
/// </summary>
public enum ScenarioKind
{
    /// <summary>The published baseline run: the current fleet, dispatched over the artifact period.</summary>
    Baseline,

    /// <summary>A sweep as a whole: every run in it, read against the axis it varies.</summary>
    SweepOverview,

    /// <summary>One run inside a sweep, at one value of that sweep's axis.</summary>
    SweepRun,

    /// <summary>An input the whole site shares. Not a scenario, and says so.</summary>
    SharedInput,

    /// <summary>Anything else, including the landing page and Not found.</summary>
    None,
}

/// <summary>
/// The scenario a route resolves to, in the words the navigation shows. Pure data: it names the
/// scenario, says where it sits, and carries the routes to step through a sweep.
/// </summary>
/// <remarks>
/// This exists because the navigation used to present a sweep as one more page beside "Demand"
/// and "Hourly dispatch", which hid the fact that a sweep is a family of 25 scenarios while
/// "Hourly dispatch" is a single one. Every route the site serves is either a scenario, a view of
/// one, or an input shared by all of them, and the sidebar now says which.
/// </remarks>
/// <param name="Kind">Which of the four the route is.</param>
/// <param name="Title">The scenario's name, as a heading.</param>
/// <param name="Detail">One line under the title: what distinguishes this scenario.</param>
/// <param name="SweepId">The sweep this belongs to, when it belongs to one.</param>
/// <param name="RunNumber">1-based position of this run among the sweep's viewable runs.</param>
/// <param name="RunCount">How many of the sweep's runs can be opened.</param>
/// <param name="RunRoute">Route to this run's own dispatch view, when a run is open.</param>
/// <param name="SweepRoute">Route to the sweep this run belongs to.</param>
/// <param name="PreviousRoute">Route to the previous viewable run, when there is one.</param>
/// <param name="NextRoute">Route to the next viewable run, when there is one.</param>
public sealed record ScenarioScope(
    ScenarioKind Kind,
    string Title,
    string Detail,
    string? SweepId = null,
    int? RunNumber = null,
    int? RunCount = null,
    string? RunRoute = null,
    string? SweepRoute = null,
    string? PreviousRoute = null,
    string? NextRoute = null)
{
    /// <summary>Whether this scope is a single run rather than a family or an input.</summary>
    public bool IsSingleRun => Kind is ScenarioKind.Baseline or ScenarioKind.SweepRun;

    /// <summary>
    /// Resolves a relative route against the sweeps the navigation has loaded.
    /// </summary>
    /// <param name="relativePath">Route with no leading slash and no query string.</param>
    /// <param name="sweeps">Every sweep whose index loaded, in manifest order.</param>
    public static ScenarioScope Resolve(string? relativePath, IReadOnlyList<SweepIndexDTO> sweeps)
    {
        ArgumentNullException.ThrowIfNull(sweeps);

        string path = (relativePath ?? string.Empty).Trim('/');
        string[] segments = path.Length == 0
            ? []
            : path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return new ScenarioScope(ScenarioKind.None, "NEMSweep", "Overview");
        }

        // Both result views read the same published baseline run, so both report the same scenario.
        if (segments is ["regions"] or ["dispatch"])
        {
            return Baseline();
        }

        if (segments is ["inputs", var input])
        {
            return new ScenarioScope(
                ScenarioKind.SharedInput,
                InputTitle(input),
                "Shared input · every scenario uses this");
        }

        if (segments is ["sweeps", var sweepId])
        {
            SweepIndexDTO? sweep = FindSweep(sweeps, Unescape(sweepId));
            if (sweep is null)
            {
                return new ScenarioScope(ScenarioKind.None, "Sweep", "Not found");
            }

            int viewable = sweep.Points.Count(IsViewable);
            return new ScenarioScope(
                ScenarioKind.SweepOverview,
                sweep.Name,
                $"{viewable:N0} scenarios · {sweep.Axis.Label} ({sweep.Axis.Unit})",
                SweepId: sweep.SweepId,
                RunCount: viewable,
                SweepRoute: SweepPaths.PageRoute(sweep.SweepId));
        }

        if (segments is ["runs", var runSweepId, var pointId])
        {
            return ResolveRun(sweeps, Unescape(runSweepId), Unescape(pointId));
        }

        return new ScenarioScope(ScenarioKind.None, "NEMSweep", string.Empty);
    }

    private static ScenarioScope Baseline() => new(
        ScenarioKind.Baseline,
        "Baseline",
        "The fleet as it stands · nothing added");

    private static ScenarioScope ResolveRun(
        IReadOnlyList<SweepIndexDTO> sweeps,
        string sweepId,
        string pointId)
    {
        SweepIndexDTO? sweep = FindSweep(sweeps, sweepId);
        SweepIndexPointDTO[] viewable = sweep is null
            ? []
            : [.. sweep.Points.Where(IsViewable)];
        int index = Array.FindIndex(viewable, point =>
            string.Equals(point.PointId, pointId, StringComparison.Ordinal));
        if (sweep is null || index < 0)
        {
            return new ScenarioScope(ScenarioKind.None, "Run", "Not found");
        }

        SweepIndexPointDTO point = viewable[index];
        return new ScenarioScope(
            ScenarioKind.SweepRun,
            point.Label,
            $"{sweep.Axis.Label} {point.AxisValue:N0} {sweep.Axis.Unit}",
            SweepId: sweep.SweepId,
            RunNumber: index + 1,
            RunCount: viewable.Length,
            RunRoute: SweepPaths.RunRoute(sweep.SweepId, point.PointId),
            SweepRoute: SweepPaths.PageRoute(sweep.SweepId),
            PreviousRoute: index > 0
                ? SweepPaths.RunRoute(sweep.SweepId, viewable[index - 1].PointId)
                : null,
            NextRoute: index < viewable.Length - 1
                ? SweepPaths.RunRoute(sweep.SweepId, viewable[index + 1].PointId)
                : null);
    }

    private static SweepIndexDTO? FindSweep(IReadOnlyList<SweepIndexDTO> sweeps, string sweepId) =>
        sweeps.FirstOrDefault(sweep =>
            string.Equals(sweep.SweepId, sweepId, StringComparison.Ordinal));

    /// <summary>
    /// Matches <see cref="DispatchRunContextResolver"/>: a point with no results is not a scenario
    /// anyone can open, so it is not counted or stepped through.
    /// </summary>
    private static bool IsViewable(SweepIndexPointDTO point) =>
        point.Status == SweepPointStatus.Succeeded && !string.IsNullOrWhiteSpace(point.DetailPath);

    private static string InputTitle(string input) => input switch
    {
        "demand" => "Demand",
        "weather" => "Weather",
        "generation" => "Generation fleet",
        _ => "Input",
    };

    private static string Unescape(string segment) => Uri.UnescapeDataString(segment);
}
