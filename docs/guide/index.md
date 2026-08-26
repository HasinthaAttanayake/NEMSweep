# Getting started

This page takes you from a fresh clone to a dispatch result on screen.

## Prerequisites

NEMSweep targets `net10.0` (see `NEMSweep.Model/NEMSweep.Model.csproj`), so you need the .NET 10 SDK
installed. Nothing else is required to build, test or run the CLI.

If you would rather not install a toolchain, there is a container image instead, and the rest of
this page still applies to it: same commands, same workspace, mounts in place of directories. See
[Running the container](container.md).

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

The four settings, from `NEMSweep.CLI/Configuration/CliSettings.cs`:

| Setting | Meaning |
|---|---|
| `inputBundleRoot` | Where `--validate-inputs` and `--ingest` look for an input bundle by default. |
| `dataRoot` | Where a scenario's demand and weather artifacts are read from, and where `--ingest` writes them. |
| `outputRoot` | Where results and sweep artifacts are written. |
| `defaultScenarioPath` | The scenario configuration `--run-scenario` uses when you do not pass one explicitly. |

All four are plain strings resolved relative to your current working directory at run time, so any
of them can point outside the repository. `dataRoot` and `outputRoot` can also be overridden per run
with `--data-root` and `--output`, or with `NEMSWEEP_DATA_ROOT` and `NEMSWEEP_OUTPUT`; the
[CLI reference](cli.md) covers the precedence.

The committed example ships `dataRoot` pointing at `NEMSweep.Web/wwwroot/data`, because that is
where this repository currently keeps its artifacts, and `outputRoot` pointing at a gitignored
`out/`. A fresh clone therefore runs without configuration, and your results land somewhere that
does not disturb the committed ones.

## First run

```bash
dotnet run --project NEMSweep.CLI -- --run-scenario
```

With no argument, this loads the scenario at `defaultScenarioPath`. That is the committed
`scenarios/nem-fy2026-all-regions.json`, which dispatches all five NEM regions together over
directed interconnectors. It reads `demand-{region}.json` and `weather-{region}.json` for each
region from the data root, and dispatches every hour of the modelled year, sizing storage against
the scenario's reliability standard as it goes.

It writes `results.json`, `results-overview.json`, and a `results-{region}.json` and
`results-{region}-overview.json` pair for each region, all under the output root, which is `out/`
unless you say otherwise. Publication is atomic, so a failed run never leaves a half-written
artifact in place.

To send a run somewhere else, name it:

```bash
dotnet run --project NEMSweep.CLI -- --run-scenario --output ./my-study
```

That is also how the results the site displays are refreshed: point `--output` at the web project
when you intend to update what it shows, and nowhere near it the rest of the time.

Add `--csv` and the run also writes its results as [CSV tables](csv-tables.md), which is what you
want if the numbers are going anywhere other than back into NEMSweep.

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

`NEMSweep.Web` is a Blazor WebAssembly site that reads the JSON artifacts committed under
`NEMSweep.Web/wwwroot/data`. A default run writes to `out/` and so does not change what the site
shows; rerun with `--output NEMSweep.Web/wwwroot/data` when you mean to publish a result to it.

## Validation runs worth knowing about

Two test runs act as acceptance checks on the model rather than ordinary unit tests. See the
"Validation runs" section of the repository [README](https://github.com/HasinthaAttanayake/NEMSweep#readme)
for the exact commands:

- A suite of hand-calculated dispatch fixtures, checked against manually worked figures.
- A synthetic 8,760-hour full-year storage-sizing acceptance test, run in Release mode, which
  prints solver wall-clock time, dispatch-pass count and the selected Battery capacity.

## Making it yours

The published scenario is a worked example of a real system, and it is 746 lines because a real
system is. Two shorter routes in, and the second is easier than it looks:

**Start from a small scenario.** `scenarios/starter-nsw1.json` is the same NSW1 assets on their own,
about a fifth the length, and it runs the same way:

```bash
dotnet run --project NEMSweep.CLI -- --run-scenario scenarios/starter-nsw1.json
```

Change one number in it, run it again, and watch one number move in the result. That loop is the
whole skill. For a blank starting point instead, `--new-scenario` prints the smallest configuration
that runs:

```bash
dotnet run --project NEMSweep.CLI -- --new-scenario > scenarios/mine.json
```

**Or write a sweep, which is less work than a scenario.** This is the counterintuitive one. A sweep
point is not a scenario: it is a handful of overrides applied to one you already have, plus a value
saying where it sits on the axis. Copy `sweeps/datacentre-nameplate-fy2026.json`, change the numbers,
and you have a study without having authored a system at all.

It is also the question most people arrive with. "What happens as I vary this" is a sweep; "here is
a system I designed" is a scenario. Reach for [Sweeps](sweeps.md) before
[Scenario configuration](scenarios.md) unless you know you need the latter.

Validate before you spend time dispatching:

```bash
dotnet run --project NEMSweep.CLI -- --fan-out-sweep sweeps/my-sweep.json
```

## Where next

- [Glossary](glossary.md): the vocabulary, if a term above went past you.
- [Concepts](../concepts/index.md): what the model actually does.
- [Limitations](../assumptions/limitations.md): what it assumes, and where it will mislead you.
- [Exploring](../exploring/index.md): designing a study, including sweeps and driving the model
  with an LLM.
