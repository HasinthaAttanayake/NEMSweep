namespace NEM.Web.Services.Insights;

/// <summary>
/// How a finding should read. Tone is about what the number means for the scenario, not about
/// whether it is good news: a binding constraint is a <see cref="Constraint"/> whether the reader
/// wanted it or not.
/// </summary>
public enum FindingTone
{
    /// <summary>A fact worth stating that is neither an improvement nor a problem.</summary>
    Neutral,

    /// <summary>A result moved in the direction the scenario was reaching for.</summary>
    Favourable,

    /// <summary>A result worth a second look: a reversal, a divergence, or a cost that grew.</summary>
    Caution,

    /// <summary>The model hit a limit — a reliability target, a capacity ceiling, a failed run.</summary>
    Constraint,
}

/// <summary>
/// One thing the evidence says, written for a reader rather than assembled by one. Every finding
/// carries the figures it is drawn from so it can be checked against the tables on the same page;
/// nothing here estimates, forecasts, or advises.
/// </summary>
public sealed record Finding(
    string Headline,
    string Detail,
    FindingTone Tone = FindingTone.Neutral,
    string? Metric = null,
    string? MetricUnit = null,
    string? Href = null,
    string? LinkText = null);
