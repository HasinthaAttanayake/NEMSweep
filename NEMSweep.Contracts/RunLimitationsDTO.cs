using System.Text.Json.Serialization;

namespace NEMSweep.Contracts;

/// <summary>
/// What this result cannot be read as saying. Published in the artifact rather than left to a
/// documentation page, because the figures travel without the page: a reader, or a program, that
/// has the result in front of it has no other way to learn that battery dispatch has no foresight
/// or that the reliability target was tested over one modelled period.
/// </summary>
/// <param name="Engine">
/// Statements true of every run, whatever the data. Fixed text, so two results at the same model
/// version carry the same list.
/// </param>
/// <param name="ThisRun">
/// Statements derived from this run's own evidence: what stopped the sizing search, what the
/// weather input represents, what the dispatch period covered. Empty when nothing beyond
/// <paramref name="Engine"/> applies.
/// </param>
/// <param name="Reference">Where the reasoning behind the engine statements is set out in full.</param>
/// <remarks>
/// Nothing here is specific to a dataset. A statement about the data a run consumed is derived from
/// that run's own input provenance, not from knowledge of any particular dataset, so a run on the
/// caller's own inputs gets statements about those inputs.
/// </remarks>
public sealed record RunLimitationsDTO(
    [property: JsonRequired] string[] Engine,
    [property: JsonRequired] string[] ThisRun,
    [property: JsonRequired] string Reference);
