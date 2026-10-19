# Outputs and provenance

This page is for reading the JSON NEMSweep produces, or consuming it programmatically. Every
artifact described here is written by `NEMSweep.CLI`: the framework itself returns objects, not
files. Results and sweeps go under the **output root**, and the imported inputs a scenario reads go
under the **data root**. Both are yours to choose; see the
[CLI reference](cli.md#the-workspace).

The `storageCapacity` and `limitations` blocks and sweep summaries described below are included
in the 0.3.0 release target (see the [publication notice](https://github.com/HasinthaAttanayake/NEMSweep#install)); older artifacts
do not gain them automatically. The 0.3.0 whole-system detail, overview and sweep-index schema
versions are 18, 7 and 14 respectively (0.2.0 emitted 17, 6 and 13).

## Artifact map

| Path | What it is |
|---|---|
| `results.json` | Whole-system dispatch evidence for the run started by `--run-scenario`. The same shape is used whether the scenario declares one region or several. |
| `results-overview.json` | A compact version of the same run: totals and summaries, without the interval-by-interval series. |
| `results-{region}.json` | Full dispatch evidence for one region of a whole-system run. |
| `results-{region}-overview.json` | The compact counterpart of `results-{region}.json`. |
| `demand-{region}.json` | The imported operational-demand artifact for one region. |
| `weather-{region}.json` | The imported weather artifact for one region. |
| `generation-information.json` | The imported generation-fleet workbook, as data. |
| `sweeps/index.json` | Manifest of every published sweep: sweep ID, name, and the path to its index. Rewritten from what is on disk after every full sweep run; unchanged by partial runs. |
| `sweeps/{sweepId}/index.json` | One sweep's index: its axis, scope, provenance, and one entry per point. |
| `sweeps/{sweepId}/points/{pointId}.json` | Full dispatch result for one sweep point (same shape as a `results.json` / `results-{region}.json`). |
| `sweeps/{sweepId}/points/{pointId}.status.json` | Whether that point succeeded or failed, the failure detail if it did, and `reliabilityTargetMet`. |
| `sweeps/{sweepId}/summary.json` | One row per point: the headline result of the sweep. See [the summary](sweeps.md#the-summary-what-the-sweep-found). |
| `sweeps/{sweepId}/summary.csv` | The same rows as CSV. |
| `sweeps/{sweepId}/configs/{pointId}.json` | The fully-resolved scenario config that point ran, produced by merging the point's overrides onto the sweep's baseline config. |
| `sweeps/{sweepId}/series/base-demand-{sha256}.json` | A base-demand series externalised from one or more points that share it byte-for-byte. See below. |

Read `NEMSweep.CLI/Scenarios/DispatchResultsExport.cs` and `NEMSweep.CLI/Scenarios/SweepArtifactExport.cs` if
you need the exact shape beyond what this page covers.

### The output tree

What the two commands actually leave on disk, under whatever you passed to `--output`. A scenario
run writes flat files; a sweep writes a directory per sweep.

```text
<output root>/
  results.json                      whole-system detail, every interval
  results-overview.json             whole-system totals            <- start here
  results-<region>.json             per-region detail
  results-<region>-overview.json    per-region totals
  csv/                              only with --csv
```

```text
<output root>/
  sweeps/
    index.json                      every published sweep
    <sweepId>/
      index.json                    this sweep's axis, provenance, and every point's scalars
      configs/<pointId>.json        the resolved scenario each point ran
      points/<pointId>.json         that point's full dispatch result
      points/<pointId>.status.json  succeeded or failed, why, and the reliability verdict
      summary.json                  one row per point: what the sweep found
      summary.csv                   the same rows as CSV
      series/base-demand-<sha>.json demand series shared between points
      csv/                          only with --csv
```

`<sweepId>` is the sweep definition's own `sweepId` field, not a name derived from the file. See
[Sweeps](sweeps.md#where-the-output-lands).

One sweep artifact is **not** under the output root. `--fan-out-sweep` writes its generated point
configs, and the `points.csv` point-to-axis-value table beside them, to
`sweeps/<sweepId>/configs/` relative to your **working directory**: that is a working area next to
the sweep definition, deliberately separate from the published directory a run copies each config
into. See [Mapping point IDs to axis values](sweeps.md#mapping-point-ids-to-axis-values).

### Headline fields in `results-overview.json`

The compact whole-system file, and the one to read first. Field names are camelCase, and the same
blocks appear for each region under `regionSummariesById.<regionId>`.

| Field | Unit | Meaning |
|---|---|---|
| `reliability.achievedUsePercentageOfDemand` | **percent** of demand energy | Unserved energy actually reached. See [the units warning below](#reliability-is-a-percent-not-a-fraction). |
| `reliability.targetUsePercentageOfDemand` | **percent** of demand energy | The scenario's target, from `reliability.targetUsePercentage`. |
| `reliability.withinTarget` | boolean | Whether the target was met. |
| `reliability.standardName` | label | Descriptive only; no effect on dispatch. |
| `metrics.demandMwh` | MWh | Total demand over the period, any declared data-centre load included. |
| `metrics.unservedEnergyMwh` | MWh | Demand not served. |
| `metrics.unservedEnergyPercentageOfDemand` | percent | The same quantity and units as `reliability.achievedUsePercentageOfDemand`. |
| `metrics.unservedHours` | hours | Count of intervals with any unserved demand. |
| `metrics.peakUnservedPowerMw` | MW | Worst single interval. |
| `metrics.deliveredGenerationMwh` | MWh | Generation after curtailment and charging; **not** energy served. |
| `metrics.curtailedEnergyMwh` | MWh | Available output not used. |
| `cost.slcoeAudPerMwh` | AUD/MWh served | System levelised cost. See [SLCoE](#slcoe-what-it-is-and-what-it-divides-by). |
| `cost.generationSlcoeAudPerMwh` | AUD/MWh served | Generation component. |
| `cost.storageSlcoeAudPerMwh` | AUD/MWh served | Storage component. |
| `cost.transmissionSlcotAudPerMwh` | AUD/MWh served | Transmission component. `cost.transmissionCostStatus` says `calculated` or `notModelled`. |
| `cost.totalAnnualisedCostAud` | AUD per year | The SLCoE numerator. |
| `emissions.totalEmissionsTonnesCO2e` | t CO2-e | Operational combustion emissions, on gross generated energy. |
| `emissions.emissionsIntensityTonnesCO2ePerMwh` | t CO2-e per MWh **served** | Not the per-technology input assumption, which is per MWh generated. |
| `storageSizing.initialEnergyMwh`, `initialPowerMw` | MWh, MW | Battery as declared, before the search. |
| `storageSizing.finalEnergyMwh`, `finalPowerMw` | MWh, MW | Battery after the search. **Battery only.** |
| `storageSizing.outcome` | label | `notRequired`, `resized`, or what stopped the search. |
| `storageSizing.passesUsed` | count | Dispatch passes spent, against `storageSizing.maximumPasses`. |
| `storageSizing.trajectory` | array | Every candidate the search tried, with its unserved energy. |
| `executionMode` | label | `fixedFleet` or `sizeBattery`. |
| `periodStart`, `periodEnd`, `resolution` | timestamps, duration | The modelled period in market time, and the timestep. |
| `runId` | GUID | Identifies this run. The one value that differs between identical reruns. |
| `regionIds`, `topology.links` | n/a | The regions, and the directed interconnectors between them. |
| `dataSourcesByRegion.<regionId>` | n/a | Per-region input digests and the weather basis. See [Provenance](#provenance-and-reproducibility). |
| `storageCapacity` | MWh, MW | Installed storage split into declared, added, battery, pumped hydro and other. See [below](#battery-only-figures-versus-totals-that-include-pumped-hydro). |
| `limitations` | n/a | What this result cannot be read as saying. See [below](#limitations-what-this-result-cannot-say). |

A whole-system run also carries a `provenance` block with `gitCommitSha`. `results-overview.json`
has no top-level `storageEnergyMwh`: the installed-storage totals are per region, and the
battery-only figures are in `storageCapacity`, which the next section is about.

### Battery-only figures versus totals that include pumped hydro

**This is the easiest figure in NEMSweep to get wrong.** Only `Battery` fleets are sized
(`NEMSweep.Model/StorageSizing/StorageSizingSearch.cs`); `PumpedHydro`, and any other storage
technology, is dispatched exactly as the scenario declares it.

`storageCapacity` publishes the split directly, on the whole-system result and on each entry of
`regionSummariesById`:

| Field | Unit | What it counts |
|---|---|---|
| `storageCapacity.declaredBatteryEnergyMwh` / `declaredBatteryPowerMw` | MWh, MW | Battery the scenario declared, before sizing |
| `storageCapacity.addedBatteryEnergyMwh` / `addedBatteryPowerMw` | MWh, MW | Battery the sizing search added. Zero in `fixedFleet` |
| `storageCapacity.totalBatteryEnergyMwh` / `totalBatteryPowerMw` | MWh, MW | Battery installed in the dispatched system |
| `storageCapacity.pumpedHydroEnergyMwh` / `pumpedHydroPowerMw` | MWh, MW | Declared pumped hydro. Never sized |
| `storageCapacity.otherStorageEnergyMwh` / `otherStoragePowerMw` | MWh, MW | Any other declared storage technology. Never sized |

Total battery + pumped hydro + other storage reconciles with the installed storage energy the
result reports elsewhere, and the corresponding power fields likewise. Declared and added battery
are the components of total battery, not additional storage to sum again. From the worked example's
4,500 MW point:

```json
{
  "declaredBatteryEnergyMwh": 14387.1,
  "addedBatteryEnergyMwh": 83253.7,
  "totalBatteryEnergyMwh": 97640.8,
  "pumpedHydroEnergyMwh": 15680,
  "otherStorageEnergyMwh": 0
}
```

The older fields remain, and remain easy to misread:

| Field | Where | What it counts |
|---|---|---|
| `storageSizing.finalEnergyMwh` | system and per region | **Battery only**, after sizing. Equals `storageCapacity.totalBatteryEnergyMwh` |
| `storageSizing.initialEnergyMwh` | system and per region | **Battery only**, as declared |
| `regionSummariesById.<id>.storageEnergyMwh` | per region only | **All storage fleets**, battery and pumped hydro together |
| `storageEnergyMwh` sweep scalar | `sweeps/<id>/index.json`, `fact_scalars.csv` | **All storage fleets**: the sum of every region's `storageEnergyMwh` (`NEMSweep.CLI/Scenarios/SweepArtifactExport.cs`) |
| `storagePowerMw` sweep scalar | same | All storage fleets, by the same rule |

Sweep scalars and the CSV carry the split too, as `declaredBatteryEnergyMwh`,
`addedBatteryEnergyMwh`, `totalBatteryEnergyMwh`, `totalBatteryPowerMw`, `pumpedHydroEnergyMwh` and
`otherStorageEnergyMwh`. Use `totalBatteryEnergyMwh` where you would have reached for
`storageEnergyMwh` and meant battery.

In the NEM worked example the difference between the two is 15,680 MWh of existing pumped hydro,
held fixed across every point of the sweep. Quoting `storageEnergyMwh` as "battery built" therefore
overstates the battery by that amount at every point, and proportionally most at the points where
little battery was added.

### Reliability is a percent, not a fraction

`achievedUsePercentageOfDemand`, `targetUsePercentageOfDemand` and
`unservedEnergyPercentageOfDemand` are **already percentages**. The model computes
`100 * (unservedEnergy / totalDemand)` (`NEMSweep.Model/Simulation/ReliabilityMetrics.cs`), and a
scenario's `reliability.targetUsePercentage` is declared on the same scale, over `(0, 100]`.

So `0.0241` means 0.0241% of demand, roughly one part in four thousand. Multiplying it by 100 again
is a hundredfold error that lands on a plausible-looking number. The NEM reliability standard,
0.002% of demand, appears in these fields as `0.002`, not `0.00002`.

The console line every run prints names the unit for the same reason:

```text
reliability: achieved 0.0014% vs target 0.0020% of demand: met
```

### How a missed reliability target is signalled

Every run prints its reliability verdict, met or not, after the results path
(`NEMSweep.CLI/Scenarios/ScenarioCommand.cs`):

```text
reliability: achieved 22.2222% vs target 0.0100% of demand: NOT met
WARNING: reliability target not met (achieved 22.2222% unserved energy, target 0.0100%).
```

The `WARNING` line appears only on a miss; the `reliability:` line appears always, so a run that
says nothing about reliability is a run that did not get far enough to assess it, rather than one
whose reliability nobody checked.

**A missed target still exits 0 by default**, because the run itself completed. Pass
`--fail-on-unmet` to make it exit 1 instead:

```bash
nemsweep --run-scenario scenarios/sized-scenario.json --data-root ./data --output ./out --fail-on-unmet
```

On a sweep, `--fail-on-unmet` exits 1 if any point missed its target and names those points on
standard error. Without the flag the exit code is unchanged: 0, or 1 only when a point failed to
run at all.

Five places record the verdict:

1. The `reliability:` console line above, and the `WARNING` line on a miss.
2. `reliability.withinTarget` in `results-overview.json` and `results.json`, and in each region's
   own `reliability` block.
3. For a sweep, each point's entry in `sweeps/<sweepId>/index.json` carries the same `reliability`
   block, and `summary.json` / `summary.csv` carry `reliabilityTargetMet`.
4. `points/<pointId>.status.json` carries `reliabilityTargetMet`, alongside the `status` that
   answers the different question of whether the run completed:

   ```json
   {"axisValue":50,"failure":null,"pointId":"p50","reliabilityTargetMet":true,"status":"succeeded"}
   ```

   A point that dispatched a full period and missed its target is `succeeded` with
   `reliabilityTargetMet: false`. Read the field, not the status.
5. In `sizeBattery` mode, `storageSizing.outcome` says what stopped the search. Only the outcome
   reporting system generation energy below demand energy establishes that no battery size could
   have met the target; the others are a ceiling or a budget the search ran into. The
   [`limitations` block](#limitations-what-this-result-cannot-say) states which, in prose.

### `limitations`: what this result cannot say

Every whole-system result and overview carries a `limitations` block, so the figures do not travel
without them. Three parts:

| Field | What it holds |
|---|---|
| `limitations.engine` | Statements true of every run, whatever the data. Fixed text, so two results at the same model version carry the same list. |
| `limitations.thisRun` | Statements derived from this run's own evidence: the period the target was tested over, what stopped the sizing search, what the weather input represents, and whether the declared system includes storage the search cannot grow. |
| `limitations.reference` | A link to [Limitations](../assumptions/limitations.md), where the reasoning behind each engine statement is set out. |

Nothing in the block is specific to a dataset. A statement about the data a run consumed is derived
from that run's own input provenance (`dataSourcesByRegion`), not from knowledge of any particular
dataset, so a run on your own inputs gets statements about your inputs. From the worked example's
4,500 MW point:

```json
[
  "The reliability target of 0.002 per cent of demand energy was tested over 2025-07-01 to 2026-07-01 in market time and over no other period.",
  "The search grew battery energy by 83,253.7 MWh over 113 dispatch passes to reach the target. That is a capacity sufficient under this dispatch rule, not a least-cost or otherwise optimal one.",
  "The weather input is a typical meteorological year, a composite of representative months applied to the dispatch period by calendar hour. It does not contain the tail weather events that drive storage and reliability outcomes, so storage need is likely understated.",
  "The declared system includes 15,680.0 MWh of storage energy the search cannot grow, counted in the installed-storage totals but not in the battery figures. Read storageCapacity rather than storageEnergyMwh for battery."
]
```

It is a summary, not a substitute: read [Limitations](../assumptions/limitations.md) before quoting
a figure.

### SLCoE: what it is, and what it divides by

One definition, used everywhere: **the annualised cost of building and running the declared system,
divided by energy served.**

- The numerator is `cost.totalAnnualisedCostAud`: generation, storage and transmission capital
  annuitised over each asset's `technicalLifeYears` at the scenario's `costBasis.realDiscountRate`,
  plus fixed and variable operating cost and fuel.
- The denominator is **energy served**, which is demand minus unserved energy
  (`NEMSweep.Model/Simulation/SystemDispatchOutcome.cs`), published as the sweep scalar
  `energyServedMwh`. It is not demand, and it is not delivered generation: the three differ whenever
  a run has unserved energy, curtailment or storage.
- `NEMSweep.Model/Economics/PowerSystemCostBreakdown.cs` divides by that same denominator for the
  generation, storage and transmission components, so `generationSlcoeAudPerMwh +
  storageSlcoeAudPerMwh + transmissionSlcotAudPerMwh` reconstructs `slcoeAudPerMwh`.
- The unit is AUD per MWh served, in the scenario's `costBasis.year` dollars.

**SLCoE is not a bill.** Network, retail, scheme and tax costs are all outside it, so it is not
comparable to a retail tariff. Per-region SLCoE additionally divides a region's own plant cost by
the load that region served, which understates cost in a net importer; `cost.netImportedEnergyMwh`
is published so that distortion is visible rather than silent.

### Rerunning one published sweep point

Each point config is a complete resolved scenario, so it is sufficient to rerun that point: use
the matching NEMSweep version, the point's config, and access to the demand and weather artifacts
it names. No baseline scenario or sweep definition is needed to rerun an individual point.

The data root is deliberately external to the configuration and can change or disappear. Results
still record each consumed demand and weather artifact's filename, schema version, and SHA-256
digest, while demand artifacts retain descriptive archive filenames. NEMSweep does not bundle the
inputs or reconstruct their upstream retrieval history; a rerun against different input artifact
bytes is a different run and can produce different results.

## Schema versions

Every artifact carries a `schemaVersion` field, and the current value for each artifact type is
defined in one place: `NEMSweep.Contracts/ArtifactSchemaVersions.cs`. This page does not repeat those
numbers, because they would go stale the moment a schema changes. Consult the
[API reference](../api/index.md) for the current values.

Schema versions cover serialized meaning as well as JSON structure. The current system and
regional dispatch details, overviews, and sweep index distinguish region-specific storage-sizing
blockers from the whole-run aggregate outcome. Earlier regional artifacts inherited the run's
failure outcome even when growth had not been tested for that region. Do not apply the current
regional interpretation to those older artifacts.

Consumers must support the corresponding artifact versions before newly generated outputs are
published. In particular, the results site's Contracts package and schema registry must be upgraded
alongside its regenerated artifacts. Updating these schema markers does not itself publish a
NuGet package or cut a release; older published artifacts remain on their original versions until
that coordinated publication.

## Sweep scalar vocabulary

A sweep publishes one set of scalars per point per region (and one for the system as a whole), in
`sweeps/{sweepId}/index.json` and alongside each point's full result. The catalogue in
`NEMSweep.Contracts/SweepScalarCatalog.cs` is the full vocabulary: the JSON name each scalar is emitted
under, its label, and its unit.

| JSON name | Label | Unit |
|---|---|---|
| `slcoeAudPerMwh` | System levelised cost | AUD/MWh served |
| `generationSlcoeAudPerMwh` | Generation levelised cost | AUD/MWh served |
| `storageSlcoeAudPerMwh` | Storage levelised cost | AUD/MWh served |
| `demandMwh` | Demand | MWh |
| `energyServedMwh` | Energy served | MWh |
| `deliveredGenerationMwh` | Delivered generation | MWh |
| `achievedRenewableShareGridScale` | Achieved renewable share (grid scale) | fraction |
| `achievedRenewableShareNative` | Achieved renewable share (native) | fraction |
| `storagePowerMw` | Storage power capacity | MW |
| `storageEnergyMwh` | Storage energy capacity (all technologies) | MWh |
| `unservedEnergyMwh` | Unserved energy | MWh |
| `unservedEnergyPercentageOfDemand` | Unserved energy | % of demand |
| `unservedHours` | Unserved hours | h |
| `hoursServedFraction` | Hours served | fraction |
| `peakUnservedPowerMw` | Peak unserved power | MW |
| `curtailedEnergyMwh` | Curtailed energy | MWh |
| `emissionsTonnesCO2e` | Emissions | t CO2-e |
| `emissionsIntensityTonnesCO2ePerMwh` | Emissions intensity | t CO2-e/MWh served |
| `transmissionSlcotAudPerMwh` | Transmission levelised cost | AUD/MWh served |
| `transmissionCostStatus` | Transmission cost status | status |
| `netImportedEnergyMwh` | Net imported energy | MWh |
| `declaredBatteryEnergyMwh` | Battery energy capacity declared | MWh |
| `addedBatteryEnergyMwh` | Battery energy capacity added by sizing | MWh |
| `totalBatteryEnergyMwh` | Battery energy capacity | MWh |
| `totalBatteryPowerMw` | Battery power capacity | MW |
| `pumpedHydroEnergyMwh` | Pumped-hydro energy capacity | MWh |
| `otherStorageEnergyMwh` | Other storage energy capacity | MWh |

The six battery and storage-technology scalars are the same split
[`storageCapacity`](#battery-only-figures-versus-totals-that-include-pumped-hydro) publishes on a
dispatch result. `storageEnergyMwh` and `storagePowerMw` count every storage technology;
`totalBatteryEnergyMwh` and `totalBatteryPowerMw` are battery alone.

`transmissionCostStatus` is the one entry the catalogue marks as not chartable. It is a status
label (`calculated` or `notModelled`), not a numeric series.

## Energy balance: delivered generation is not energy served

`deliveredGenerationMwh` excludes both curtailment **and storage charging**.
It includes generator output serving local load and sending-end exports, but
not storage discharge or received imports. `energyServedMwh` is demand minus
unserved energy, including demand met by storage discharge and imports.
The per-technology `deliveredGenerationByTechnologyMw` series and
`deliveredGenerationByTechnologyMwh` totals use that same delivered-generation
definition. Charging allocations are bookkeeping, not a physical tracing of
co-mingled electricity.

For one region, each hourly interval and its integrated run obey:

```text
G + B + I + U = D + C + E + K
L = G - C - K
S = D - U = L + B + I - E
```

| Term | Meaning | Published field in the detail result |
|---|---|---|
| `D` | Total demand | `dataSeries.demand.totalDemandMw`; total `metrics.demandMwh` / sweep `scalars.demandMwh` |
| `U` | Unserved demand | `dataSeries.unservedDemandMw`; total `metrics.unservedEnergyMwh` / sweep `scalars.unservedEnergyMwh` |
| `S` | Energy served | `D - U`; sweep `scalars.energyServedMwh` |
| `L` | Generation after curtailment and charging | Sum of `dataSeries.deliveredGenerationByTechnologyMw`; total `metrics.deliveredGenerationMwh` / sweep `scalars.deliveredGenerationMwh` |
| `C` | Grid-side storage charge, all technologies and charge sources | `dataSeries.chargeMw` |
| `B` | Grid-side storage discharge, all technologies | `dataSeries.dischargeMw` |
| `K` | Remaining curtailment, after exports and surplus charging | `dataSeries.curtailmentMw`; total `metrics.curtailedEnergyMwh` / sweep `scalars.curtailedEnergyMwh` |
| `I` | Imports received, after link losses | `dataSeries.importsMw` |
| `E` | Exports sent, before link losses | `dataSeries.exportsMw` |
| `G` | Engine generation before curtailment and charging | No direct series; reconstruct the aggregate as `L + C + K` |
| `T` | Transmission loss | `dataSeries.transmissionLossesMw`; system link evidence in `interconnectors[].lossesMw` |

Integrate MW series by summing and multiplying by `resolution.TotalHours` to
obtain MWh. `G` includes available intermittent output subsequently curtailed,
whereas dispatchable fleets record dispatched output; it is not installed
capacity or every dispatchable fleet's unused potential output.

System imports and exports are sums of internal regional transfers, not
flows across an external system boundary. Sending-end exports minus received
imports equals transmission loss: `E - I = T`. Hence system-wide:

```text
G + B + U = D + C + K + T
S = L + B - T
S - L = B - T
```

Regional transmission loss is attributed to the receiving region. Do not
subtract it again in the regional equation: `I` is already net of loss.
Likewise, do not subtract charging again from `L`.
The system `scalars.netImportedEnergyMwh` is zero because its cost-accounting
boundary excludes internal transfers; use the import/export series and link
loss evidence for this grid balance instead.

Storage losses are applied once on charge: input energy times the fleet's
round-trip efficiency becomes stored energy; discharge removes stored energy
one-for-one. For the storage inventory, separately from the grid balance:

```text
openingStoredMwh + chargeMwh = dischargeMwh + storageLossMwh + closingStoredMwh
storageLossMwh = sum over fleets and intervals of chargeMwh * (1 - efficiency)
```

Thus `chargeMwh - dischargeMwh` is **not** necessarily storage loss: it also
includes the change in stored energy. Installed storage has an opening seed;
sizing does not increase that seed. Detail results already export charge,
discharge and transmission-loss series, but do not export a storage-loss
series or terminal inventory. `stateOfChargeByTechnologyMwh` records interval
**beginnings**, so its last value is not the end-of-run inventory. Aggregate
charge cannot in general be split between fleets with different efficiencies
from these published fields alone. The grid-balance check does not need that
split, and inferred generation is not an independent check of gross generation.

### Checking the published sweep

`PublishedEnergyBalanceTests` checks every interval, integrated regional and
system balances, metric/scalar agreement, regional aggregation and summed
link losses for p0, p9, p10 and p24. It prints every grid-balance term in MWh,
then reruns the emitted configurations against the supplied data to check
independently recorded, unrounded engine generation as well.
It pins the four documented v0.2.0 engine totals (served, delivered, discharge
and transmission loss) to within 0.0001 MWh, the original baseline-config
SHA-256, and all ten ingested demand/weather SHA-256 digests, checking the
input identities in both published evidence and the rerun. A different,
internally balanced sweep is not sufficient to pass this reproduction check.

The unrounded tolerance is
`max(0.000001 MWh, 1e-9 * max(abs(left), abs(right)))`.
Published series and energy scalars are independently rounded to 0.1 MW/MWh.
The artifact checks therefore add the worst-case rounding error: 0.05 times
the number of independently rounded values in the equation, times interval
hours for MW samples. For `L + B + I = D - U + E`, that is
`0.05 * (technologyCount + 5) * intervalHours` per interval, summed over the
horizon for the integrated check. For this five-technology, 8,760-hour sweep
the bound is 0.5 MWh per interval and 4,380 MWh per year. It is a serialization
bound, not permission for an engine imbalance. Metric and aggregation checks
similarly account for only the rounded values in each comparison.

Small synthetic cases run normally; the external-data acceptance is
explicitly skipped unless both directories are supplied:

```powershell
$env:NEMSWEEP_ENERGY_BALANCE_SWEEP = (Resolve-Path .\study\out\sweeps\datacentre-nameplate-fy2026).Path
$env:NEMSWEEP_ENERGY_BALANCE_DATA = (Resolve-Path .\study\data).Path
dotnet test .\NEMSweep.CLI.Tests\NEMSweep.CLI.Tests.csproj -c Release --filter FullyQualifiedName~PublishedEnergyBalanceTests --logger "console;verbosity=detailed"
```

To reproduce the 0.2.0 check, build the CLI at `7f44412`, prepare the
[Advanced starter](synthetic-starters.md), and run the original committed
scenario/sweep configurations with the starter's ingested data. You can select
only the four requested points without changing their overrides or baseline:

```powershell
# Run from the 0.2.0 source root after preparing .\study with --quick-start advanced.
$definition = Get-Content .\sweeps\datacentre-nameplate-fy2026.json -Raw | ConvertFrom-Json
$definition.points = @($definition.points | Where-Object { $_.pointId -in @('p0', 'p9', 'p10', 'p24') })
[System.IO.File]::WriteAllText(
    (Join-Path $PWD 'study\sweeps\energy-balance-four-points.json'),
    ($definition | ConvertTo-Json -Depth 100))
dotnet .\NEMSweep.CLI\bin\Release\net10.0\nemsweep.dll --run-sweep .\study\sweeps\energy-balance-four-points.json --data-root .\study\data --output .\study\out
```

Run the acceptance test from a checkout containing the test, pointing its
environment variable at the generated 0.2.0 sweep. No new artifact schema or
change to existing totals is needed to check this balance.

### Verified 0.2.0 result

The four-point run at `7f44412`, using the original committed configs and
the verified Advanced data release `v2026.10.1`, reproduces the reported
delivered-generation and served-energy scalars. These are unrounded engine
totals shown to six decimal places in TWh:

| Point | Delivered generation `L` | Energy served `S` | Discharge `B` | Transmission loss `T` | `S - L = B - T` |
|---|---:|---:|---:|---:|---:|
| p0 | 186.684136 | 186.643742 | 0.024973 | 0.065368 | -0.040394 |
| p9 | 225.408514 | 226.060638 | 1.073309 | 0.421185 | 0.652124 |
| p10 | 229.475558 | 230.388178 | 1.386342 | 0.473722 | 0.912620 |
| p24 | 271.480899 | 278.114778 | 7.522816 | 0.888937 | 6.633880 |

Every tested regional/hourly balance closes. The largest unrounded integrated
system residual is less than 0.000002 MWh. Integrating the independently
rounded published series gives system residuals of 30.7, 17.5, 23.6 and
53.1 MWh respectively, within the stated serialization bound.

This is a definition difference, not an energy-creation or scalar-summing bug.
No existing field or dispatch result changes. In particular, the worked
example's per-technology delivered-generation breakdown is unchanged; the
p24-minus-p0 delivered-generation increase remains approximately 84.797 TWh.
It should be described as **delivered generation excluding storage charging
and discharge**, rather than gross generation or extra energy served.

## Base-demand externalisation

Sweep points frequently share an identical base-demand series, because varying a cost parameter or
a storage limit does not change demand at all. Rather than repeating that series inside every
point's result, a sweep run externalises it: the first point to produce a given series writes it
once to `series/base-demand-{sha256}.json`, where the hash is computed from the serialised series
itself, and every point whose demand series is byte-identical simply references that file's path.
A run also prunes series files nothing in the freshly-written index still references, so the
series directory does not accumulate stale content-addressed files across regenerations.

## Provenance and reproducibility

Every dispatch result records the exact input artifacts it consumed, whether that result is a
scenario run, a region within one, or a sweep point. For each of the demand and weather inputs it
records the filename, the schema version, and the SHA-256 digest of the exact bytes that were
parsed (`DispatchInputArtifactDTO` in `NEMSweep.Contracts/DispatchResultsDTO.cs`). The digest, not the
configured file path, is the reproducibility boundary: a path can be overwritten with different
content later, but the digest identifies the bytes that actually produced this result.

A whole-system result also records the model build behind it, in a `provenance` block carrying the
git commit SHA and a flag for whether the working tree had uncommitted changes. A sweep `index.json`
records the same for the sweep as a whole. Digests pin the bytes a run consumed, but publishing a
result is a manual step, so without the commit a file copied somewhere else could not say which
version of the model read them.

### Citing a run

Cite the run, not just the tool. "Modelled with NEMSweep" cannot be checked by anyone; a version, a
commit and a scenario can be. The paste-ready form:

```text
Modelled with NEMSweep <version> at commit <commit>, scenario <scenario config>, run <runId>.
https://github.com/HasinthaAttanayake/NEMSweep
```

`nemsweep --version` reports the version, and `provenance.gitCommitSha` and `runId` on the result
supply the commit and the run. The scenario config is the one field you have to carry yourself: a
dispatch result records no scenario identifier, so note which file you ran at the time you run it.
A sweep is better placed, because its index provenance lists the baseline scenario config by path
and SHA-256 under the `baseline-scenario-config` purpose.
[CITATION.cff](https://github.com/HasinthaAttanayake/NEMSweep/blob/main/CITATION.cff) carries the
citation metadata for the software itself.

### Which commit is recorded

The commit is the one the binary was built at, stamped in by its build, not whatever commit the
directory you ran from happens to be standing on. That distinction matters once the CLI is installed
or run as a container, where the two are unrelated. The dirty flag is only ever true when the run
was made from a checkout standing on that same commit, because that is the only case where the
source the binary was built from is in front of the tool to inspect. The block is absent when the
binary was built outside a checkout and so carries no commit to report.

Provenance paths in a sweep index are recorded relative to the data root, or to the working
directory for the sweep definition and baseline config, which is what keeps them citing
`sweeps/…` and `scenarios/…` rather than a location on one machine. The digest remains the
reproducibility boundary; the path is there to say where the run was configured from.

## Writing conventions

Published artifacts follow the conventions in `NEMSweep.CLI/Infrastructure/JsonFile.cs`:

- Property names are camelCase.
- Object keys are sorted ordinally, so a rerun that changes one value produces a small, reviewable
  diff rather than a reordered file.
- Numeric values are rounded according to their unit, so `*Mw` and `*Mwh` fields carry one decimal
  place, AUD fields two, and fractional shares four, rather than full floating-point precision that
  no one reads.
- Published artifacts are written unindented. Indentation was roughly seventy percent of the bytes
  in these files, and nothing ever read the whitespace: not the CLI, not the site, not a person.

### What changes between two identical reruns

Rerun a scenario against unchanged inputs with the same CLI build and every modelled value is
reproduced exactly. The artifacts are not byte-identical, though, and what differs depends on the
artifact:

| Artifact | Differs between identical reruns |
|---|---|
| `results*.json` | `runId` only, a fresh GUID per run |
| `demand-{region}.json`, `weather-{region}.json`, `generation-information.json` | `generatedAt`, a UTC timestamp stamped at import |
| `sweeps/{sweepId}/index.json` | Each point's measured `durationMs`, and the sweep's `totalDurationMs` when recorded |

The `runId`-only guarantee assumes unchanged input-path layout and build metadata as well as
unchanged input bytes. It was verified by regenerating all twelve committed dispatch artifacts and
diffing them leaf by leaf. It applies to the dispatch artifacts; it does not extend to the imported
inputs or to the sweep index, which record when and how long a run took.

### Atomic publication

Publishing a **scenario** result is atomic: the new files are staged in a temporary directory, the
previous versions of the target files are moved aside, and only then are the staged files moved
into place. If anything fails partway through, the move is rolled back and the previous artifacts
are restored, so a failed run never leaves a half-written result where a complete one used to be
(`DispatchResultsExport.WritePublication`).

A **sweep** has no equivalent guarantee at the sweep level. Each point's dispatch result lands
through that same staged path, but it is then rewritten in place to externalise its base-demand
series, and the generated configs, per-point status files, the sweep index and the manifest are
written incrementally as the run proceeds. An interrupted sweep can therefore leave a partially
updated `sweeps/{sweepId}/` directory. Rerunning the sweep restores it.

## These files are generated

Every file described here is generated. Do not hand-edit one. Regenerate it by rerunning the
command that produced it (`--ingest` for the input artifacts, `--run-scenario` or `--run-sweep` for
results), so what is on disk stays traceable to the inputs and commit behind it.

The published artifact set is what the results site displays. Publishing to it is a deliberate act:
use the site's publication workflow when you intend to update it, and let ordinary runs land in your
own directory the rest of the time.

**That set is an illustrative example, not a dataset.** It is one FY2026 run and one sweep, retained
so the site has something to show. It is not carried in this repository, so a clone sources its own
upstream data and runs `--ingest` before it can reproduce a run. The
provenance block on it is real, which is exactly why it is worth saying plainly: a demo carrying
input digests and a commit can read as more authoritative than it is. Run your own scenario before
quoting a figure, and see [DATA-LICENSE.md](https://github.com/HasinthaAttanayake/NEMSweep/blob/main/DATA-LICENSE.md)
for the terms on the data behind it.

## CSV

Everything on this page is JSON, which is the contract but not something you can open. Add `--csv`
to a run and it also writes a star schema of CSV tables projected from these same values, rounded
by the same rule. See [CSV tables](csv-tables.md).
