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

    /// <summary>A short form for chart labels and table headers, where the identifier is enough.</summary>
    public static string Short(string regionId) => regionId;
}
