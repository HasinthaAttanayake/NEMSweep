# Outputs and provenance

This page is for reading the JSON NemSim produces, or consuming it programmatically. Every
artifact described here lives under `NEM.Web/wwwroot/data` and is written by `NEM.CLI`.

## Artifact map

| Path | What it is |
|---|---|
| `results.json` | Whole-system dispatch evidence for the run started by `--run-scenario`. The same shape is used whether the scenario declares one region or several. |
| `results-overview.json` | A compact version of the same run: totals and summaries, without the interval-by-interval series. |
| `results-{region}.json` | Full dispatch evidence for one region of a whole-system run. |
| `results-{region}-overview.json` | The compact counterpart of `results-{region}.json`. |
| `demand-{region}.json` | The imported operational-demand artifact for one region. |
| `weather-{region}.json` | The imported weather artifact for one region. |
| `generation-information.json` | The imported generation-fleet workbook, as data. |
| `sweeps/index.json` | Manifest of every published sweep: sweep ID, name, and the path to its index. Rewritten from what is on disk after every sweep run. |
| `sweeps/{sweepId}/index.json` | One sweep's index: its axis, scope, provenance, and one entry per point. |
| `sweeps/{sweepId}/points/{pointId}.json` | Full dispatch result for one sweep point (same shape as a `results.json` / `results-{region}.json`). |
| `sweeps/{sweepId}/points/{pointId}.status.json` | Whether that point succeeded or failed, and the failure detail if it did. |
| `sweeps/{sweepId}/configs/{pointId}.json` | The fully-resolved scenario config that point ran, produced by merging the point's overrides onto the sweep's baseline config. |
| `sweeps/{sweepId}/series/base-demand-{sha256}.json` | A base-demand series externalised from one or more points that share it byte-for-byte. See below. |

Read `NEM.CLI/Scenarios/DispatchResultsExport.cs` and `NEM.CLI/Scenarios/SweepArtifactExport.cs` if
you need the exact shape beyond what this page covers.

## Schema versions

Every artifact carries a `schemaVersion` field, and the current value for each artifact type is
defined in one place: `NEM.Contracts/ArtifactSchemaVersions.cs`. This page does not repeat those
numbers, because they would go stale the moment a schema changes. Consult the
[API reference](../api/index.md) for the current values.

## Sweep scalar vocabulary

A sweep publishes one set of scalars per point per region (and one for the system as a whole), in
`sweeps/{sweepId}/index.json` and alongside each point's full result. The catalogue in
`NEM.Contracts/SweepScalarCatalog.cs` is the full vocabulary: the JSON name each scalar is emitted
under, its label, and its unit.

| JSON name | Label | Unit |
|---|---|---|
| `slcoeAudPerMwh` | System levelised cost | AUD/MWh served |
| `generationSlcoeAudPerMwh` | Generation levelised cost | AUD/MWh served |
| `storageSlcoeAudPerMwh` | Storage levelised cost | AUD/MWh served |
| `demandMwh` | Demand | MWh |
| `energyServedMwh` | Energy served | MWh |
| `deliveredGenerationMwh` | Delivered generation | MWh |
| `achievedRenewableShareGridScale` | Achieved renewable share (grid scale) | fraction |
| `achievedRenewableShareNative` | Achieved renewable share (native) | fraction |
| `storagePowerMw` | Storage power capacity | MW |
| `storageEnergyMwh` | Storage energy capacity | MWh |
| `unservedEnergyMwh` | Unserved energy | MWh |
| `unservedEnergyPercentageOfDemand` | Unserved energy | % of demand |
| `unservedHours` | Unserved hours | h |
| `hoursServedFraction` | Hours served | fraction |
| `peakUnservedPowerMw` | Peak unserved power | MW |
| `curtailedEnergyMwh` | Curtailed energy | MWh |
| `transmissionSlcotAudPerMwh` | Transmission levelised cost | AUD/MWh served |
| `transmissionCostStatus` | Transmission cost status | status |
| `netImportedEnergyMwh` | Net imported energy | MWh |

