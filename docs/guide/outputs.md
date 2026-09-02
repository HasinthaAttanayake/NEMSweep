# Outputs and provenance

This page is for reading the JSON NEMSweep produces, or consuming it programmatically. Every
artifact described here is written by `NEMSweep.CLI`: the framework itself returns objects, not
files. Results and sweeps go under the **output root**, and the imported inputs a scenario reads go
under the **data root**. Both are yours to choose; see the
[CLI reference](cli.md#the-workspace).

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

Read `NEMSweep.CLI/Scenarios/DispatchResultsExport.cs` and `NEMSweep.CLI/Scenarios/SweepArtifactExport.cs` if
you need the exact shape beyond what this page covers.

## Schema versions

Every artifact carries a `schemaVersion` field, and the current value for each artifact type is
defined in one place: `NEMSweep.Contracts/ArtifactSchemaVersions.cs`. This page does not repeat those
numbers, because they would go stale the moment a schema changes. Consult the
[API reference](../api/index.md) for the current values.

## Sweep scalar vocabulary

A sweep publishes one set of scalars per point per region (and one for the system as a whole), in
`sweeps/{sweepId}/index.json` and alongside each point's full result. The catalogue in
`NEMSweep.Contracts/SweepScalarCatalog.cs` is the full vocabulary: the JSON name each scalar is emitted
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
parsed (`DispatchInputArtifactDTO` in `NEMSweep.Contracts/DispatchResultsDTO.cs`). The digest, not the
configured file path, is the reproducibility boundary: a path can be overwritten with different
content later, but the digest identifies the bytes that actually produced this result.

A whole-system result also records the model build behind it, in a `provenance` block carrying the
git commit SHA and a flag for whether the working tree had uncommitted changes. A sweep `index.json`
records the same for the sweep as a whole. Digests pin the bytes a run consumed, but publishing a
result is a manual step, so without the commit a file copied somewhere else could not say which
version of the model read them.

The commit is the one the binary was built at, stamped in by its build, not whatever commit the
directory you ran from happens to be standing on. That distinction matters once the CLI is installed
or run as a container, where the two are unrelated. The dirty flag is only ever true when the run
was made from a checkout standing on that same commit, because that is the only case where the
source the binary was built from is in front of the tool to inspect. The block is absent when the
binary was built outside a checkout and so carries no commit to report.

Provenance paths in a sweep index are recorded relative to the data root, or to the working
directory for the sweep definition and baseline config, which is what keeps them citing
`sweeps/…` and `scenarios/…` rather than a location on one machine. The digest remains the
reproducibility boundary; the path is there to say where the run was configured from.

## Writing conventions

Published artifacts follow the conventions in `NEMSweep.CLI/Infrastructure/JsonFile.cs`:

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

Every file described here is generated. Do not hand-edit one. Regenerate it by rerunning the
command that produced it (`--ingest` for the input artifacts, `--run-scenario` or `--run-sweep` for
results), so what is on disk stays traceable to the inputs and commit behind it.

The published artifact set is what the results site displays. Publishing to it is a deliberate act:
use the site's publication workflow when you intend to update it, and let ordinary runs land in your
own directory the rest of the time.

**That set is an illustrative example, not a dataset.** It is one FY2026 run and one sweep, retained
so the site has something to show and so a clone runs without first sourcing upstream data. The
provenance block on it is real, which is exactly why it is worth saying plainly: a demo carrying
input digests and a commit can read as more authoritative than it is. Run your own scenario before
quoting a figure, and see [DATA-LICENSE.md](https://github.com/HasinthaAttanayake/NEMSweep/blob/main/DATA-LICENSE.md)
for the terms on the data behind it.

## CSV

Everything on this page is JSON, which is the contract but not something you can open. Add `--csv`
to a run and it also writes a star schema of CSV tables projected from these same values, rounded
by the same rule. See [CSV tables](csv-tables.md).
