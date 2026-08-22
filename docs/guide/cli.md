# CLI reference

`NEM.CLI` is a single executable with no options parser: each command is a flag literal followed
by zero to three positional arguments, matched by a pattern in
`NEM.CLI/Application/CommandRouter.cs`. This page documents every command it routes.

Paths you pass on the command line — a scenario config, a sweep definition, an input bundle — are
resolved relative to the **solution root**, not to your current working directory
(`RepositoryPaths.ResolveConfiguredPath`). An absolute path is used as given.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | The command line was recognised and ran, but the command itself failed. |
| `2` | The command line could not be routed to any command. |

## Help and version

```bash
dotnet run --project NEM.CLI -- --help
```

`-h` and `--usage` are accepted as synonyms. Usage text is written to standard output and the
command returns `0` — asking for help is a success, not an unrecognised command line.

```bash
dotnet run --project NEM.CLI -- --version
```

Prints the assembly's informational version (falling back to its assembly version, or `unknown`).

## Scenario and sweep runs

### `--run-scenario`

```bash
dotnet run --project NEM.CLI -- --run-scenario
```

```bash
dotnet run --project NEM.CLI -- --run-scenario scenarios/my-scenario.json
```

| Argument | Optional | Default |
|---|---|---|
| scenario config path | yes | `defaultScenarioPath` from the loaded CLI settings |

Reads a scenario configuration and dispatches every region it describes over the modelled year,
sizing storage against the scenario's reliability standard. Writes `results.json`,
`results-overview.json`, and a `results-{region}.json` / `results-{region}-overview.json` pair per
region, all under `outputRoot`. Publication is staged and atomic: the previous versions of these
files are moved aside first and only deleted once every new file has landed, so a failure partway
through leaves the prior artifacts in place rather than a half-written set.

If the reliability target is not met, the command still exits `0` and prints a `WARNING` line
naming the achieved and target unserved-energy percentages.

### `--fan-out-sweep`

```bash
dotnet run --project NEM.CLI -- --fan-out-sweep sweeps/my-sweep.json
```

| Argument | Optional | Default |
|---|---|---|
| sweep definition path | no | — |

Reads a sweep definition and, for each point, merges the point's overrides onto the baseline
scenario config to produce `sweeps/{sweepId}/configs/{pointId}.json` under the solution root.
Every generated point config is then loaded and validated as a scenario config before the command
succeeds. This makes `--fan-out-sweep` the command to run first while you are developing a sweep:
a bad override surfaces immediately, against every point, without running any dispatch.

### `--run-sweep`

```bash
dotnet run --project NEM.CLI -- --run-sweep sweeps/my-sweep.json
```

| Argument | Optional | Default |
|---|---|---|
| sweep definition path | no | — |

Fans out the same point configs as `--fan-out-sweep`, but **without** validating them first, then
runs `--run-scenario` for each point in turn. A point that fails does not stop the sweep: its
failure is recorded in that point's `points/{pointId}.status.json` and the sweep continues with the
remaining points. The command's own exit code is `1` if any point failed, `0` otherwise. Because
fan-out here skips validation, a config mistake surfaces as a per-point failure buried in the
sweep's results rather than an upfront error — run `--fan-out-sweep` on the same definition first
to catch that class of mistake before spending the time on a full run.

Published under `NEM.Web/wwwroot/data/sweeps/{sweepId}/`: `index.json` (the sweep index),
`points/{pointId}.json` and `points/{pointId}.status.json` for each point, a copy of each point's
`configs/{pointId}.json`, and any base-demand series the points reference under `series/`.
`sweeps/index.json` (the manifest of all published sweeps) is rewritten from what is on disk after
every run.

### `--describe-schema`

```bash
dotnet run --project NEM.CLI -- --describe-schema scenario
```

```bash
dotnet run --project NEM.CLI -- --describe-schema sweep
```

| Argument | Optional | Default |
|---|---|---|
| format: `scenario` or `sweep` | no | — |

Prints a JSON Schema (draft 2020-12) for the requested format to standard output. This is the
machine-readable contract for a scenario config or a sweep definition — hand it to a validation
tool, or to an LLM you want to generate scenarios or sweeps for you.

## Input bundles

An input bundle is a folder of source data — demand archives, EPW weather files, a generation
workbook — plus a `manifest.json`.

### `--validate-inputs`

```bash
dotnet run --project NEM.CLI -- --validate-inputs
```

