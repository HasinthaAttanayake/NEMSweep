# CLI reference

`NEMSweep.CLI` turns input bundles into versioned demand and weather artifacts, validates scenario/sweep inputs, and runs them through the framework. Bundle format 2 and scenario schema 7 accept portable region IDs; legacy bundle format 1 and some single-source import commands remain NEM-specific. The framework (`NEMSweep.Model`, `NEMSweep.Contracts`) has no command line. It dispatches hourly on one fixed market-time offset; ingestion resamples the half-hour demand artifact to that timestep.

`NEMSweep.CLI` is a single executable with no options parser: each command is a flag literal followed
by zero to three positional arguments, matched by a pattern in
`NEMSweep.CLI/Application/CommandRouter.cs`. This page documents every command it routes.

Install the version you want to reproduce as a .NET tool, which puts `nemsweep` on your path:

```bash
dotnet tool install --global NEMSweep.CLI --version 0.3.0
```

From a clone, the equivalent of every `nemsweep` invocation below is
`dotnet run --project NEMSweep.CLI -- ...`.

This page targets 0.3.0, including `--agent-guide`, `--points` and `--fail-on-unmet`.
Installation requires package publication; until then use this source revision
(see the [release target notice](https://github.com/HasinthaAttanayake/NEMSweep#install)).
Check `--help` and `--version` rather than assuming an installed tool or `latest`
container includes every command documented here.

Paths you pass on the command line are resolved relative to your **current working directory**
(`WorkspacePaths.ResolveConfiguredPath`). An absolute path is used as given. Nothing searches for a
repository, so the executable runs from wherever you put it.

## The workspace

Every run reads inputs from one directory and writes results to another. Neither is discovered:
you supply them, and each takes the first of three sources.

| Root | What it holds | Command line | Environment | Setting |
|---|---|---|---|---|
| Data | Demand, weather and generation artifacts a scenario reads. Also where `--ingest` writes them. | `--data-root <dir>` | `NEMSWEEP_DATA_ROOT` | `dataRoot` |
| Output | `results*.json` and published sweep artifacts. Fan-out configs remain under the working directory. | `--output <dir>` | `NEMSWEEP_OUTPUT` | `outputRoot` |

The environment variables exist because a container's settings file sits in a read-only image
layer, so a flag or a variable is the only way to redirect a run there.

The shipped `inputBundleRoot` setting is `data/nemsweep-inputs-v2` for current format-2 demand/weather
bundles. `--validate-inputs` and `--ingest` use it when no bundle path is supplied. The former
`data/nemsweep-inputs` bundle is legacy format 1 and remains supported when explicitly selected or
configured in `appsettings.local.json`; local settings still take precedence over shipped defaults.
See [Input bundles](input-bundles.md) for the layouts and non-destructive migration steps.

Two further flags are stripped the same way, and apply to the runs that can assess reliability:

| Flag | Applies to | Meaning |
|---|---|---|
| `--fail-on-unmet` | `--run-scenario`, `--run-sweep` | Exit `1` when the reliability target was not met. Without it such a run exits `0`, because the run itself completed. |
| `--points <id,id,...>` | `--run-sweep` | Run only the named sweep points. |

Passing either to a command that does not act on it is an error rather than a no-op: a caller who
asked for a subset of points, or for a missed target to fail the run, and silently got neither has
no way to tell from the exit code.

Both overrides may appear anywhere on the command line. They are stripped before the command itself
is matched, so they never occupy one of a command's positional arguments:

```bash
nemsweep --run-scenario scenarios/my-scenario.json --output ./study
```

```bash
nemsweep --output ./study --run-scenario scenarios/my-scenario.json
```

Two more options ride alongside them. `--format json` makes [`--validate-scenario`](#--validate-scenario)
report its answer as one JSON object rather than prose, for a caller that acts on it instead of
reading it; it is the only command that reports an answer rather than writing artifacts, so it is
the only one the option applies to. And `--csv` asks a run to also write its results as a star schema
of CSV tables, which is what you want if the result is going anywhere other than back into NEMSweep:

```bash
nemsweep --run-scenario scenarios/my-scenario.json --output ./study --csv
```

The JSON is written exactly as before either way. See [CSV tables](csv-tables.md).

`--help`, `--version`, `--describe-schema` and `--quick-start` work without loading local settings.
Quick-start owns a new study directory and rejects all global workspace/format overrides.
The other workspace-free commands work before
you have written any settings file.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | The command line was recognised and ran, but the command itself failed. |
| `2` | The command line could not be read: no command matched it, or an option was missing its value. |

## Help and version

```bash
dotnet run --project NEMSweep.CLI -- --help
```

`-h` and `--usage` are accepted as synonyms. Usage text is written to standard output and the
command returns `0`, because asking for help is a success rather than an unrecognised command
line.

```bash
dotnet run --project NEMSweep.CLI -- --version
```

Prints the assembly's informational version (falling back to its assembly version, or `unknown`).

## Hosted quick-start

```powershell
nemsweep --quick-start
nemsweep --quick-start simple .\my-first-study
nemsweep --quick-start advanced .\my-nem-study
```

Explains Simple (Island-X) and Advanced (five connected NEM regions), prompts
when no choice is supplied, or uses the explicit selection for automation.
Simple uses synthetic data; Advanced preserves real Australian NEM FY2026 AEMO
demand and original TMYx 2011-2025 EPWs (not measured FY2026 weather).
Downloads a pinned format-2 bundle, verifies length/hash, prepares
current-spec configurations and automatically ingests the data. **No simulations
or sweeps are run.** The destination must not already exist, including as an
empty directory; failures leave no partially prepared study.

With no destination, uses `nemsweep-starter-simple` or `nemsweep-starter-advanced`
under the current directory. Redirected stdin requires explicit selection.
Preparation is settings-independent and rejects `--data-root`, `--output`,
`--csv` and `--format`; later study runs accept them normally.

Simple is the smallest starter, a synthetic one-region Island-X grid; Advanced
is the NEM worked example. See [Hosted starters](synthetic-starters.md) for
download safety, assumptions and run instructions.

## Scenario and sweep runs

### `--run-scenario`

```bash
dotnet run --project NEMSweep.CLI -- --run-scenario
```

```bash
dotnet run --project NEMSweep.CLI -- --run-scenario scenarios/my-scenario.json
```

| Argument | Optional | Default |
|---|---|---|
| scenario config path | yes | `defaultScenarioPath` from the loaded CLI settings |

Reads a scenario configuration and dispatches every region it describes over the modelled year,
sizing storage against the scenario's reliability standard. Writes `results.json`,
`results-overview.json`, and a `results-{region}.json` and `results-{region}-overview.json` pair per
region.

Results are published to the **output root**, and the demand and weather artifacts the scenario
names are read from the **data root**. Point either wherever you need it:

```bash
nemsweep --run-scenario scenarios/my-scenario.json --data-root ./reference --output ./study
```

A scenario names its inputs by file name, and the data root is the single place they are looked
for. An absolute path in a scenario config is used as given.

Publication is staged and atomic: the previous versions of these files are moved aside first and
only deleted once every new file has landed, so a failure partway through leaves the prior
artifacts in place rather than a half-written set.

If the reliability target is not met, the command still exits `0` and prints a `WARNING` line
naming the achieved and target unserved-energy percentages.

### `--validate-scenario`

```bash
dotnet run --project NEMSweep.CLI -- --validate-scenario scenarios/my-scenario.json
```

```bash
dotnet run --project NEMSweep.CLI -- --validate-scenario scenarios/my-scenario.json --format json
```

| Argument | Optional | Default |
|---|---|---|
| scenario config path | yes | `defaultScenarioPath` from the loaded CLI settings |

Loads a scenario configuration and reports whether it is valid. It **writes no artifacts**: the
report goes to standard output and nothing is created on disk, so it is safe to run against a
directory you do not want touched. Exit code `0` if it loaded, `1` if it did not. Use it before `--run-scenario` while you are editing a config: an
invented field name then costs a parse rather than a full dispatch.

With `--format json` the answer is a single object, which is what a caller correcting a generated
config wants:

```json
{"valid":false,"path":"scenarios/mine.json","error":{"stage":"Input","code":"invalidConfig","message":"The JSON property 'invented' could not be mapped ..."}}
```

The message names the offending property, because deserialisation rejects unknown fields rather
than ignoring them. That strictness is what makes a mistake loud, and it is why the schema matters.

### `--fan-out-sweep`

```bash
dotnet run --project NEMSweep.CLI -- --fan-out-sweep sweeps/my-sweep.json
```

| Argument | Optional | Default |
|---|---|---|
| sweep definition path | no | n/a |

Reads a sweep definition and, for each point, merges the point's overrides onto the baseline
scenario config to produce `sweeps/{sweepId}/configs/{pointId}.json` under your working directory.
That is a working area beside the sweep definition, deliberately separate from the published sweep
directory: a run copies each config from here to there, and one location serving both would have it
copy a file onto itself.
Every generated point config is then loaded and validated as a scenario config before the command
succeeds. This makes `--fan-out-sweep` the command to run first while you are developing a sweep:
a bad override surfaces immediately, against every point, without running any dispatch.

It also prints the point-to-axis-value table and writes it as
`sweeps/{sweepId}/configs/points.csv`, because a point ID encodes nothing about where that point
sits on the axis and the mapping is otherwise only in the definition the caller just supplied. See
[Mapping point IDs to axis values](sweeps.md#mapping-point-ids-to-axis-values).

### `--run-sweep`

```bash
dotnet run --project NEMSweep.CLI -- --run-sweep sweeps/my-sweep.json
```

| Argument | Optional | Default |
|---|---|---|
| sweep definition path | no | n/a |

| Flag | Meaning |
|---|---|
| `--points <id,id,...>` | Run only these points. An ID the definition does not declare is an error, and the message lists the ones it does. |
| `--fail-on-unmet` | Exit `1` when any point missed its reliability target, naming those points on standard error. |

Fans out the same point configs as `--fan-out-sweep`, then runs `--run-scenario` for each point
in turn, validating each through the scenario loader. A point that fails does not stop the sweep: its
failure is recorded in that point's `points/{pointId}.status.json` and the sweep continues with the
remaining points. The command's own exit code is `1` if any point failed, `0` otherwise. Because
validation here is per point, a config mistake surfaces as a recorded per-point failure rather
than aborting the entire sweep upfront. Run `--fan-out-sweep` first while developing a definition
to inspect and validate its generated configs without dispatching.

Unlike a scenario publication, a sweep is written incrementally: configs, per-point results and
status files, the sweep index and the manifest each land as they are produced. An interrupted sweep
can therefore leave a partially updated `sweeps/{sweepId}/` directory. Rerun the sweep to bring it
back to a consistent state.

Published under `{outputRoot}/sweeps/{sweepId}/`: `index.json` (the sweep index),
`points/{pointId}.json` and `points/{pointId}.status.json` for each point, a copy of each point's
`configs/{pointId}.json`, and any base-demand series the points reference under `series/`.
`sweeps/index.json` (the manifest of all published sweeps) is rewritten from what is on disk after
every full run. A full run also writes `summary.json` and `summary.csv` in the sweep directory and
prints the same rows as a table; see
[The summary](sweeps.md#the-summary-what-the-sweep-found).

**Selecting fewer than all defined points makes the run partial.** Selecting every point with
`--points` is still a full run. A partial run leaves the sweep's aggregate artifacts alone: it
publishes each selected point's result, status and config, and does not rewrite `index.json`, the
`sweeps/index.json` manifest, or the summary files: rebuilding the index from a subset would drop
every point the run did not touch. The run says so on stdout, and the summary table still prints
for the points that ran. Rerun without `--points` to republish the sweep.

### `--describe-schema`

```bash
dotnet run --project NEMSweep.CLI -- --describe-schema scenario
```

```bash
dotnet run --project NEMSweep.CLI -- --describe-schema sweep
```

| Argument | Optional | Default |
|---|---|---|
| format: `manifest`, `legacy-manifest`, `demand`, `weather`, `scenario` or `sweep` | no | n/a |

Prints the selected versioned JSON Schema (draft 2020-12) to standard output. The formats cover current/legacy bundle manifests, ingested demand/weather artifacts, and scenario/sweep configs. See [Schemas](schemas.md) for versions and compatibility. Hand a schema to a validation tool or an LLM that generates inputs.

### `--new-scenario`

```bash
dotnet run --project NEMSweep.CLI -- --new-scenario > scenarios/mine.json
```

| Argument | Optional | Default |
|---|---|---|
| none | n/a | n/a |

Prints a compact fixed-fleet scenario: one region, one generating fleet and an empty `storageFleets` array. It includes the required reliability target and omits `storageSizing`. Written to standard output rather than to a file, so it composes with a redirect and never overwrites anything by surprise. Like `--describe-schema` it needs no workspace, so it
works before you have written a settings file.

The published `scenarios/nem-fy2026-all-regions.json` stays the worked example of a real system.
`scenarios/starter-nsw1.json` sits between the two: the same NSW1 assets on their own, short enough
to read in one go.

### `--agent-guide`

```bash
nemsweep --agent-guide
```

| Argument | Optional | Default |
|---|---|---|
| none | n/a | n/a |

Prints the agent guide that was embedded in this build, which is the repository's
[`skills/nemsweep/SKILL.md`](https://github.com/HasinthaAttanayake/NEMSweep/blob/main/skills/nemsweep/SKILL.md).

Embedded rather than fetched, deliberately. An agent that reads the copy on `main` is reading about
a version it may not have installed: the install commands, the flags and the field names all move
between releases. The embedded copy can only describe the binary printing it. Like
`--describe-schema` it needs no workspace.

```bash
nemsweep --agent-guide > AGENT-NOTES.md
```

## Input bundles

An input bundle is a folder of source data (demand archives and EPW weather files) plus a `manifest.json`. Current format 2 has no generation-workbook dependency; see [Input bundles](input-bundles.md) for the format details.

### `--validate-inputs`

```bash
dotnet run --project NEMSweep.CLI -- --validate-inputs
```

```bash
dotnet run --project NEMSweep.CLI -- --validate-inputs path/to/input-bundle
```

| Argument | Optional | Default |
|---|---|---|
| input bundle path | yes | `inputBundleRoot` from the loaded CLI settings |

Loads and validates demand and weather sources, plus the generation-information workbook when a legacy format-1 bundle requires it. It prints a validity summary and writes nothing, so it is safe to run against a bundle you have not committed to yet.

### `--ingest`

```bash
dotnet run --project NEMSweep.CLI -- --ingest
```

```bash
dotnet run --project NEMSweep.CLI -- --ingest path/to/input-bundle
```

| Argument | Optional | Default |
|---|---|---|
| input bundle path | yes | `inputBundleRoot` from the loaded CLI settings |

Runs the identical validation `--validate-inputs` performs, then writes one `demand-{region}.json` and one `weather-{region}.json` per region under the **data root**. Legacy format 1 also writes `generation-information.json`; format 2 has no workbook or generation-information artifact. If validation fails, `--ingest` writes nothing.

`--ingest` supersedes `--import-demand`, `--generation-information` and `--epw-report`: one bundle,
one pass, and it produces the same artifacts those three single-source commands produce
individually. Reach for the single-source commands only when you are iterating on one input and
do not want to reprocess the rest of the bundle each time.

## Single-source imports

Each of these imports one kind of source data directly, without requiring a full input bundle.
They are covered by `--ingest`; use them when iterating on a single input.

### `--import-demand`

```bash
dotnet run --project NEMSweep.CLI -- --import-demand
```

```bash
dotnet run --project NEMSweep.CLI -- --import-demand path/to/output-directory
```

| Argument | Optional | Default |
|---|---|---|
| output directory | yes | the data root |

Reads operational-demand archives from the input bundle at `inputBundleRoot` and writes
`demand-{region}.json` for each region found, to the given output directory, or to the data root
when you do not name one. Like every other path on the command line, an explicit directory here is
resolved relative to your current working directory.

### `--generation-information`

```bash
dotnet run --project NEMSweep.CLI -- --generation-information path/to/workbook.xlsx
```

| Argument | Optional | Default |
|---|---|---|
| workbook path | no | n/a |

Reads a generation-information workbook and writes `generation-information.json` under the data
root. The workbook path is relative to your current working directory unless it is absolute.

### `--epw-report`

```bash
dotnet run --project NEMSweep.CLI -- --epw-report NSW1 path/to/solar.epw
```

```bash
dotnet run --project NEMSweep.CLI -- --epw-report NSW1 path/to/solar.epw path/to/wind.epw
```

| Argument | Optional | Default |
|---|---|---|
| region | no | n/a |
| solar EPW path | no | n/a |
| wind EPW path | yes | the solar EPW path (one file used for both) |

The region argument may be any nonblank portable region ID that can be represented in an artifact filename; known NEM IDs retain their lowercase names and custom IDs use the portable encoded filename. The EPW paths are relative to your current working directory unless they are absolute. Writes `weather-{region}.json` under the data root, and prints
the provenance report, the daylight DNI source shares and their total, and a count of each series
constructed.
