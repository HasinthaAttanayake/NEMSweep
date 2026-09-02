# Getting started

This page takes you from a fresh clone to a dispatch result on screen. It is for anyone who wants to
run NEMSweep locally.

Everything on this page is `NEMSweep.CLI`, the command-line tool that runs the framework against
National Electricity Market data. The framework itself (`NEMSweep.Model`, `NEMSweep.Contracts`) has
no command line and no data; the CLI is what ingests AEMO and EnergyPlus Weather inputs, validates
scenarios against the five NEM regions, and writes the published artifacts.

## Prerequisites

NEMSweep targets `net10.0` (see `NEMSweep.Model/NEMSweep.Model.csproj`), so a source build needs the
.NET 10 SDK. Nothing else is required to build, test or run the CLI.

To run without a toolchain, use the container image instead. The rest of this page still applies:
same commands, same workspace, mounts in place of directories. See
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

The solution file is `NEMSweep.slnx` at the repository root. `dotnet test` runs
`NEMSweep.Model.Tests` and `NEMSweep.CLI.Tests`.

## Configure the workspace

`NEMSweep.CLI` reads its settings from a JSON file next to the built executable. Two files are
candidates, checked in this order:

1. `appsettings.local.json`: machine-local, gitignored, absent by default.
2. `appsettings.example.json`: committed, and used as the fallback when the local file is absent.

Copy the example to create your own local file:

```bash
cp NEMSweep.CLI/appsettings.example.json NEMSweep.CLI/appsettings.local.json
```

The local file is gitignored, so editing it never produces a diff for anyone else. The example is
the fallback, so the CLI runs out of the box even if you skip this step.

The four settings, defined in `NEMSweep.CLI/Configuration/CliSettings.cs`:

| Setting | Type | Required | Meaning |
|---|---|---|---|
| `inputBundleRoot` | string | yes | Where `--validate-inputs` and `--ingest` look for an input bundle by default. |
| `dataRoot` | string | yes | Where a scenario's demand and weather artifacts are read from, and where `--ingest` writes them. |
| `outputRoot` | string | yes | Where dispatch results and sweep artifacts are written. |
| `defaultScenarioPath` | string | yes | The scenario config `--run-scenario` uses when you do not pass one. |

All four are resolved relative to your current working directory at run time, so any of them can
point outside the repository. `dataRoot` and `outputRoot` can also be overridden per run with
`--data-root` and `--output`, or with `NEMSWEEP_DATA_ROOT` and `NEMSWEEP_OUTPUT`; the
[CLI reference](cli.md#the-workspace) covers the precedence.

The committed `appsettings.example.json` points `dataRoot` at the published example artifacts, and
`outputRoot` at a gitignored `out/`. A fresh clone therefore runs without configuration, and your
results land where they do not disturb the published ones.

## Run your first scenario

```bash
dotnet run --project NEMSweep.CLI -- --run-scenario
```

With no argument, `--run-scenario` loads the scenario at `defaultScenarioPath`. In the committed
settings that is `scenarios/nem-fy2026-all-regions.json`, which dispatches all five NEM regions
together over directed interconnectors. It reads `demand-{region}.json` and `weather-{region}.json`
for each region from the data root, dispatches every hour of the modelled year, and sizes battery
storage against the scenario's reliability standard as it goes.

The run writes `results.json`, `results-overview.json`, and a `results-{region}.json` and
`results-{region}-overview.json` pair for each region, all under the output root. Publication is
atomic, so a failed run never leaves a half-written artifact in place. See
[Outputs and provenance](outputs.md) for the artifact map.

To send a run elsewhere, name the output directory:

```bash
dotnet run --project NEMSweep.CLI -- --run-scenario --output ./my-study
```

That is also how the results displayed by the separate results site are refreshed: publish the
output when you intend to update what it shows, and keep ordinary study output in your own directory.

Add `--csv` and the run also writes its results as [CSV tables](csv-tables.md), which is what you
want if the numbers are going anywhere other than back into NEMSweep.

A scenario run re-dispatches the whole modelled year for each candidate battery size the sizing
search tries, so its runtime is one dispatch multiplied by the number of passes the search takes.
It still finishes quickly enough to iterate on locally. On success the process prints the number of
hourly intervals dispatched and the regions covered, then the path it wrote the results to, and
exits with status 0.
If the reliability standard was not met it also prints a `WARNING` line naming the achieved and
target unserved-energy percentages. That is a result worth reading closely, not a failure, and the
exit code is still 0.

## Start from a smaller scenario

The published `scenarios/nem-fy2026-all-regions.json` is a worked example of a real system, and it
is 746 lines because a real system is. Two shorter ways in:

`scenarios/starter-nsw1.json` is the same NSW1 assets on their own, 176 lines, and it runs the same
way:

```bash
dotnet run --project NEMSweep.CLI -- --run-scenario scenarios/starter-nsw1.json
```

Change one number in it, run it again, and watch one number move in the result.

For a blank starting point, `--new-scenario` prints the smallest configuration that runs: one
region, one generating fleet, one storage fleet. It writes to standard output, so redirect it to a
file:

```bash
dotnet run --project NEMSweep.CLI -- --new-scenario > scenarios/mine.json
```

## Or write a sweep

A sweep is often less work than a scenario. A sweep is a baseline scenario config plus a set of
points, each point a small override patch on that baseline, lined up along one labelled axis. You do
not author a system: you take one that exists and vary it.

"What happens as I vary this" is a sweep. "Here is a system I designed" is a scenario. If your
question is the first kind, read [Sweeps](sweeps.md) before
[Scenario configuration](scenarios.md). Copy `sweeps/datacentre-nameplate-fy2026.json`, change the
numbers, and validate before spending time on dispatch:

```bash
dotnet run --project NEMSweep.CLI -- --fan-out-sweep sweeps/my-sweep.json
```

`--fan-out-sweep` applies every point's patch to the baseline and validates each resulting config
without running any dispatch, so a bad override surfaces immediately.

## Where next

- [CLI reference](cli.md): every command, its arguments, and how the workspace roots are chosen.
- [Glossary](glossary.md): the vocabulary, if a term above went past you.
- [Concepts](../concepts/index.md): what the model does, stage by stage.
- [Limitations](../assumptions/limitations.md): what the model assumes, and where it will mislead
  you.
- [Exploring](../exploring/index.md): designing a study, including sweeps and driving the model with
  an LLM.
