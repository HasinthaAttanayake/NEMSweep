# Hosted starters

Start with a synthetic island grid or the Australian NEM's original input data.
The separate public [NEMSweep.Samples repository](https://github.com/HasinthaAttanayake/NEMSweep.Samples)
provides generation recipes and release provenance for two raw format-2 bundles.
The CLI prepares their configurations using the current scenario and sweep specifications.

> **Publication status:** the reviewed [data release, `v2026.10.1`](https://github.com/HasinthaAttanayake/NEMSweep.Samples/releases/tag/v2026.10.1)
> is published, and CLI quick-start ships in `NEMSweep.CLI` 0.2.0 and later.
> Publishing the data does not itself publish a new software version, so the two
> carry separate version numbers.

## Choose a starter

**Simple is the smallest.** One synthetic region, a few fleets, and runs that finish in seconds:
reach for it to confirm an install works or to try a recipe. **Advanced is the NEM worked example**,
and a sized run over its five regions takes about a minute per point.

| Starter | Regions and period | Configurations |
|---|---|---|
| Simple (smallest) | Island-X, calendar 2025, fixed UTC+10 | Readable fixed/sized scenarios and a two-point mode sweep. Demand is 60 MW during 08:00-20:00 and 30 MW otherwise. |
| Advanced (NEM worked example) | NSW1, QLD1, SA1, TAS1, VIC1, FY2026, fixed UTC+10 | Existing NEM example fleet, cost, technology and directed-link assumptions; fixed/sized scenarios, a two-point mode sweep and the full 25-point data-centre sweep. |

**Simple is synthetic. Advanced preserves the original real inputs.** Advanced
contains the real AEMO operational-demand archives for Australia's NEM FY2026
(1 July 2025-30 June 2026), and all seven original TMYx 2011-2025 EPW files,
including separate solar/wind sites for NSW1 and QLD1. Their bytes, filenames and
role mappings are preserved. EPW typical-year profiles are assembled from
historical records, **not weather measured during FY2026**. Original EPW headers
attribute NCEI ISD/ERA5 records and Climate.Onebuilding.org.
SA1's original EPW uses UTC+09:30; the other six use UTC+10. Existing ingestion
normalizes profiles to the run's fixed UTC+10 offset without editing raw sources.

Both starters supply example scenario assumptions, not forecasts, calibrated
models or certified results. Advanced preserves the existing fleet/cost/link
assumptions, not published simulation results. Do not change a reliability
target or fleet merely to make an example pass.

The corrected source-preserving recipe was reviewed and merged in
[samples PR #2](https://github.com/HasinthaAttanayake/NEMSweep.Samples/pull/2).
The published release includes the [third-party attribution notice](https://github.com/HasinthaAttanayake/NEMSweep.Samples/blob/main/LICENSE.md#third-party-advanced-input-data),
with the EPW citation and original publisher download links. The recipe's BSD
license does not relicense the third-party demand/weather datasets.

## Prepare using the CLI

With the published tool installed
(`dotnet tool install --global NEMSweep.CLI --version 0.3.0`, once published;
see the [release target notice](https://github.com/HasinthaAttanayake/NEMSweep#install)):

```bash
nemsweep --quick-start
```

The bare command explains both starters and prompts for `1`/`simple` or
`2`/`advanced`. For automation, select explicitly:

```bash
nemsweep --quick-start simple ./my-first-study
```

```bash
nemsweep --quick-start advanced ./my-nem-study
```

From a clone, `dotnet run --project NEMSweep.CLI -- --quick-start simple ./my-first-study` is the
equivalent. Both flows explain the chosen starter and that preparation does not run a model.
Redirected stdin requires explicit selection; EOF ends selection instead of choosing silently.

Omitting the destination creates `nemsweep-starter-simple` or
`nemsweep-starter-advanced` in the current directory. The destination must not
exist, **even as an empty directory**. No existing study is overwritten.

Quick-start does not load local settings or use environment-root overrides.
`--data-root`, `--output`, `--csv` and `--format` are rejected for preparation.
They remain available for the later scenario/sweep runs.

Preparation:

1. Downloads the pinned public HTTPS asset, with no GitHub account or token.
2. Verifies its exact byte length and SHA-256 before extraction.
3. Extracts and validates the raw bundle, then reuses normal ingestion.
4. Materialises current-spec fixed/sized scenarios and sweeps with relative paths,
   and validates all generated sweep-point configurations without dispatching.
5. Moves the complete study into its destination only after success.

The downloader has a two-minute preparation timeout, a 32 MiB download ceiling,
a 128 MiB extracted-data ceiling and a 256-entry ZIP ceiling. It rejects links,
traversal, nonportable filenames and case-insensitive path collisions. Download,
validation or cancellation failures remove owned staging files and leave no
partially prepared study; any cleanup error is reported explicitly. Existing
input bundles are never used or modified.

## Run deliberately

The resulting directory contains:

```text
my-study/
  README.md
  bundle/       raw format-2 manifest, demand ZIP and EPWs
  data/         ingested per-region demand/weather artifacts
  scenarios/    fixed, sized and mode-sweep baseline configurations
  sweeps/       demo definition and validated point configs
                plus datacentre-full.json for Advanced
```

Both starters prepare the same three entry points, so these commands are the same either way. Run
them from the study root; `cd ./my-first-study` first.

```bash
nemsweep --run-scenario scenarios/fixed-scenario.json --data-root ./data --output ./out/fixed
```

Sizing and sweeps are separate opt-in actions:

```bash
nemsweep --run-scenario scenarios/sized-scenario.json --data-root ./data --output ./out/sized
```

```bash
nemsweep --run-sweep sweeps/demo.json --data-root ./data --output ./out/demo --csv
```

The fixed scenario uses exactly its declared fleet; `sizeBattery` can add battery
storage within configured bounds. It does not size pumped hydro.

### The Simple sweep's `fixed` point is not the standalone fixed scenario

Worth knowing before you compare the two, because the names suggest otherwise. Simple's
`scenarios/fixed-scenario.json` declares **80 MW** of Gas. Its `scenarios/mode-sweep-baseline.json`,
which `sweeps/demo.json` patches, declares **50 MW** of Gas and a zero-capacity Battery carrying
the sizing assumptions, the same as `scenarios/sized-scenario.json`. So the sweep's `fixed` point
dispatches the 50 MW fleet unchanged, not the 80 MW one.

That is deliberate: the mode sweep varies one thing, `executionMode`, against one baseline, which
is what makes the two points comparable. It matches the
[custom-grid example](custom-grids.md). If you want the 80 MW configuration in a sweep, point
`baselineConfigPath` at `scenarios/fixed-scenario.json` instead.

Advanced's optional `sweeps/datacentre-full.json` preserves all 25 original axis
values and region overrides. It uses the sized baseline and may be expensive: about a minute per
point on ordinary hardware. Quick-start **never runs it**. A valid prepared study does not guarantee
that every scenario meets its declared reliability target.

Move the entire study as needed, but always run from its root: sweep baseline
paths resolve relative to the working directory, not the sweep file directory.

## Download or regenerate manually

The samples [release page](https://github.com/HasinthaAttanayake/NEMSweep.Samples/releases/tag/v2026.10.1)
provides `simple-v2.zip`, `advanced-v2.zip` and SHA256 checksums in its notes. The raw ZIPs have
`manifest.json`, `demand/` and `weather/` at their root; they contain no generation
workbook, executable setup scripts or scenario configurations. See the samples
repository for the reviewed recipe, pinned hashes and local regeneration
instructions.

Extract either archive into a new bundle directory and use the normal commands:

```bash
nemsweep --validate-inputs ./bundle
```

```bash
nemsweep --ingest ./bundle --data-root ./data
```

Manual ingestion provides data, not the complete CLI-generated study templates.
For a locally generated full example without downloading the release, use the existing
[custom-grid recipe](custom-grids.md). These routes remain supported alongside
quick-start.

Raw [input bundle formats](input-bundles.md) and [JSON schemas](schemas.md) are
independent of dataset revision `v2026.10.1`. Never replace bytes at a published
revision. The CLI catalogue pins exact release bytes, while configuration templates
are embedded with the CLI and migrate the existing scenario source through shared
validation. No model/output schema changes are introduced by quick-start.
