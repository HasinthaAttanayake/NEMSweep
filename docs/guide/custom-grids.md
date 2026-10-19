# Run a synthetic custom grid

This example exercises portable bundle ingestion, a custom region ID, both scenario execution modes,
and a mixed-mode sweep without downloading or committing source data. It generates a full annual
half-hour demand archive and a full non-leap hourly EPW file from deterministic synthetic values.
It is a plumbing example only: the invented demand, weather, costs and fleet are not a forecast, a
measured site, or a recommended system.

## Prerequisites

**This example needs PowerShell**, because the input generator is a PowerShell script. Windows
PowerShell 5.1 or [PowerShell 7+](https://learn.microsoft.com/powershell/scripting/install/installing-powershell)
(`pwsh`) both work, and `pwsh` is a cross-platform install on Linux and macOS. The script itself
uses only PowerShell and the .NET runtime the CLI already needs; there is no second toolchain.

If you cannot install PowerShell, use [Hosted starters](synthetic-starters.md) instead:
`nemsweep --quick-start simple` prepares an equivalent synthetic one-region study with no scripting.

## Generate the inputs

From the repository root, run the checked-in PowerShell generator. It writes its results under an
ignored `examples/custom-grid/generated/` directory:

```powershell
.\examples\custom-grid\Generate-SyntheticGrid.ps1
```

Use `-OutputDirectory <directory>` to generate elsewhere. The sweep records the generated baseline's
absolute path, so it remains runnable from the repository root with a custom output directory.
Use that directory in place of `examples/custom-grid/generated` in the commands below.

If your shell does not execute local scripts by default, invoke it with your normal PowerShell
execution-policy choice, for example:

```powershell
pwsh -File .\examples\custom-grid\Generate-SyntheticGrid.ps1
```

The script creates:

- a format-2 `bundle/manifest.json` mapping the preserved ID `Island-X` to `weather/station-01/`;
- one ZIP under `bundle/demand/operational-demand-hh/` containing all 17,520 half-hour
  `OPERATIONAL_DEMAND` actual records for 2025 at UTC+10;
- one 8,760-row synthetic EPW typical year, with complete hourly timestamps and the weather columns
  ingestion consumes;
- a fixed-fleet scenario, a sizeBattery scenario, a fixed-fleet sweep baseline and a version-2
  sweep that overrides `executionMode` to compare fixed dispatch with bounded Battery sizing.

Format 2 needs no generation-information workbook. The outputs are small and reproducible; the
script uses only PowerShell and the .NET runtime already needed by the CLI.

## Validate and ingest

```powershell
dotnet run --project .\NEMSweep.CLI -- --validate-inputs .\examples\custom-grid\generated\bundle

dotnet run --project .\NEMSweep.CLI -- --ingest .\examples\custom-grid\generated\bundle `
  --data-root .\examples\custom-grid\generated\data
```

Validation is read-only. Ingestion parses every demand and weather source before writing anything.
The custom ID is encoded in filenames as `region-49534C414E442D58`; the original `Island-X` remains
the region identity in the artifacts and scenario. This demonstrates why the bundle's readable
`weatherDirectory` is not derived from the region ID.

## Run both modes and the sweep

```powershell
dotnet run --project .\NEMSweep.CLI -- --run-scenario .\examples\custom-grid\generated\scenarios\fixed-scenario.json `
  --data-root .\examples\custom-grid\generated\data `
  --output .\examples\custom-grid\generated\fixed-results

dotnet run --project .\NEMSweep.CLI -- --run-scenario .\examples\custom-grid\generated\scenarios\sized-scenario.json `
  --data-root .\examples\custom-grid\generated\data `
  --output .\examples\custom-grid\generated\sized-results

dotnet run --project .\NEMSweep.CLI -- --run-sweep .\examples\custom-grid\generated\sweeps\mode-sweep.json `
  --data-root .\examples\custom-grid\generated\data `
  --output .\examples\custom-grid\generated\sweep-results
```

The fixed scenario uses only its declared 80 MW Gas fleet and an empty storage fleet array. The sized
scenario uses a 50 MW fleet and declares a zero-capacity Battery with its cost and technology
assumptions before asking the bounded Battery search to meet its reliability target. The sweep
has a fixed-fleet baseline: one point keeps that mode, while the other switches to `sizeBattery` and
supplies `storageSizing` bounds. Its point labels and axis values are deliberately illustrative,
not a quantitative comparison of unlike physical dimensions.

The model's core remains deliberately bounded: one-hour dispatch, one fixed market-time offset,
scenario-declared generation/storage/interconnectors, fixed technology behavior, and AUD cost
assumptions. There is no arbitrary dispatch-framework selector or network-charging block in these
configs. See [Scenario configuration](scenarios.md), [Sweeps](sweeps.md), and
[Model limitations](../assumptions/limitations.md) before adapting the example to a real study.