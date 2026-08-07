# NemSim

NemSim is an open model of Australia's National Electricity Market, being built
in public one validated layer at a time.

**Live progress site:** [https://nemsim.pages.dev/](https://nemsim.pages.dev/)

> **Under active construction**
>
> Development is sequential: each dataset and model component is added, tested,
> and exposed before the next part of the simulation is introduced. The deployed
> site shows the current state of that work, not a completed model.

## Research focus

NemSim is being developed to explore:

1. What could electricity cost in 2030 if Australia achieves its 82% renewable
   generation target?
2. How could data-centre growth affect that outcome under the Australian
   Government's [expectations for data-centre and AI infrastructure developers](https://www.industry.gov.au/publications/expectations-data-centres-and-ai-infrastructure-developers)?

## Available now

The currently validated and published layers are:

- **Operational demand:** New South Wales actual operational demand by financial
  year, month, or day.
- **Weather resources:** solar radiation, wind, temperature, solar geometry, and
  modelled solar and wind generation.
- **Baseline dispatch:** hourly merit-order generation by technology against New
  South Wales operational demand, including curtailment and reliability metrics.

New views will be published as the sequential build reaches each part of the
model. Follow progress on the [NEM Sim Development Board](https://github.com/users/HasinthaAttanayake/projects/11).

## Repository

- `NEM.Model` contains domain models, units, time series, and generation models.
- `NEM.CLI` validates source data and generates datasets consumed by the site.
- `NEM.Contracts` defines the exported data contracts.
- `NEM.Web` is the Blazor WebAssembly progress site.
- `NEM.Model.Tests` and `NEM.CLI.Tests` cover model and ingestion behaviour.

The implemented aggregate roots and domain-service boundaries are tracked in
the [domain model](docs/domain-model.md).

### CLI structure

`NEM.CLI` is organised by workflow, with application mechanics kept separate:

| Folder | Responsibility |
| --- | --- |
| `Application` | Argument routing, usage, exit codes, and shared command context |
| `Configuration` | Strongly typed local/example settings and validation |
| `Infrastructure` | Repository paths and the shared JSON policy |
| `Demand` | Operational-demand import, validation, and export |
| `Weather` | EPW parsing, provenance analysis, diagnostics, and weather export |
| `Generation` | Generation-information workbook import and export |
| `Scenarios` | Scenario input adaptation, dispatch orchestration, and result export |

`NEM.CLI.Tests` mirrors the same feature folders.

## Local development

NemSim currently targets .NET 10.

```powershell
dotnet build .\NemSim.slnx
dotnet test .\NemSim.slnx
dotnet run --project .\NEM.CLI\NEM.CLI.csproj
dotnet run --project .\NEM.Web\NEM.Web.csproj
```

The web project is then available at the URL printed by `dotnet run`.

### Validation runs

Run the committed hand-calculated dispatch fixtures with:

```powershell
dotnet test .\NEM.Model.Tests\NEM.Model.Tests.csproj `
  --filter FullyQualifiedName~ManualScenarioFixtureTests
```

Run the synthetic 8,760-hour storage-sizing acceptance in Release mode with:

```powershell
dotnet test .\NEM.Model.Tests\NEM.Model.Tests.csproj -c Release `
  --filter FullyQualifiedName~FullYearSizingAcceptanceTests `
  --logger "console;verbosity=detailed"
```

The full-year test prints solver wall-clock time, dispatch-pass count, and the
selected Battery capacity. Treat a runtime above a few minutes as a scope issue
to record rather than an automatic optimisation task.

Copy `NEM.CLI/appsettings.example.json` to
`NEM.CLI/appsettings.local.json` for machine-local scenario settings. The local
file is ignored by Git; the example is used as the fallback when it is absent.
Scenario results record the schema version and SHA-256 digest of the exact
demand and weather artifact bytes used by the run.

Regenerate the committed dispatch artifact from the committed demand and weather
inputs with:

```powershell
dotnet run --project .\NEM.CLI\NEM.CLI.csproj -- --run-scenario
```
