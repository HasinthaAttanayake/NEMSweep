# Getting started

This page takes you from a fresh clone to a dispatch result on screen.

## Prerequisites

NEMSweep targets `net10.0` (see `NEMSweep.Model/NEMSweep.Model.csproj`), so you need the .NET 10 SDK installed.
Nothing else is required to build, test or run the CLI.

## Clone, build, test

```bash
git clone https://github.com/HasinthaAttanayake/NEMSweep.git
```

```bash
dotnet build NEMSweep.slnx
```

```bash
dotnet test NEMSweep.slnx
```

The solution file is `NEMSweep.slnx` at the repository root. `dotnet test` runs `NEMSweep.Model.Tests`,
`NEMSweep.CLI.Tests` and `NEMSweep.Web.Tests`.

## Configuration

`NEMSweep.CLI` reads its settings from a JSON file next to the built executable. There are two
candidates, checked in this order:

1. `appsettings.local.json`, which is machine-local, gitignored, and absent by default.
2. `appsettings.example.json`, which is committed and used as the fallback when the local file is
   absent.

Copy the example to create your own local file:

```bash
cp NEMSweep.CLI/appsettings.example.json NEMSweep.CLI/appsettings.local.json
```

Because the local file is gitignored, editing it never produces a diff for anyone else, and
because the example is the fallback, the CLI works out of the box even if you skip this step.

The three settings, from `NEMSweep.CLI/Configuration/CliSettings.cs`:

| Setting | Meaning |
|---|---|
| `inputBundleRoot` | Where `--validate-inputs` and `--ingest` look for an input bundle by default. |
| `outputRoot` | Where `--ingest` writes its artifacts, and the first place a scenario run looks for the demand and weather files a scenario references. Normally `NEMSweep.Web/wwwroot/data`, the directory the web site reads from. It does **not** control where scenario results are published. |
| `defaultScenarioPath` | The scenario configuration `--run-scenario` uses when you do not pass one explicitly. |

All three are plain strings resolved relative to the solution root at run time, so you can point
`inputBundleRoot` or `outputRoot` at a directory outside the repository without NEMSweep caring where
your shell happens to be. The [CLI reference](cli.md) covers how command-line paths are resolved
the same way.

## First run

```bash
dotnet run --project NEMSweep.CLI -- --run-scenario
```

With no argument, this loads the scenario at `defaultScenarioPath`. That is the committed
`scenarios/nem-fy2026-all-regions.json`, which dispatches all five NEM regions together over
directed interconnectors. It reads the committed demand and weather artifacts for each region
(`demand-{region}.json` and `weather-{region}.json`, looked for under `outputRoot` first and then
under the solution root) and dispatches every hour of the modelled year, sizing storage against the
scenario's reliability standard as it goes.

It writes `results.json`, `results-overview.json`, and a `results-{region}.json` and
`results-{region}-overview.json` pair for each region. Results always go to
`NEMSweep.Web/wwwroot/data`, which is a fixed path rather than the configured `outputRoot`; changing
`outputRoot` moves where inputs are read from, not where results land. Publication is atomic, so a
failed run never leaves a half-written artifact in place.

A single scenario run dispatches a modelled year in one pass per storage-sizing iteration, so it
finishes quickly enough to iterate on locally rather than being something you queue and walk away
from. You will know it succeeded because the process prints the number of hourly intervals
dispatched and the regions covered, then a line confirming where it wrote the results, and exits
with status 0. If the reliability standard was not met, it also prints a `WARNING` line naming the
achieved and target unserved-energy percentages. That is not a failure, just a result worth
reading closely.

## Viewing results

```bash
dotnet run --project NEMSweep.Web
```

`NEMSweep.Web` is a Blazor WebAssembly site that reads the JSON artifacts under
`NEMSweep.Web/wwwroot/data`, the same files `--run-scenario` writes and `--ingest` writes to by default.
Open the URL it prints to browse the results you just generated.

## Validation runs worth knowing about

Two test runs act as acceptance checks on the model rather than ordinary unit tests. See the
"Validation runs" section of the repository [README](https://github.com/HasinthaAttanayake/NEMSweep#readme)
for the exact commands:

- A suite of hand-calculated dispatch fixtures, checked against manually worked figures.
- A synthetic 8,760-hour full-year storage-sizing acceptance test, run in Release mode, which
  prints solver wall-clock time, dispatch-pass count and the selected Battery capacity.

## Where next

- [Concepts](../concepts/index.md): what the model actually does.
- [Limitations](../assumptions/limitations.md): what it assumes, and where it will mislead you.
- [Exploring](../exploring/index.md): designing a study, including sweeps and driving the model
  with an LLM.
