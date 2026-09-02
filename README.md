# NEMSweep

NEMSweep is a deterministic engine for grid dispatch, reliability assessment, storage sizing and
system cost. You describe a set of regions, each with its demand, generation and storage, and a
reliability standard. The engine dispatches them in merit order for every hour of the modelled
period, grows battery storage in the regions that miss the standard, and reports the technical and
economic result. It is written for analysts building or scrutinising an energy-system policy or
investment case, who will quote its numbers in their own work.

The engine does not hardcode a region list or couple to AEMO: region identifiers are free-form
strings. Its grid model runs on a fixed one-hour timestep, and sub-hourly input is resampled to it.
The market-time offset is a run parameter, taken from the scenario period bounds and defaulting to
the National Electricity Market's UTC+10; a run works in one fixed offset with no daylight saving.
`NEMSweep.Model` and `NEMSweep.Contracts` have no package dependencies, so you can embed them in
your own software.

**Documentation:** [how to run it, what it assumes, and how to explore the scenario space](docs/index.md)
**Live results:** [https://www.nemsweep.com/](https://www.nemsweep.com/)

## The three layers

A statement about NEMSweep belongs to exactly one of three layers. Keeping them apart is how you
read the rest of this repository without mistaking the published example for the limit of the
engine.

| Layer | What it is | Where it lives |
|---|---|---|
| Framework | The dispatch, reliability, storage-sizing and cost engine described above. No hardcoded region list and no AEMO coupling. Fixed one-hour timestep; the market-time offset is a run parameter that defaults to UTC+10. Deterministic, no package dependencies, embeddable. | `NEMSweep.Model`, `NEMSweep.Contracts` |
| NEM scoping | The command-line tool that binds the framework to Australia. It ingests AEMO operational demand, EnergyPlus Weather data and AEMO generation data, and validates scenarios against the five National Electricity Market regions (NSW1, QLD1, SA1, TAS1, VIC1). | `NEMSweep.CLI` |
| Published example | One scenario: the National Electricity Market configured for the 2026 financial year, plus a sweep that adds data-centre load across the regions. Everything on nemsweep.com is this one example. | the repository artifacts, the live site |

The five NEM regions are the CLI's constraint, enforced because the data it ingests is National
Electricity Market data. The one-hour timestep is the framework's, fixed in `NEMSweep.Model`. The
market-time offset is a run parameter the framework reads from the scenario period; the CLI takes it
from the ingested data, so a NEM run gets UTC+10.

## What the framework does

Given a realised system and a reliability standard, the framework:

- dispatches generation in merit order by short-run marginal cost for every hour, and meters flow
  and loss on each directed interconnector;
- grows battery storage in the regions that miss the standard, re-dispatching the whole linked
  system for each candidate, and stops when the standard is met or the bounded search reaches a
  limit;
- costs the build and operation of the resulting system and divides annualised cost by energy
  served (demand minus unserved energy) to give a system levelised cost of electricity (SLCoE), in
  AUD per MWh.

Only battery capacity is sized. Pumped hydro is fixed at whatever the scenario declares.

Where a region does not reach the standard, the search reports what stopped it: a battery capacity
ceiling, a dispatch-pass budget, storage that has stopped reducing unserved energy, or system
generation energy below demand energy. Only the last of these establishes that no battery size
could have met the standard.

## What the framework does not do

- It is not a market model. There is no bidding, no settlement, no unit commitment, and no
  security-constrained economic dispatch. Merit-order dispatch only.
- It is not a forecast. Each run is one system against one weather profile, with no stochastic
  draws, so it reports a realised outcome rather than an expectation over a distribution. The
  trustworthy output is the gap between two scenarios, not the level of any single one.
- It is not a general-purpose energy-system model. The dispatch method is fixed. The flexibility is
  in the system you describe, not in the modelling approach.
- The cost it reports covers building and running the system. It excludes retail, network and
  scheme costs. SLCoE is not a retail price anyone pays.

The project treats this low fidelity as a feature: fast feedback, no proprietary solver, no
linear-programming background required, and it runs on a laptop. Read
[Limitations](docs/assumptions/limitations.md) before you quote a figure from it.

## Determinism and provenance

The same inputs at the same commit reproduce every modelled value. Every result records the SHA-256
digest of the exact input bytes it was built from, and that digest, not the file path, is the
reproducibility boundary. The model constants a scenario cannot override are listed in an
assumptions register that a test suite (`ModelAssumptionsTests`) checks against the code on every
change.

Dispatch artifacts are not byte-identical between reruns, because each run stamps a fresh `runId`
that identifies the run rather than describing its contents.
[Outputs and provenance](docs/guide/outputs.md) sets out exactly what differs.

## The published example

nemsweep.com publishes one worked example, not a dataset and not a forecast:

- a baseline scenario, `scenarios/nem-fy2026-all-regions.json`, dispatching all five National
  Electricity Market regions together over directed interconnectors, built from AEMO operational
  demand for the 2026 financial year, a typical-meteorological-year weather profile, and a declared
  generation and storage fleet;
- a sweep, `sweeps/datacentre-nameplate-fy2026.json`, that holds that baseline fixed and adds
  data-centre nameplate load across the regions, from 0 to 12,000 MW, one run per step.

The data-centre framing follows the Australian Government's
[expectations for data-centre and AI infrastructure developers](https://www.industry.gov.au/publications/expectations-data-centres-and-ai-infrastructure-developers).
Any load increase behaves the same way in the model.

The example demonstrates what the framework does. It is not the limit of what the framework does.
Generation mix, economics, the reliability standard and transmission capacity are all scenario
inputs, and each can be swept the same way. [Designing a study](docs/exploring/index.md) covers how.

The example's artifacts are published with the results site. They are an illustrative example, not
a dataset, and they derive from AEMO and EnergyPlus Weather sources under their own terms. Run your
own scenario before quoting a figure.

## Repository

| Project | Contents |
|---|---|
| `NEMSweep.Model` | The framework: domain models, units, time series, generation models, dispatch, storage sizing, economics. No package dependencies. |
| `NEMSweep.Contracts` | The exported data contracts. No package dependencies. |
| `NEMSweep.CLI` | The National Electricity Market scoping: source-data validation and ingestion, and the commands that produce the published datasets. |
| `NEMSweep.Model.Tests`, `NEMSweep.CLI.Tests` | Cover model and ingestion behaviour. |
| `docs` | The docfx documentation site. |

`NEMSweep.CLI` is organised by workflow, with application mechanics kept separate:

| Folder | Responsibility |
|---|---|
| `Application` | Argument routing, workspace-override parsing, exit codes, shared command context, and the commands that run without a workspace (`--new-scenario`, `--describe-schema`) |
| `Configuration` | Typed CLI settings, the input-bundle manifest, and the scenario config and sweep definition formats, each with its validation |
| `Infrastructure` | Workspace-root resolution, the shared JSON read and write policy, JSON merge-patch, staged atomic file writes, and build provenance |
| `Demand` | Operational-demand import, validation, and export |
| `Weather` | EPW parsing, provenance analysis, the weather basis, and weather export |
| `Generation` | Generation-information workbook import and export |
| `Ingest` | Input-bundle validation and coordinated artifact ingestion |
| `Scenarios` | Scenario validation and dispatch, sweep fan-out and runs, and result export as JSON and the CSV star schema |

`NEMSweep.CLI.Tests` mirrors the same feature folders. The implemented aggregate roots and
domain-service boundaries are tracked in the [domain model](docs/domain-model.md).

## Licence and data

The code is [BSD-3-Clause](LICENSE.md): use it in your own software, including proprietary software,
with attribution and no copyleft. `NEMSweep.Model` and `NEMSweep.Contracts` have no package
dependencies, so a project reference from a clone, or a reference to the built assembly, is all it
takes to embed them.

The data is not covered by that licence. The demand, generation and weather artifacts derive from
AEMO and EnergyPlus Weather sources with their own terms. Read [DATA-LICENSE.md](DATA-LICENSE.md)
before redistributing any of it. The artifacts this repository carries are an illustrative example,
not a dataset.

Using NEMSweep in published work? [CITATION.cff](CITATION.cff) has the citation metadata.
Contributions are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md).

## Local development

There are two ways to run NEMSweep: clone it and build it with the .NET SDK, or run the published
container image, which needs no toolchain and works under Docker or Podman.

The image is published to the GitHub Container Registry as `ghcr.io/<owner>/nemsweep`, where
`<owner>` is the repository owner. It contains the tool and nothing else: demand, weather and
generation artifacts are inputs you bring. The image defaults the data root to `/data` and the
output root to `/out`, so mount your own directories over those and pass a scenario by absolute
path:

```bash
docker run --rm -v ./reference:/data:ro -v ./study:/out ghcr.io/<owner>/nemsweep:latest --run-scenario /data/my-scenario.json
```

Podman takes the same arguments. [Running the container](docs/guide/container.md) covers the
mounts, where the input data comes from, and pinning a run by image digest.

NEMSweep targets .NET 10 for a source build. The `dotnet` commands below are identical on Windows,
macOS and Linux.

```bash
dotnet build NEMSweep.slnx
```

```bash
dotnet test NEMSweep.slnx
```

```bash
dotnet run --project NEMSweep.CLI -- --help
```

Copy `NEMSweep.CLI/appsettings.example.json` to `NEMSweep.CLI/appsettings.local.json` for
machine-local input and output paths. The local file is ignored by Git, and the example is the
fallback when it is absent, so a fresh clone runs without configuring anything.

A run reads its inputs from a data root and writes results to an output root. The committed example
reads the artifacts this repository already carries and writes to a gitignored `out/`, so an
ordinary run never disturbs the published results. Override either per run:

```bash
dotnet run --project NEMSweep.CLI -- --run-scenario --output ./my-study
```

`NEMSWEEP_DATA_ROOT` and `NEMSWEEP_OUTPUT` do the same for environments where editing a settings
file is awkward. Nothing searches for the repository, so the built executable runs from wherever you
put it.

The full walkthrough covering configuration, the first scenario run and every command is in
[Getting started](docs/guide/index.md) and the [CLI reference](docs/guide/cli.md).

### Validation runs

Run the committed hand-calculated dispatch fixtures with:

```bash
dotnet test NEMSweep.Model.Tests/NEMSweep.Model.Tests.csproj --filter FullyQualifiedName~ManualScenarioFixtureTests
```

Run the synthetic 8,760-hour storage-sizing acceptance in Release mode with:

```bash
dotnet test NEMSweep.Model.Tests/NEMSweep.Model.Tests.csproj -c Release --filter FullyQualifiedName~FullYearSizingAcceptanceTests --logger "console;verbosity=detailed"
```

The full-year test prints solver wall-clock time, dispatch-pass count, and the selected battery
capacity. Treat a runtime above a few minutes as a scope issue to record rather than an automatic
optimisation task.

### Documentation site

The docs under `docs/` are built with [docfx](https://dotnet.github.io/docfx/), pinned in
`.config/dotnet-tools.json`.

```bash
dotnet tool restore
```

```bash
dotnet docfx docs/docfx.json --serve
```

The site is then at `http://localhost:8080`. `docs/api` and `docs/_site` are generated and ignored
by Git. CI builds the site with `--warningsAsErrors`, so a broken cross-reference fails the build.
