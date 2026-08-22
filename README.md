# NemSim

NemSim is an hourly grid dispatch model for Australia's National Electricity Market, being built
in public one validated layer at a time.

You describe a set of regions — each with its demand, generation assets and storage — and NemSim
dispatches them hour by hour across a modelled year. Where the reliability standard you declared is
not met, it grows storage until either the standard is met or it reports that no battery of any
size can meet it. The result is a set of technical and economic figures for that system.

The model is deterministic: the same inputs at the same commit produce byte-identical artifacts, and
every result records the SHA-256 of the exact input bytes it was built from.

**Documentation:** [how to run it, what it assumes, and how to explore the scenario
space](docs/index.md)
**Live results site:** [https://nemsim.pages.dev/](https://nemsim.pages.dev/)

> **Under active construction**
>
> Development is sequential: each dataset and model component is added, tested, and exposed before
> the next part of the simulation is introduced. The deployed site shows the current state of that
> work, not a completed model.

## Research focus

The published work so far explores:

1. What could electricity cost in 2030 if Australia achieves its 82% renewable generation target?
2. How could data-centre growth affect that outcome under the Australian Government's
   [expectations for data-centre and AI infrastructure developers](https://www.industry.gov.au/publications/expectations-data-centres-and-ai-infrastructure-developers)?

That is one slice of the space the framework supports. Generation mix, economics, reliability
standards and transmission capacity can all be swept the same way — see
[Designing a study](docs/exploring/index.md).

## Available now

The currently validated and published layers are:

- **Operational demand:** actual operational demand by financial year, month, or day, for each of
  the five NEM regions.
- **Weather resources:** solar radiation, wind, temperature, solar geometry, and modelled solar and
  wind generation.
- **Generation fleet:** installed capacity and source units by technology.
- **Baseline dispatch:** hourly merit-order generation by technology against operational demand,
  including curtailment and reliability metrics.
- **Whole-system dispatch:** the five regions dispatched together over directed interconnectors,
  with losses metered per link, transmission costed, and storage sized across the regions to a
  reliability standard.
- **Scenario sweeps:** one input varied across a series of runs with the rest of the scenario held
  constant, published with every run's results and provenance.

Follow progress on the
[NEM Sim Development Board](https://github.com/users/HasinthaAttanayake/projects/11).

## Repository

- `NEM.Model` contains domain models, units, time series, and generation models.
- `NEM.Contracts` defines the exported data contracts.
- `NEM.CLI` validates source data and generates the datasets consumed by the site.
- `NEM.Web` is the Blazor WebAssembly results site.
- `NEM.Model.Tests`, `NEM.CLI.Tests`, and `NEM.Web.Tests` cover model, ingestion, and web behaviour.
- `docs` is the docfx documentation site.

`NEM.CLI` is organised by workflow, with application mechanics kept separate:

| Folder | Responsibility |
| --- | --- |
| `Application` | Argument routing, usage, exit codes, and shared command context |
| `Configuration` | Strongly typed local/example settings and validation |
| `Infrastructure` | Repository paths and the shared JSON policy |
| `Demand` | Operational-demand import, validation, and export |
| `Weather` | EPW parsing, provenance analysis, diagnostics, and weather export |
| `Generation` | Generation-information workbook import and export |
| `Ingest` | Input-bundle validation and coordinated artifact ingestion |
| `Scenarios` | Scenario input adaptation, dispatch orchestration, and result export |

`NEM.CLI.Tests` mirrors the same feature folders. The implemented aggregate roots and
domain-service boundaries are tracked in the [domain model](docs/domain-model.md).

## Local development

NemSim targets .NET 10.

```powershell
dotnet build .\NemSim.slnx
```

```powershell
dotnet test .\NemSim.slnx
```

```powershell
dotnet run --project .\NEM.CLI\NEM.CLI.csproj -- --help
```

```powershell
dotnet run --project .\NEM.Web\NEM.Web.csproj
```

Copy `NEM.CLI/appsettings.example.json` to `NEM.CLI/appsettings.local.json` for machine-local input
and output paths. The local file is ignored by Git; the example is the fallback when it is absent.

The full walkthrough — configuration, the first scenario run, and every command — is in
[Getting started](docs/guide/index.md) and the [CLI reference](docs/guide/cli.md).

### Validation runs

Run the committed hand-calculated dispatch fixtures with:

```powershell
dotnet test .\NEM.Model.Tests\NEM.Model.Tests.csproj --filter FullyQualifiedName~ManualScenarioFixtureTests
```

Run the synthetic 8,760-hour storage-sizing acceptance in Release mode with:

```powershell
dotnet test .\NEM.Model.Tests\NEM.Model.Tests.csproj -c Release --filter FullyQualifiedName~FullYearSizingAcceptanceTests --logger "console;verbosity=detailed"
```

The full-year test prints solver wall-clock time, dispatch-pass count, and the selected Battery
capacity. Treat a runtime above a few minutes as a scope issue to record rather than an automatic
optimisation task.

### Documentation site

The docs under `docs/` are built with [docfx](https://dotnet.github.io/docfx/), pinned in
`.config/dotnet-tools.json`.

```powershell
dotnet tool restore
```

```powershell
dotnet docfx .\docs\docfx.json --serve
```

The site is then at `http://localhost:8080`. `docs/api` and `docs/_site` are generated and ignored
by Git. CI builds the site with `--warningsAsErrors`, so a broken cross-reference fails the build.
