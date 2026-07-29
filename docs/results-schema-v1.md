# `results.json` schema v1

`NEM.Web/wwwroot/data/results.json` is the versioned output of the baseline
merit-order dispatch scenario. Regenerate it from committed source artifacts:

```powershell
dotnet run --project .\NEM.CLI\NEM.CLI.csproj -- --run-scenario
```

The command reads the paths and fleet assumptions in `NEM.CLI/appsettings.json`.
Demand is resampled to hourly average MW. Typical-year weather is matched to the
demand timeline by month, day, and hour.

## Top-level fields

| Field | Meaning |
| --- | --- |
| `schemaVersion` | Contract version; consumers must assert `1` |
| `scenario` | Scenario ID, region, period bounds, and interval resolution |
| `generatedAt` | UTC timestamp when the artifact was generated |
| `dataSources` | Demand archive names and weather source filename |
| `assumptions` | Scenario description and configured aggregate fleets |
| `dataSeries` | Aligned hourly power series |
| `metrics` | Whole-period energy and reliability summaries |
| `cost` | Generation cost result and calculation status |

## Units and balance

Every numeric physical or monetary field carries its unit in the field name:

- `*Mw`: interval-average power in megawatts
- `*Mwh`: energy in megawatt-hours
- `*Aud`: Australian dollars
- `*AudPerMwh`: Australian dollars per megawatt-hour
- `hoursServedFraction`: dimensionless value from 0 to 1

`generationByTechnologyMw` contains generation delivered to load. Renewable
availability constrained off is excluded and reported in `curtailmentMw`.
For each interval, the current single-region model guarantees:

```text
sum(generationByTechnologyMw) + unservedDemandMw = demandMw
```

Generation cost fields remain `null` with status `pending NEM-018` until issue
#24 supplies the levelised generation-cost calculation. This is explicit in v1
and avoids publishing a fabricated zero cost.