```bash
dotnet run --project NEM.CLI -- --validate-inputs path/to/input-bundle
```

| Argument | Optional | Default |
|---|---|---|
| input bundle path | yes | `inputBundleRoot` from the loaded CLI settings |

Loads and validates the demand, weather and generation-information data in the bundle, and prints
a validity summary for each. **This command writes nothing at all** — it is read-only by design,
so it is safe to run against a bundle you have not committed to yet.

### `--ingest`

```bash
dotnet run --project NEM.CLI -- --ingest
```

```bash
dotnet run --project NEM.CLI -- --ingest path/to/input-bundle
```

| Argument | Optional | Default |
|---|---|---|
| input bundle path | yes | `inputBundleRoot` from the loaded CLI settings |

Runs the identical validation `--validate-inputs` performs, then, only if that validation
succeeds, writes every artifact: `demand-{region}.json` per region, `weather-{region}.json` per
region, and `generation-information.json`, all under `outputRoot`. If validation fails, `--ingest`
writes nothing — the same guarantee `--validate-inputs` gives you, just with the write step
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
dotnet run --project NEM.CLI -- --import-demand
```

```bash
dotnet run --project NEM.CLI -- --import-demand path/to/output-directory
```

| Argument | Optional | Default |
|---|---|---|
| output directory | yes | `outputRoot` from the loaded CLI settings |

Reads operational-demand archives from the input bundle at `inputBundleRoot` and writes
`demand-{region}.json` for each region found, to the given output directory (or `outputRoot`).
Unlike the scenario, sweep and input-bundle paths above, an explicit output directory here is
resolved relative to your current working directory, not the solution root.

### `--generation-information`

```bash
dotnet run --project NEM.CLI -- --generation-information path/to/workbook.xlsx
```

| Argument | Optional | Default |
|---|---|---|
| workbook path | no | — |

Reads a generation-information workbook and writes `generation-information.json` under
`NEM.Web/wwwroot/data`. The workbook path is used as given — relative to your current working
directory unless it is absolute — rather than resolved against the solution root.

### `--epw-report`

```bash
dotnet run --project NEM.CLI -- --epw-report NSW1 path/to/solar.epw
```

```bash
dotnet run --project NEM.CLI -- --epw-report NSW1 path/to/solar.epw path/to/wind.epw
```

| Argument | Optional | Default |
|---|---|---|
| region | no | — |
| solar EPW path | no | — |
| wind EPW path | yes | the solar EPW path (one file used for both) |

The region argument is validated against the five NEM regions (`NSW1`, `QLD1`, `SA1`, `TAS1`,
`VIC1`); anything else is rejected before any file is read. The EPW paths, like the
generation-information workbook path, are used as given rather than resolved against the solution
root. Writes `weather-{region}.json` and `weather-provenance.json` under `NEM.Web/wwwroot/data`,
and prints the provenance report — including the daylight DNI source shares — and a count of each
series constructed.

## EPW diagnostics

These commands inspect a single EPW file directly; none of them write anything. As with
`--generation-information`, the file path is used as given rather than resolved against the
solution root.

### `--epw-series`

```bash
dotnet run --project NEM.CLI -- --epw-series path/to/file.epw
```

Parses the file into its resource time series and prints the length of each series (GHI, DNI, DHI,
solar zenith, dry-bulb temperature, wind) plus the first timestamp.

### `--epw-validate`

```bash
dotnet run --project NEM.CLI -- --epw-validate path/to/file.epw
```

Runs the full structural validation used by every other EPW-reading command and prints the row
count and the distinct source years found.

### `--epw-gaps`

```bash
dotnet run --project NEM.CLI -- --epw-gaps path/to/file.epw
```

Prints the row count and a gap count. **The gap count is always zero**: the EPW parser throws an
exception the moment it encounters a gap, rather than returning one for this command to report.
This command is retained only as an explicit, low-ceremony confirmation that a file parses
end to end — it is not a gap detector.

### `--epw-rows`

```bash
dotnet run --project NEM.CLI -- --epw-rows path/to/file.epw
```

Prints the total row count, then the dry-bulb temperature, DNI and wind speed for the first row,
the middle of the year (row 4,380), and the last row.

### `--epw-header`

```bash
dotnet run --project NEM.CLI -- --epw-header path/to/file.epw
```

Prints the file's city, time zone, records-per-hour, whether a leap year was observed, and the
line number the data section starts at.
