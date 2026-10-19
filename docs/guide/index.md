# Getting started

This page takes you from a fresh clone to a dispatch result on screen. It is for anyone who wants to
run NEMSweep locally.

Everything on this page is `NEMSweep.CLI`, the command-line tool that ingests raw
inputs and runs portable grid scenarios. It accepts custom region IDs through
format-2 bundles, as well as National Electricity Market inputs. The framework
itself (`NEMSweep.Model`, `NEMSweep.Contracts`) has no command line and no data.

For ready-to-edit configurations, see [Hosted starters](synthetic-starters.md):
Simple is the smallest, a synthetic Island-X grid; Advanced is the NEM worked example, preserving
real Australian NEM FY2026 AEMO demand, original TMYx EPW weather and the existing five-region
fleet/cost/link assumptions. Both are prepared by `nemsweep --quick-start`. For an
immediately local recipe, see [Custom grids](custom-grids.md).

## Prerequisites

NEMSweep targets `net10.0` (see `NEMSweep.Model/NEMSweep.Model.csproj`), so a source build needs the
.NET 10 SDK, and so does installing the published .NET tool. Nothing else is required to build,
test or run the CLI.

To run without a toolchain, use the container image instead. The rest of this page still applies:
same commands, same workspace, mounts in place of directories. See
[Running the container](container.md).

### Linux

Two things catch a fresh Linux machine out, neither specific to NEMSweep.

**No .NET SDK.** Where your distribution does not package .NET 10, Microsoft's script installs it
into your home directory without root:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
```

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools"
```

**`PATH` after a tool install.** `dotnet tool install --global` writes to `~/.dotnet/tools`, which a
fresh shell does not search, so `nemsweep` comes back as `command not found` even though the install
reported success:

```bash
export PATH="$PATH:$HOME/.dotnet/tools"
```

Put either `export` in your shell profile to make it persist.

**ICU.** .NET needs the ICU libraries for globalization, and a minimal container or cloud image
often ships without them. The symptom is a `System.Globalization` stack trace on first run rather
than a clear message. Install them with the distribution's own package:

```bash
sudo apt-get update && sudo apt-get install -y libicu-dev
```

```bash
sudo dnf install -y libicu
```

## Install without cloning

If you only want to run NEMSweep, install the
[NEMSweep.CLI](https://www.nuget.org/packages/NEMSweep.CLI) .NET tool rather than building from
source. These docs target 0.3.0; installation requires its publication
(see the [release target notice](https://github.com/HasinthaAttanayake/NEMSweep#install)). Pin the version you want to reproduce:

```bash
dotnet tool install --global NEMSweep.CLI --version 0.3.0
```

```bash
nemsweep --version
```

The second command confirms the install and prints `NEMSweep.CLI 0.3.0+<commit>`.

Everything below is then `nemsweep <command>` in place of
`dotnet run --project NEMSweep.CLI -- <command>`, and the workspace is configured with `--data-root`
and `--output` or their environment variables rather than with a settings file next to a build. To
build against the framework instead of running it, add the `NEMSweep.Model` and `NEMSweep.Contracts`
packages to your own project:

```bash
dotnet add package NEMSweep.Model --version 0.3.0
dotnet add package NEMSweep.Contracts --version 0.3.0
```

Use the same package version for both references. Later releases are opt-in: replace `0.3.0` only
when you intend to upgrade, then rerun your tests and scenario validation.

## Clone, build, test

A source build reports the `VersionPrefix` in `Directory.Build.props`, and a release takes its
version from the `v*` tag instead. `DocumentedCliVersion` pins the release targeted by the install
snippets above; neither property confirms that the packages have been published.

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

`outputRoot` in the committed `appsettings.example.json` is a gitignored `out/`, so your results
land where they do not disturb published ones. `dataRoot` is the setting you have to supply:
the example still points it at `NEMSweep.Web/wwwroot/data`, a path the repository no longer
contains, and the demand and weather artifacts are not carried here in any case. Set it to wherever
you ingest them, or pass `--data-root` per run.

## Ingest the input artifacts

Building your own input bundle? Start with
[AEMO NEMWeb demand and Climate.OneBuilding EPW weather](input-bundles.md#finding-sources-for-your-own-bundle).
That guide covers source selection, unchanged files, typical-year provenance and
the separate data-attribution/licensing requirements.

A scenario reads a `demand-{region}.json` and a `weather-{region}.json` per region, plus
the generation fleet defined in its scenario configuration. The demand and weather
artifacts are produced, not committed: assemble a current **format-2**
[input bundle](input-bundles.md) of demand archives and EPW weather files, then run

```bash
dotnet run --project NEMSweep.CLI -- --validate-inputs
```

```bash
dotnet run --project NEMSweep.CLI -- --ingest
```

`--validate-inputs` writes nothing and reports what it found. `--ingest` runs the same checks and
then writes the artifacts into the data root. [Input bundles](input-bundles.md) covers the bundle
shape, the manifest, and which upstream file supplies what.

**Legacy format 1 only:** existing NEM bundles additionally contain an AEMO
Generation Information workbook. Their ingestion produces `generation-information.json`
for legacy generation-information commands. Format-2 ingestion and scenario runs
do not require that workbook or artifact; scenario configuration supplies the fleet.

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
