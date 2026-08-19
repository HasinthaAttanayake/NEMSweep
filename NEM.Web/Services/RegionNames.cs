using NEM.Contracts;

namespace NEM.Web.Services;

/// <summary>
/// How a region identifier is written on the site. The artifacts carry NEM identifiers such as
/// NSW1; a reader wants the state. Both are shown together wherever there is room, because the
/// identifier is what appears in the underlying JSON.
/// </summary>
public static class RegionNames
{
    private static readonly Dictionary<string, string> States = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NSW1"] = "New South Wales",
        ["QLD1"] = "Queensland",
        ["SA1"] = "South Australia",
        ["TAS1"] = "Tasmania",
        ["VIC1"] = "Victoria",
    };

    /// <summary>The state name alone, or the raw identifier when it is not one of the NEM regions.</summary>
    public static string State(string regionId) => States.GetValueOrDefault(regionId, regionId);

    /// <summary>The state name with its region identifier, as "Victoria (VIC1)".</summary>
    public static string Full(string regionId) =>
        NemRegions.IsKnown(regionId) ? $"{State(regionId)} ({regionId})" : regionId;

    /// <summary>
    /// State names in a sentence, as "New South Wales, Queensland and Victoria". Joining on "and"
    /// alone strung five states together with four of them, which is not how the list is read
    /// aloud.
    /// </summary>
    public static string StateList(IEnumerable<string> regionIds) =>
        Readable(regionIds.Select(State));

    /// <summary>State names with their identifiers in a sentence, as "Victoria (VIC1) and ...".</summary>
    public static string FullList(IEnumerable<string> regionIds) =>
        Readable(regionIds.Select(Full));

    /// <summary>
    /// Names in a readable list, so three names read as "A, B and C" rather than "A, B, C" or
    /// "A and B and C".
    /// </summary>
    public static string Readable(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        string[] values = [.. names];
        return values.Length switch
        {
            0 => string.Empty,
            1 => values[0],
            _ => $"{string.Join(", ", values[..^1])} and {values[^1]}",
        };
    }
}
