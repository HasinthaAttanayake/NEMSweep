# CLI reference

`NEMSweep.CLI` is a single executable with no options parser: each command is a flag literal followed
by zero to three positional arguments, matched by a pattern in
`NEMSweep.CLI/Application/CommandRouter.cs`. This page documents every command it routes.

Paths you pass on the command line are resolved relative to your **current working directory**
(`WorkspacePaths.ResolveConfiguredPath`). An absolute path is used as given. Nothing searches for a
repository, so the executable runs from wherever you put it.

## The workspace

Every run reads inputs from one directory and writes results to another. Neither is discovered:
you supply them, and each takes the first of three sources.

| Root | What it holds | Command line | Environment | Setting |
|---|---|---|---|---|
| Data | Demand, weather and generation artifacts a scenario reads. Also where `--ingest` writes them. | `--data-root <dir>` | `NEMSWEEP_DATA_ROOT` | `dataRoot` |
| Output | `results*.json` and everything under `sweeps/`. | `--output <dir>` | `NEMSWEEP_OUTPUT` | `outputRoot` |

The environment variables exist because a container's settings file sits in a read-only image
layer, so a flag or a variable is the only way to redirect a run there.

Both overrides may appear anywhere on the command line. They are stripped before the command itself
is matched, so they never occupy one of a command's positional arguments:

```bash
nemsweep --run-scenario scenarios/my-scenario.json --output ./study
```

```bash
nemsweep --output ./study --run-scenario scenarios/my-scenario.json
```

One more option rides alongside them. `--csv` asks a run to also write its results as a star schema
of CSV tables, which is what you want if the result is going anywhere other than back into NEMSweep:

```bash
nemsweep --run-scenario scenarios/my-scenario.json --output ./study --csv
```

The JSON is written exactly as before either way. See [CSV tables](csv-tables.md).

`--help`, `--version` and `--describe-schema` answer without a workspace at all, so they work before
you have written any settings file.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | The command line was recognised and ran, but the command itself failed. |
| `2` | The command line could not be routed to any command. |

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

### `--run-sweep`

```bash
dotnet run --project NEMSweep.CLI -- --run-sweep sweeps/my-sweep.json
```

| Argument | Optional | Default |
|---|---|---|
| sweep definition path | no | n/a |

Fans out the same point configs as `--fan-out-sweep`, but **without** validating them first, then
runs `--run-scenario` for each point in turn. A point that fails does not stop the sweep: its
failure is recorded in that point's `points/{pointId}.status.json` and the sweep continues with the
remaining points. The command's own exit code is `1` if any point failed, `0` otherwise. Because
fan-out here skips validation, a config mistake surfaces as a per-point failure buried in the
sweep's results rather than an upfront error. Run `--fan-out-sweep` on the same definition first to
catch that class of mistake before spending the time on a full run.

Unlike a scenario publication, a sweep is written incrementally: configs, per-point results and
status files, the sweep index and the manifest each land as they are produced. An interrupted sweep
can therefore leave a partially updated `sweeps/{sweepId}/` directory. Rerun the sweep to bring it
back to a consistent state.

Published under `{outputRoot}/sweeps/{sweepId}/`: `index.json` (the sweep index),
`points/{pointId}.json` and `points/{pointId}.status.json` for each point, a copy of each point's
`configs/{pointId}.json`, and any base-demand series the points reference under `series/`.
`sweeps/index.json` (the manifest of all published sweeps) is rewritten from what is on disk after
every run.

### `--describe-schema`

```bash
dotnet run --project NEMSweep.CLI -- --describe-schema scenario
```

```bash
dotnet run --project NEMSweep.CLI -- --describe-schema sweep
```

| Argument | Optional | Default |
|---|---|---|
| format: `scenario` or `sweep` | no | n/a |

Prints a JSON Schema (draft 2020-12) for the requested format to standard output. This is the
machine-readable contract for a scenario config or a sweep definition. Hand it to a validation
tool, or to an LLM you want to generate scenarios or sweeps for you.

## Input bundles

An input bundle is a folder of source data (demand archives, EPW weather files and a generation
workbook) plus a `manifest.json`.

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

Loads and validates the demand, weather and generation-information data in the bundle, and prints
a validity summary for each. **This command writes nothing at all.** It is read-only by design, so
it is safe to run against a bundle you have not committed to yet.

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

Runs the identical validation `--validate-inputs` performs, then, only if that validation
succeeds, writes every artifact: `demand-{region}.json` per region, `weather-{region}.json` per
region, and `generation-information.json`, all under the **data root**. Ingest produces the
artifacts scenarios read, so it writes where they are read from; only results belong under the
output root. If validation fails, `--ingest`
writes nothing, which is the same guarantee `--validate-inputs` gives you with the write step
appended when the bundle is good.

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

The region argument is validated against the five NEM regions (`NSW1`, `QLD1`, `SA1`, `TAS1`,
`VIC1`); anything else is rejected before any file is read, and before settings are loaded. The EPW
paths are relative to your current working directory unless they are absolute. Writes
`weather-{region}.json` under the data root, and prints the provenance report, including the
daylight DNI source shares, along with a count of each series constructed.

