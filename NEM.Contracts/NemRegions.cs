namespace NEM.Contracts;

/// <summary>The five NEM regions. Region identity is a string everywhere else; this is the one
/// place that says which strings are real.</summary>
public static class NemRegions
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "NSW1",
        "QLD1",
        "SA1",
        "TAS1",
        "VIC1",
    };

    public static bool IsKnown(string? regionId) => regionId is not null && All.Contains(regionId);
}