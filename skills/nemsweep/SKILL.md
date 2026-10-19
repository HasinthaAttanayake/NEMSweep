---
name: nemsweep
description: Install and run NEMSweep, a deterministic grid dispatch, reliability, battery-sizing and system-cost model. Use when asked whether a described electricity system could meet demand hour by hour, how much battery storage it would need, or what that system costs per MWh served.
---

# NEMSweep

## What NEMSweep is and isn't

- It dispatches a declared system in merit order by short-run marginal cost for every hour of the demand and weather period you supply, grows battery capacity where the reliability target is missed, and reports reliability, system cost and emissions.
- It is not a market model and not a forecast: no bidding, no unit commitment, no stochastic draws. One system against one supplied profile.
- Only battery capacity is sized. Everything else is exactly what the scenario declares.
- Its cost is the levelised **system** cost of building and running that system, per MWh served. Not a bill: no network, retail, scheme or tax costs.

Full list, with what each means for a report: [engine limits](#engine-limits).

## Preflight checklist

```bash
dotnet --version
```

Reports `10.x` → **route A**. Otherwise:

```bash
docker info
```

Succeeds → **route B**. Neither: ask the user before installing the .NET SDK. If you cannot ask, install it in user space and say so in your report:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
```

```bash
export DOTNET_ROOT="$HOME/.dotnet" && export PATH="$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools"
```

If both .NET and Docker are present, use route A unless the user asked for a container.

**Linux notes.**

- `dotnet tool install --global` writes to `~/.dotnet/tools`, which a fresh shell does not search, so `nemsweep` reports `command not found` after a successful install. Fix with `export PATH="$PATH:$HOME/.dotnet/tools"`.
- .NET needs the ICU libraries. A minimal image without them fails on first run with a `System.Globalization` stack trace. Install the distribution's package: `apt-get install -y libicu-dev`, or `dnf install -y libicu`. That is the route to prefer. `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` is a fallback where you cannot install packages: NEMSweep formats and parses invariantly throughout and needs no time-zone database, and its test suite passes with the variable set, though that was checked on a machine that had ICU rather than on an ICU-less image. If you use it, say so when you report, and run `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test NEMSweep.slnx` on that image first if you can.

## Questions to ask the user

Only these, and only where the request has not already answered them.

| Question | Default if unanswered |
|---|---|
| The NEM worked example, or their own grid data? | The NEM worked example (`--quick-start advanced`). Use Simple if the task is only to check the install. |
| What question, or which input to vary? | Reproduce the worked example as it stands and report it. Do not invent a variation. |
| Where should outputs go? | `./out` under the study directory. |

Their own data needs an [input bundle](https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/docs/guide/input-bundles.md) and `nemsweep --ingest`, not a starter.

## Route A: .NET tool

```bash
dotnet tool install --global NEMSweep.CLI --version 0.3.0
```

```bash
nemsweep --version
```

Expected:

```text
NEMSweep.CLI 0.3.0+<commit>
```

The suffix identifies the commit the package was built at; `<commit>` is a placeholder, not literal output. `--tool-path ./tools` instead of `--global` keeps the install local to a directory; invoke it as `./tools/nemsweep`.

This guide targets 0.3.0, including `--agent-guide`, `--points`, `--fail-on-unmet`, sweep summaries,
`storageCapacity` and `limitations`. Installation requires 0.3.0 package publication; until then
build this source revision (see the [release target notice](https://github.com/HasinthaAttanayake/NEMSweep#install)).
Do not assume an older installed tool or the moving container tag has these features.
Check `--help` and `--version` first.

On builds that support it, `nemsweep --agent-guide` prints the copy of this guide embedded in the installed build. Prefer it to fetching this file: it describes the binary you actually have, and the two disagree whenever the installed version is not the latest.

```bash
nemsweep --agent-guide
```

## Route B: container

```bash
docker pull ghcr.io/hasinthaattanayake/nemsweep:latest
```

```bash
docker run --rm ghcr.io/hasinthaattanayake/nemsweep:latest --version
```

The image is tagged `latest` and by commit SHA, for `linux/amd64` and `linux/arm64`. **It is not tagged with a release version**, so `--version` reports whatever `latest` was built from, which may be older or newer than the 0.3.0 release target. Podman takes the same arguments.

Three constraints: the image runs as a non-root user, so mounts must permit that user to write; use **absolute** command-line paths to avoid depending on the container's working directory (relative paths are supported but do not automatically resolve against your mount); and the data root defaults to `/data`, the output root to `/out`.

```bash
docker run --rm -v "$PWD/study:/work:ro" -v "$PWD/study/out:/out" ghcr.io/hasinthaattanayake/nemsweep:latest --run-scenario /work/scenarios/sized-scenario.json --data-root /work/data --output /out
```

**Prepare on the host or inside the container.** Container quick-start needs network access and a writable parent mount. Its destination must be a new child directory, not the mount point itself. For example, with the current directory mounted at `/work`, prepare `/work/study`:

```bash
docker run --rm -v "$PWD:/work" ghcr.io/hasinthaattanayake/nemsweep:latest --quick-start simple /work/study
```

Then run the scenario using the read-only study mount and writable output mount above. Alternatively, prepare with route A, or fetch the bundle from the [NEMSweep.Samples release](https://github.com/HasinthaAttanayake/NEMSweep.Samples/releases/tag/v2026.10.1) and `--ingest` it. Compare host and container results using the same CLI build and inputs; `runId` identifies each execution.

## Starters

Neither starter runs anything. Both download, verify and ingest into a **new** directory that must not already exist, and both reject `--data-root`, `--output`, `--csv` and `--format`.

**Simple is the smallest**: one synthetic region, Island-X, runs in seconds. Use it to confirm an install works.

```bash
nemsweep --quick-start simple ./my-first-study
```

**Advanced is the NEM worked example**: five real NEM regions, FY2025-26 AEMO demand, TMYx weather. A sized run takes about a minute.

```bash
nemsweep --quick-start advanced ./my-nem-study
```

Each creates:

```text
<study>/
  README.md
  bundle/      raw format-2 manifest, demand archives and EPW weather
  data/        ingested per-region demand-*.json and weather-*.json
  scenarios/   fixed-scenario.json, sized-scenario.json, mode-sweep-baseline.json
  sweeps/      demo.json, plus datacentre-full.json in Advanced only
```

Run from the study root; sweep baseline paths resolve against the working directory.

```bash
nemsweep --run-scenario scenarios/fixed-scenario.json --data-root ./data --output ./out/fixed
```

```bash
nemsweep --run-scenario scenarios/sized-scenario.json --data-root ./data --output ./out/sized
```

```bash
nemsweep --run-sweep sweeps/demo.json --data-root ./data --output ./out/demo --csv
```

`fixed-scenario.json` dispatches the declared fleet; `sized-scenario.json` runs the battery search; `sweeps/demo.json` compares those two modes against one shared baseline. Note that the sweep's `fixed` point patches `mode-sweep-baseline.json`, which is **not** the same fleet as `scenarios/fixed-scenario.json`.

## Smoke test: does this install work?

**This checks the install. It is not a template, and its figures are not a benchmark for any other study.** They are facts about the NEM sample data at engine commit `7f44412`.

Prepare Advanced, then run point `p9` of the 25-point data-centre sweep, which adds 4,500 MW.

```bash
nemsweep --quick-start advanced ./my-nem-study
```

```bash
cd ./my-nem-study
```

```bash
nemsweep --fan-out-sweep sweeps/datacentre-full.json --data-root ./data --output ./out
```

```bash
nemsweep --run-scenario sweeps/australian-nem-datacentre-fy2026/configs/p9.json --data-root ./data --output ./out/p9
```

Read `./out/p9/results-overview.json`:

| Path in the file | Expected |
|---|---|
| `cost.slcoeAudPerMwh` | `144.45` |
| `storageSizing.finalEnergyMwh` | `97640.8` |
| `storageSizing.initialEnergyMwh` | `14387.1` |
| `reliability.withinTarget` | `true` |

`reliability.achievedUsePercentageOfDemand` should be about `0.00137`, which is 0.00137% of demand, under the 0.002% target.

If these do not match, **stop**. Report `nemsweep --version` and the result's `provenance.gitCommitSha`, and do not present figures from that install as reproducing the worked example.

## Recipes

Field paths below come from `nemsweep --describe-schema scenario` and `nemsweep --describe-schema sweep`. Run those for the exact `schemaVersion` the installed CLI accepts; at 0.3.0 it is scenario 7 and sweep 2.

**The examples below are written against the Simple starter**, whose one region is `Island-X` and whose `scenarios/sized-scenario.json` declares a `Gas` fleet at 10 AUD/GJ and a zero-capacity `Battery` at 200,000 AUD/MW and 150,000 AUD/MWh. Against Advanced, or your own data, keep the shape and change the region IDs and baseline values: read the baseline config first, since a patch names fields by their value in that file.

### Validate before running

Always: a dispatch is minutes, validation is seconds.

```bash
nemsweep --validate-scenario scenarios/sized-scenario.json --data-root ./data --format json
```

```json
{"id":"synthetic-simple-sized","interconnectors":0,"path":"scenarios/sized-scenario.json","regions":["Island-X"],"schemaVersion":7,"valid":true}
```

For a sweep, fan out. It applies every point's patch, writes the resulting config and validates each one, stopping at the first bad point, without dispatching anything.

```bash
nemsweep --fan-out-sweep sweeps/my-sweep.json --data-root ./data --output ./out
```

Deserialisation is strict: any unknown property anywhere in a config is a hard error, not a silently ignored value. `demandFile`, `weatherFile` and `dataCentreNameplateMw` are per region, and a config setting them at the root is rejected.

### Run chosen sweep points

```bash
nemsweep --run-sweep sweeps/datacentre-full.json --data-root ./data --output ./out --points p0,p9,p10
```

An ID the definition does not declare is an error, and the message lists the ones it does.

**A run selecting fewer than all defined points is partial, and deliberately leaves the sweep's aggregate artifacts alone.** It publishes each selected point's result, status and config, and does not rewrite `index.json`, the sweep manifest or the summary files, because rebuilding the index from a subset would drop every point the run did not touch. It says so on stdout. Selecting every point with `--points` is a full run; rerun without the flag when you want the whole published sweep current.

On an older CLI, or to get an ordinary `results*.json` set instead of a sweep artifact, fan out and run one config. Each point config is a complete standalone scenario, so it reproduces exactly what that point would have run.

```bash
nemsweep --fan-out-sweep sweeps/datacentre-full.json --data-root ./data --output ./out
```

```bash
for point in p0 p9 p10; do nemsweep --run-scenario "sweeps/australian-nem-datacentre-fy2026/configs/$point.json" --data-root ./data --output "./out/$point"; done
```

Do not hand-trim the sweep definition: a trimmed copy publishes over the full sweep's output directory unless you also change its `sweepId`, and it becomes a second file to keep in step.

Point IDs do not encode axis values. `p9` is 4,500 MW only because the definition says so. `--fan-out-sweep` prints the mapping and writes it as `sweeps/<sweepId>/configs/points.csv`. It is also in the definition's `points[]`, in `sweeps/<sweepId>/index.json` after a run, and in the `Running sweep point p9 (Data centre nameplate added=4500 MW).` line `--run-sweep` prints per point.

### Change one input: gas fuel price x 1.5

JSON path: `regions[].generatingFleets[].costParameters.fuelPriceAudPerGj`. Per fleet, per region, so change it in every region declaring `Gas`.

Either edit a copy of the scenario, give it a new `id`, validate and run it:

```bash
python -c "import json; d=json.load(open('scenarios/sized-scenario.json')); [f['costParameters'].__setitem__('fuelPriceAudPerGj', 15) for r in d['regions'] for f in r['generatingFleets'] if f['technology']=='Gas']; d.__setitem__('id', d['id']+'-gas15'); json.dump(d, open('scenarios/sized-gas15.json','w'), indent=2)"
```

```bash
nemsweep --validate-scenario scenarios/sized-gas15.json --data-root ./data --format json
```

```bash
nemsweep --run-scenario scenarios/sized-gas15.json --data-root ./data --output ./out/gas15
```

Compare `cost.slcoeAudPerMwh` and `reliability.achievedUsePercentageOfDemand` between the two runs' `results-overview.json`.

Or write a two-point sweep, which keeps baseline and variant in one artifact:

```json
{
  "schemaVersion": 2,
  "sweepId": "gas-price-x15",
  "name": "Gas fuel price +50%",
  "axis": { "label": "Gas fuel price", "unit": "AUD/GJ" },
  "baselineConfigPath": "scenarios/sized-scenario.json",
  "points": [
    { "pointId": "base", "axisValue": 10, "label": "10 AUD/GJ (baseline)", "overrides": {} },
    {
      "pointId": "x15",
      "axisValue": 15,
      "label": "15 AUD/GJ (+50%)",
      "overrides": {
        "regions": [
          {
            "regionId": "Island-X",
            "generatingFleets": [
              { "technology": "Gas", "costParameters": { "fuelPriceAudPerGj": 15 } }
            ]
          }
        ]
      }
    }
  ]
}
```

```bash
nemsweep --run-sweep sweeps/gas-price.json --data-root ./data --output ./out/gas --csv
```

Naming one field is enough: `regions`, `generatingFleets` and `storageFleets` merge by key (`regionId`, `technology`), so everything unnamed carries over from the baseline. Fuel price is a **dispatch** input, so expect the physical result to move, not only the cost.

### Sweep a cost parameter: battery capital cost, five points

JSON paths: `regions[].storageFleets[].costParameters.powerCapitalCostAudPerMw` and `.energyCapitalCostAudPerMwh`. One point per multiple of the baseline's two values — 50%, 75%, 100%, 125%, 150% — each patching the `Battery` fleet, with `axisValue` matching:

```json
{
  "pointId": "p50",
  "axisValue": 50,
  "label": "50% of baseline battery capital cost",
  "overrides": {
    "regions": [
      {
        "regionId": "Island-X",
        "storageFleets": [
          {
            "technology": "Battery",
            "costParameters": {
              "powerCapitalCostAudPerMw": 100000,
              "energyCapitalCostAudPerMwh": 75000
            }
          }
        ]
      }
    ]
  }
}
```

```bash
nemsweep --run-sweep sweeps/battery-capex.json --data-root ./data --output ./out/capex --csv
```

**Expect the installed storage to be identical at every point.** The sizing search is driven by the reliability target, not by cost, so a capital-cost sweep reprices one fixed system rather than showing what a cheaper battery would have led someone to build. Say that when you report it. The summary table the run prints makes it obvious:

```text
point  Battery capital cost (% of baseline)  target  unmet %  battery MWh   SLCoE
p50                                      50     met   0.0000        120.0   94.47
p150                                    150     met   0.0000        120.0  100.34
```

Nothing cross-checks `axisValue` against `overrides`. A sweep whose axis has no relationship to what it varied will run and chart perfectly normally.

## Reading results

```text
<output root>/
  results.json                      whole-system detail, every interval
  results-overview.json             whole-system totals            <- start here
  results-<region>.json             per-region detail
  results-<region>-overview.json    per-region totals
  csv/                              only with --csv
  sweeps/<sweepId>/
    index.json                      axis, provenance, every point's scalars   <- start here
    configs/<pointId>.json          the resolved scenario that point ran
    points/<pointId>.json           that point's full dispatch result
    points/<pointId>.status.json    whether the run completed, and reliabilityTargetMet
    summary.json                    one row per point: what the sweep found   <- read this
    summary.csv                     the same rows as CSV
    csv/points/<pointId>/fact_scalars.csv    only with --csv
```

The sweep directory is named after the definition's `sweepId` field, not its filename. `--fan-out-sweep` writes its configs, and `points.csv` beside them, to `sweeps/<sweepId>/configs/` under your **working directory** instead; a run copies each config into the output root.

| Field | Unit | Note |
|---|---|---|
| `cost.slcoeAudPerMwh` | AUD per MWh **served** | System levelised cost. |
| `cost.generationSlcoeAudPerMwh`, `storageSlcoeAudPerMwh`, `transmissionSlcotAudPerMwh` | AUD/MWh served | Sum to `slcoeAudPerMwh`. |
| `cost.totalAnnualisedCostAud` | AUD per year | The SLCoE numerator. |
| `storageCapacity.totalBatteryEnergyMwh`, `totalBatteryPowerMw` | MWh, MW | **Battery only**, installed. Read this for battery. |
| `storageCapacity.declaredBatteryEnergyMwh`, `addedBatteryEnergyMwh` | MWh | Battery before sizing, and what sizing added. |
| `storageCapacity.pumpedHydroEnergyMwh`, `otherStorageEnergyMwh` | MWh | Declared storage the search cannot grow. |
| `storageSizing.finalEnergyMwh`, `finalPowerMw` | MWh, MW | **Battery only**, after sizing. Same as `totalBattery*`. |
| `storageSizing.initialEnergyMwh`, `initialPowerMw` | MWh, MW | **Battery only**, as declared. |
| `storageSizing.outcome` | label | `notRequired`, `resized`, or what stopped the search. |
| `regionSummariesById.<id>.storageEnergyMwh` | MWh | **All storage fleets**, pumped hydro included. |
| `storageEnergyMwh` (sweep scalar, CSV column) | MWh | **All storage fleets**, summed over regions. |
| `totalBatteryEnergyMwh` (sweep scalar, CSV column) | MWh | **Battery only**. The sweep-side equivalent of `storageCapacity`. |
| `limitations.engine`, `limitations.thisRun` | prose | What this result cannot be read as saying. |
| `reliability.achievedUsePercentageOfDemand` | **percent** of demand | Already a percent. |
| `reliability.targetUsePercentageOfDemand` | **percent** of demand | Same scale. |
| `reliability.withinTarget` | boolean | The thing to check. |
| `metrics.unservedEnergyMwh`, `unservedHours`, `peakUnservedPowerMw` | MWh, hours, MW | |
| `metrics.demandMwh` | MWh | Demand, including any declared data-centre load. |
| `metrics.deliveredGenerationMwh` | MWh | Generation after curtailment and charging. **Not** energy served. |
| `energyServedMwh` (sweep scalar) | MWh | Demand minus unserved. The levelised-cost denominator. |
| `emissions.emissionsIntensityTonnesCO2ePerMwh` | t CO2-e per MWh **served** | Combustion only, not life-cycle. |
| `executionMode` | label | `fixedFleet` or `sizeBattery`. |

**SLCoE** is the annualised cost of building and running the declared system, divided by energy served, in the scenario's `costBasis.year` dollars. Energy served is demand minus unserved energy (`NEMSweep.Model/Simulation/SystemDispatchOutcome.cs`); the division is in `NEMSweep.Model/Economics/PowerSystemCostBreakdown.cs`. Not demand, and not delivered generation. It excludes network, retail, scheme and tax costs, so it is not comparable to a retail tariff.

**Battery versus total storage.** Only `Battery` fleets are sized. Read `storageCapacity`, which splits installed storage into declared battery, added battery, total battery, pumped hydro and other, and whose energy fields sum to the installed total. `storageEnergyMwh` counts every storage fleet including pumped hydro: in the NEM worked example the gap is 15,680 MWh of fixed pumped hydro, so quoting it as "battery built" overstates the battery by that much at every point. The sweep scalars and the CSV carry the same split as `totalBatteryEnergyMwh` and its siblings.

**Reliability units.** `100 * (unserved / demand)` (`NEMSweep.Model/Simulation/ReliabilityMetrics.cs`). `0.0241` is 0.0241% of demand. Multiplying by 100 again is a hundredfold error that looks plausible.

**Every run states its reliability verdict**, and a miss still exits 0 unless you ask otherwise:

```text
reliability: achieved 22.2222% vs target 0.0100% of demand: NOT met
WARNING: reliability target not met (achieved 22.2222% unserved energy, target 0.0100%).
```

Pass `--fail-on-unmet` to `--run-scenario` or `--run-sweep` to exit 1 on a miss instead; on a sweep it names the points that missed, on standard error. Without the flag the exit code is unchanged, because the run itself completed.

```bash
nemsweep --run-scenario scenarios/sized-scenario.json --data-root ./data --output ./out --fail-on-unmet
```

In the artifacts, `reliability.withinTarget` is `false` system-wide, per region, and in each point's entry in a sweep index; `points/<pointId>.status.json` and `summary.json` carry `reliabilityTargetMet`. Note that a point's `status` answers a different question: one that dispatched a full period and missed its target is `succeeded` with `reliabilityTargetMet: false`. A sweep exits 1 on its own only when a point failed to run at all.

**Provenance.** Every result records, per region, the filename, schema version and SHA-256 of the exact input bytes it parsed (`dataSourcesByRegion`), plus the weather basis in prose. A whole-system result carries `provenance.gitCommitSha`, the commit the binary was built at, and `runId`. The digest, not the file path, is the reproducibility boundary. `runId` is the only value that differs between two otherwise identical reruns.

## Reporting results

- **The result tells you most of this itself.** Read `limitations.engine` and `limitations.thisRun` on the result: the first is the engine's limits, the second is what this run's own evidence says (the period the target was tested over, what stopped the sizing search, what the weather input represents). Use them; do not paste them wholesale.
- **Every figure carries its basis.** Which data, which scenario and which overrides; the version from `nemsweep --version` and the commit from `provenance.gitCommitSha`. A figure without those cannot be checked by anyone.
- **Go through the engine limits below and say which ones bear on this question, and how.** Pick the ones that matter; do not paste the list. Asked whether storage can absorb a load increase, the no-foresight battery rule and the single-period reliability test are material. Asked about cost, the exclusion of network and retail costs is.
- **Caveat the data you actually used, not the data in the example.** The worked-example facts below apply only when the run used the NEM sample. For the user's own data, describe what they supplied: how many weather years it covers, whether it contains extreme years, what period the demand is from.
- **Prefer differences to levels.** The gap between two scenarios on the same data is the trustworthy output. The level of a single run inherits every assumption in the scenario.
- Report a missed target as a missed target, with `achievedUsePercentageOfDemand` against `targetUsePercentageOfDemand`, and say what `storageSizing.outcome` gives as the reason.

### Engine limits

True of every run, whatever the data.

1. Each run is one system against the demand and weather supplied, with no random draws. It reports a realised outcome, not an expectation. Differences between scenarios are more trustworthy than the level of any single run.
2. Generators dispatch in merit order by short-run marginal cost (`NEMSweep.Model/Simulation/GenerationMeritOrder.cs`). No bidding, no unit commitment, no price forecast, no investment valuation.
3. Only battery capacity is sized (`NEMSweep.Model/StorageSizing/StorageSizingSearch.cs`). Pumped hydro, generation and transmission stay as the scenario declares them.
4. Battery charge and discharge follows a fixed rule with **no foresight** (`NEMSweep.Model/Simulation/GreedySurplusAndIncrementalGenerationChargingPolicy.cs`): it sees the current interval only, so the size it finds can be too small against a multi-day drought or too large against a system able to arbitrage.
5. Where the search stops at a limit, it has **not** proved that no battery would work. `storageSizing.outcome` says what stopped it; only the outcome reporting system generation energy below demand energy establishes that no battery size could have met the target.
6. The reliability target is whatever the scenario sets, and it is tested **over the modelled period only** — typically one year. It is not a multi-year expectation.
7. Cost is the system levelised cost of building and running the declared system, per MWh served. It is not a bill: network, retail, scheme and tax costs are excluded.

### Worked-example facts

True only of the NEM sample data in the Advanced starter. Do not carry these into a report on the user's own data.

- Weather is one typical meteorological year per region, assembled from 2011-2025 observations. Hard years such as a long wind drought are absent, so storage needs are probably understated.
- The fleet is the FY2025-26 NEM, with about 16,000 MWh of pumped hydro held fixed and excluded from the battery figures.
- The target is 0.002% of demand unmet, the level of the NEM reliability standard.
- Added load is flat, full-load-factor data-centre demand spread across the regions. No new generation and no new transmission are added.
- The published figures:

| Added load | Meets target | Unmet demand | Battery energy capacity | Cost per MWh served |
|---|---|---|---|---|
| 0 MW | yes | n/a | 14,387 MWh | $148.40 |
| 3,500 MW | yes | n/a | n/a | $141.17 (lowest) |
| 4,500 MW | yes | 0.001% | 97,641 MWh | $144.45 |
| 5,000 MW | no | 0.024% | n/a | n/a |

- Past 5,000 MW the sweep cannot say whether generation, transmission or better-planned batteries would lift the limit. It varied none of those.

## Read more

Raw Markdown, which is what to fetch rather than the rendered site:

- Getting started: <https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/docs/guide/index.md>
- CLI reference: <https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/docs/guide/cli.md>
- Scenario configuration: <https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/docs/guide/scenarios.md>
- Sweeps: <https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/docs/guide/sweeps.md>
- Outputs and provenance: <https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/docs/guide/outputs.md>
- Hosted starters: <https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/docs/guide/synthetic-starters.md>
- Input bundles: <https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/docs/guide/input-bundles.md>
- Running the container: <https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/docs/guide/container.md>
- Limitations: <https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/docs/assumptions/limitations.md>
