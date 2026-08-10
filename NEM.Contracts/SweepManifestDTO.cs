namespace NEM.Contracts;

/// <summary>
/// Which sweeps exist under <c>data/sweeps/</c>. Emitted by the sweep run so a client can treat
/// sweep existence as data rather than as a hand-maintained list. Publishing which of them to show
/// remains a site decision.
/// </summary>
public sealed record SweepManifestDTO(
    int SchemaVersion,
    SweepManifestEntryDTO[] Sweeps);

/// <summary>
/// One published sweep. Labels, units and point counts come from the sweep's own index, which
/// <see cref="IndexPath"/> locates relative to the manifest.
/// </summary>
public sealed record SweepManifestEntryDTO(
    string SweepId,
    string Name,
    string IndexPath);
