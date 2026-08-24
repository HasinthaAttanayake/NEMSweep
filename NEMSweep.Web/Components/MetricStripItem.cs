namespace NEMSweep.Web.Components;

public sealed record MetricStripItem(
    string Label,
    string Value,
    string? Unit = null,
    string? Style = null);