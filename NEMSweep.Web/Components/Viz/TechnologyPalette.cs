namespace NEMSweep.Web.Components.Viz;

/// <summary>
/// The colour a technology is drawn in, everywhere. Dispatch stacks, regional mix bars and sweep
/// series all name the same technologies, and a reader carries the colours between pages, so the
/// mapping lives in one place rather than being restated per chart.
/// </summary>
public static class TechnologyPalette
{
    private static readonly Dictionary<string, string> Generation = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Solar"] = "#d99a1e",
        ["Wind"] = "#3f8f86",
        ["Hydro"] = "#4372a8",
        ["Coal"] = "#525d68",
        ["Gas"] = "#b8613f",
    };

    private static readonly Dictionary<string, string> Storage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Battery"] = "#176b5b",
        ["PumpedHydro"] = "#4372a8",
    };

    /// <summary>Drawn back to front, so the weather-driven technologies sit at the bottom of a stack.</summary>
    public static IReadOnlyList<string> StackOrder { get; } = ["Solar", "Wind", "Hydro", "Coal", "Gas"];

    /// <summary>
    /// Technologies counted as renewable wherever this site states a renewable share. Hydro is
    /// included because the model's own grid-scale renewable share includes it; counting solar and
    /// wind alone reproduces the model's separate native share, which is a different figure.
    /// </summary>
    public static IReadOnlySet<string> Renewable { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Solar", "Wind", "Hydro" };

    private const string Unknown = "#8a7d72";

    public static string ForGeneration(string technology) =>
        Generation.GetValueOrDefault(technology, Unknown);

    public static string ForStorage(string technology) =>
        Storage.GetValueOrDefault(technology, Unknown);

    /// <summary>
    /// The technologies a result carries, in stack order first and then whatever else it names, so
    /// a fleet the palette has not met still appears rather than being dropped.
    /// </summary>
    public static IReadOnlyList<string> Order(IEnumerable<string> technologies)
    {
        ArgumentNullException.ThrowIfNull(technologies);

        string[] present = [.. technologies];
        return
        [
            .. StackOrder.Where(technology => present.Contains(technology, StringComparer.OrdinalIgnoreCase)),
            .. present.Where(technology => !StackOrder.Contains(technology, StringComparer.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }
}