`transmissionCostStatus` is the one entry the catalogue marks as not chartable. It is a status
label (`calculated` or `notModelled`), not a numeric series.

## Base-demand externalisation

Sweep points frequently share an identical base-demand series, because varying a cost parameter or
a storage limit does not change demand at all. Rather than repeating that series inside every
point's result, a sweep run externalises it: the first point to produce a given series writes it
once to `series/base-demand-{sha256}.json`, where the hash is computed from the serialised series
itself, and every point whose demand series is byte-identical simply references that file's path.
A run also prunes series files nothing in the freshly-written index still references, so the
series directory does not accumulate stale content-addressed files across regenerations.

## Provenance and reproducibility

Every dispatch result records the exact input artifacts it consumed, whether that result is a
scenario run, a region within one, or a sweep point. For each of the demand and weather inputs it
records the filename, the schema version, and the SHA-256 digest of the exact bytes that were
parsed (`DispatchInputArtifactDTO` in `NEM.Contracts/DispatchResultsDTO.cs`). The digest, not the
configured file path, is the reproducibility boundary: a path can be overwritten with different
content later, but the digest identifies the bytes that actually produced this result.

A sweep's `index.json` additionally records the git commit SHA the model was built from when the
sweep ran, and a flag for whether the working tree had uncommitted changes at that time. A sweep
result therefore states not just which input bytes it read but which version of the model produced
it.

## Writing conventions

Published artifacts follow the conventions in `NEM.CLI/Infrastructure/JsonFile.cs`:

- Property names are camelCase.
- Object keys are sorted ordinally, so a rerun that changes one value produces a small, reviewable
  diff rather than a reordered file.
- Numeric values are rounded according to their unit, so `*Mw` and `*Mwh` fields carry one decimal
  place, AUD fields two, and fractional shares four, rather than full floating-point precision that
  no one reads.
- Published artifacts are written unindented. Indentation was roughly seventy percent of the bytes
  in these files, and nothing ever read the whitespace: not the CLI, not the site, not a person.

### What changes between two identical reruns

Rerun a scenario against unchanged inputs at the same commit and every modelled value is
reproduced exactly. The artifacts are not byte-identical, though, and what differs depends on the
artifact:

| Artifact | Differs between identical reruns |
|---|---|
| `results*.json` | `runId` only, a fresh GUID per run |
| `demand-{region}.json`, `weather-{region}.json`, `generation-information.json` | `generatedAt`, a UTC timestamp stamped at import |
| `sweeps/{sweepId}/index.json` | Each point's measured `durationMs`, and the sweep's `totalDurationMs` when recorded |

The `runId`-only guarantee was verified by regenerating all twelve committed dispatch artifacts and
diffing them leaf by leaf. It applies to the dispatch artifacts; it does not extend to the imported
inputs or to the sweep index, which record when and how long a run took.

### Atomic publication

Publishing a **scenario** result is atomic: the new files are staged in a temporary directory, the
previous versions of the target files are moved aside, and only then are the staged files moved
into place. If anything fails partway through, the move is rolled back and the previous artifacts
are restored, so a failed run never leaves a half-written result where a complete one used to be
(`DispatchResultsExport.WritePublication`).

A **sweep** has no equivalent guarantee at the sweep level. Each point's dispatch result lands
through that same staged path, but it is then rewritten in place to externalise its base-demand
series, and the generated configs, per-point status files, the sweep index and the manifest are
written incrementally as the run proceeds. An interrupted sweep can therefore leave a partially
updated `sweeps/{sweepId}/` directory. Rerunning the sweep restores it.

## These files are generated

Everything under `NEM.Web/wwwroot/data` is a committed, generated artifact. Do not hand-edit it.
Regenerate it by rerunning the command that produced it (`--ingest` for the input artifacts,
`--run-scenario` or `--run-sweep` for results), so the file on disk stays traceable to the inputs
and commit that produced it.
