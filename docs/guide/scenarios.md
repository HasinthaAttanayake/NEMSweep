# Scenario configuration

A scenario config is the `NEMSweep.CLI` input format. It is a single JSON file describing every
region a run will dispatch: its demand and weather inputs, its generating and storage fleets, the
interconnectors linking it to other regions, the cost basis the run is priced against, and the
bounds the storage-sizing search may use to grow a region's Battery capacity. `--run-scenario` reads
one of these, dispatches it over the period its demand series cover, and writes the results.

The CLI accepts free-form region IDs in scenarios when they refer to ingested artifacts, including
IDs created by a format-2 portable input bundle. Legacy NEM-only import paths remain limited to the
five National Electricity Market regions. The framework itself also treats region identifiers as
free-form strings.

The authoritative machine-readable form of everything on this page is the published JSON Schema:

```bash
dotnet run --project NEMSweep.CLI -- --describe-schema scenario
```

Run that to see the exact `schemaVersion` the installed CLI accepts, and to validate a config
before you run it. This page does not repeat that number, because it changes independently of the
documentation.

The CLI upgrades supported legacy scenario versions 5 and 6 to version 7 `sizeBattery` configs.
Legacy reliability fields are moved out of `storageSizing`, with the historical default applied
when an older config has no target. The pre-emissions shape also gains a zero operational-emissions
intensity on generation profiles to preserve its prior behavior. The normalized config must still
meet current validation, including a `Battery` assumptions fleet in every region. Other older
schemas are rejected rather than guessed at.

## How the file is read

Deserialisation is strict: `additionalProperties` is `false` throughout the schema, and the CLI
enforces the same thing at the JSON level (`UnmappedMemberHandling.Disallow`). Any unknown property
anywhere in the file is a hard error rather than a value that gets silently ignored, whether it is
a typo in a field name or a field copied from the wrong nesting level. That is deliberate: a
scenario that loaded despite a typo would run with different assumptions than the author intended,
and the model would never say so.

`demandFile` and `weatherFile` are **per-region**, not top-level fields. A file that sets them (or
`dataCentreNameplateMw`) at the root is rejected outright, specifically to stop a single-region
habit from carrying over into a multi-region config.

## Root object

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `schemaVersion` | integer | yes | n/a | Must equal the version reported by `--describe-schema scenario`. |
| `id` | string | yes | n/a | Scenario identifier. Reported by `--validate-scenario` and carried on sweep-generated point configs, but **not** written into a dispatch result: neither `results.json` nor `results-{region}.json` records it. It does not appear in any published path either, because an ordinary run writes fixed `results*.json` names and sweep paths are keyed by sweep and point ID. Keep your own note of which config produced a result. |
| `name` | string | yes | n/a | Human-readable name. |
| `costBasis` | object | yes | n/a | See [`costBasis`](#costbasis). |
| `executionMode` | string | no, defaults to `fixedFleet` | n/a | `fixedFleet` dispatches declared fleets unchanged; `sizeBattery` runs the bounded Battery sizing search. |
| `reliability` | object | yes | n/a | Required target and optional standard label, used by both execution modes. |
| `regions` | array of [region](#regions) | yes, at least one | n/a | One entry per region, with IDs matching the ingested artifacts. |
| `storageSizing` | object or null | mode-dependent | n/a | Required as an object for `sizeBattery`; omitted or `null` for `fixedFleet`. |
| `interconnectors` | array of [interconnector](#interconnectors) | no | n/a | Directed transmission links between regions. |
| `provenance` | object | no | n/a | Free-form; the CLI overwrites this on sweep-generated configs, so treat it as informational rather than as configuration. |

## `reliability`

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `targetUsePercentage` | number | yes | percentage of demand energy, `(0, 100]` | Maximum unserved energy as a percentage of demand energy. Both the system and each individual region must meet the target. |
| `standardName` | string or null | no | n/a | Optional descriptive label recorded with the reliability result; it does not affect dispatch. |

This target is required in both execution modes. `fixedFleet` reports the declared fleets' result even
when the target is missed; `sizeBattery` uses it to guide the bounded Battery search.

## `costBasis`

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `year` | integer | yes | calendar year, 2000–2100 | The cost-basis year for all AUD figures in the scenario. |
| `realDiscountRate` | decimal | yes | fraction (e.g. `0.07` = 7%) | Real discount rate used to annuitise capital costs. |

## `regions[]`

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `regionId` | string | yes | n/a | Exact ID matching the demand and weather artifacts. IDs must be distinct and have no leading or trailing whitespace. Custom IDs may be at most 100 UTF-8 bytes after invariant uppercase normalization; known NEM IDs keep their existing filenames. |
| `demandFile` | string | yes | n/a | Path to this region's demand series (as written by `--ingest`). |
| `weatherFile` | string | yes | n/a | Path to this region's weather series (as written by `--ingest`). |
| `generatingFleets` | array of [generating fleet](#regionsgeneratingfleets) | yes, at least one | n/a | This region's generation. Technologies must be distinct within the region. |
| `storageFleets` | array of [storage fleet](#regionsstoragefleets) | yes, may be empty | n/a | This region's declared storage. Technologies must be distinct. `sizeBattery` additionally requires a `Battery` fleet in every region to provide its cost and technology assumptions; zero initial capacity is valid. |
| `dataCentreNameplateMw` | number | no, default 0 | MW | A flat, full-load-factor additive demand component, representing new load such as a data centre. |

Scenario validation also calls the portable filename encoder before dispatch. Custom IDs exceeding the 100-byte normalized limit or containing invalid UTF-16 are rejected. Free-form IDs can contain spaces, punctuation, commas, quotes and Unicode; if an ID appears in a demand CSV field, quote it using standard CSV escaping.

An interconnector endpoint must name a region that appears in `regions` (see
[below](#interconnectors)).

## `regions[].generatingFleets[]`

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `technology` | string | yes | n/a | Generation technology name (e.g. `Coal`, `Gas`, `Solar`, `Wind`, `Hydro`). Must be distinct within the region. |
| `nameplateCapacityMw` | number | yes | MW | Installed nameplate capacity. |
| `costParameters` | object | yes | n/a | See [cost parameters](#generation-cost-parameters). |
| `technologyProfile` | object | yes | n/a | See [generation technology profile](#generation-technology-profile). |
| `monthlyCapacityFactors` | array of [monthly capacity factor](#monthlycapacityfactors) | no | n/a | An energy budget per calendar month, used by Hydro (see below). |

### Generation cost parameters

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `capitalCostAudPerMw` | decimal | yes | AUD per MW | Overnight capital cost per MW of nameplate capacity. |
| `fixedOperatingCostAudPerMwYear` | decimal | yes | AUD per MW per year | Fixed O&M cost per MW of nameplate capacity per year. |
| `variableOperatingCostAudPerMwh` | decimal | yes | AUD per MWh generated | Variable O&M cost per MWh generated. |
| `fuelPriceAudPerGj` | decimal | yes | AUD per GJ | Fuel price. Zero for fuel-free technologies (Solar, Wind, Hydro). |

### Generation technology profile

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `heatRateGjPerMwh` | number | yes | GJ per MWh generated | Fuel consumed per MWh generated. Zero for fuel-free technologies. Combined with `fuelPriceAudPerGj` and `variableOperatingCostAudPerMwh` to derive short-run marginal cost, which sets merit order. |
| `technicalLifeYears` | integer | yes | years | Technical life used to annuitise capital cost. |
| `emissionsIntensityTonnesPerMwh` | number | yes | t CO2-e per MWh generated | Operational emissions per MWh generated, on the same gross basis as fuel. Combustion only, so not a life-cycle figure. Zero for non-emitting technologies, stated rather than defaulted. This field is an input assumption; the emissions intensity a run publishes is a different quantity, t CO2-e per MWh **served**. |

**Leave `emissionsIntensityTonnesPerMwh` at zero for Solar and Wind.** Emissions are charged on
available generation, before curtailment, which is exact for the fleets that emit because dispatch
constrains off only Solar and Wind. A non-zero intensity on either of those two therefore charges
the scenario for output that was constrained off and never released anything. Nothing in validation
stops you: the field is yours to set, and this is the one value where a plausible-looking number is
wrong. [Emissions](../concepts/economics.md#emissions) covers the basis.

### `monthlyCapacityFactors[]`

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `month` | date | yes | calendar month (first-of-month date) | The month this budget applies to. |
| `capacityFactor` | number | yes | fraction, `(0, 1]` | Energy budget for the month, expressed as a capacity factor against `nameplateCapacityMw`. |

`monthlyCapacityFactors` is an **energy budget**, not a shape constraint: it caps how much energy
Hydro may generate across the month in total, rather than dictating an hourly output profile. It is
the mechanism that lets a scenario represent Hydro as a rationed but dispatchable resource rather
than as unlimited firm capacity.

## `regions[].storageFleets[]`

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `technology` | string | yes | n/a | Storage technology name (e.g. `Battery`, `PumpedHydro`). Must be distinct within the region. |
| `initialEnergyCapacityMwh` | number | yes | MWh | Installed energy capacity at the start of the run. |
| `initialPowerCapacityMw` | number | yes | MW | Installed power capacity at the start of the run. |
| `costParameters` | object | yes | n/a | See [storage cost parameters](#storage-cost-parameters). |
| `technologyProfile` | object | yes | n/a | See [storage technology profile](#storage-technology-profile). |

`initialEnergyCapacityMwh` and `initialPowerCapacityMw` must **either both be zero or both be
positive**, because a half-built fleet is rejected. A zero/zero fleet is not the same as omitting
the technology: it means no capacity of that technology is installed at the start of the run, but the
cost and technology-profile assumptions attached to it still apply to any capacity the
storage-sizing search later adds. In practice this is how you let the sizing loop introduce a
Battery: declare a `Battery` fleet at 0 MWh / 0 MW with the cost and efficiency assumptions you want
it to be built at, and the search grows it from there if the region fails its reliability target.
Only `Battery` fleets are grown this way; other storage technologies (for example `PumpedHydro`)
are dispatched as declared and are not resized by the search.

### Storage cost parameters

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `powerCapitalCostAudPerMw` | decimal | yes | AUD per MW | Overnight capital cost per MW of power capacity. |
| `energyCapitalCostAudPerMwh` | decimal | yes | AUD per MWh | Overnight capital cost per MWh of energy capacity. |
| `fixedOperatingCostAudPerMwYear` | decimal | yes | AUD per MW per year | Fixed O&M cost per MW of power capacity per year. |

### Storage technology profile

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `technicalLifeYears` | integer | yes | years | Technical life used to annuitise capital cost. |
| `roundTripEfficiency` | number | yes | fraction, `[0, 1]` | Round-trip efficiency of one charge/discharge cycle. |

## `storageSizing`

`storageSizing` is present only when `executionMode` is `sizeBattery`. It contains only search
bounds; reliability is configured separately in the required root `reliability` object.

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `maximumPowerMw` | number | yes | MW | Largest Battery power the sizing search may consider, applied **per region**, not to the system total. |
| `maximumEnergyMwh` | number | yes | MWh | Largest Battery energy the sizing search may consider, applied **per region**. Must support at least four hours at `maximumPowerMw`. |
| `maximumPasses` | integer | no, default `256` | count | Cap on whole-system dispatch passes the sizing search may spend. Each pass re-dispatches every region for the full period, so this bounds wall-clock cost. Reaching it is a reportable outcome, not a crash. |


## `interconnectors[]`

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `fromRegionId` | string | yes | n/a | Sending-end region. Must be one of `regions`. |
| `toRegionId` | string | yes | n/a | Receiving-end region. Must be one of `regions` and different from `fromRegionId`. |
| `capacityMw` | number | yes | MW | Directed transfer capacity, metered at the sending end. |
| `routeLengthKm` | number | yes, positive | km | The line's route length. Declared directly; not derived from anything else. |
| `capitalCostAudPerKmPerMw` | decimal | yes | AUD per km per MW | Capital cost rate, multiplied by route length and capacity. |
| `fixedOperatingCostAudPerKmPerMwYear` | decimal | yes | AUD per km per MW per year | Fixed O&M cost rate, multiplied by route length and capacity. |
| `technicalLifeYears` | integer | yes, nonzero | years | Technical life used to annuitise capital cost. |

Interconnectors are **directed**. A corridor that carries flow both ways is two entries, one
`fromRegionId` and `toRegionId` pair per direction, and each entry is costed independently at the
route's full length. Declaring both directions therefore roughly doubles the reported transmission
capital and fixed cost for that corridor rather than splitting it between the two flows. See
[Limitations](../assumptions/limitations.md) for what that does to reported system cost. At most one
interconnector is permitted per exact direction, and `fromRegionId`/`toRegionId` must not be equal.

Costing an interconnector does not require its endpoint regions to carry weather data: route length
is `routeLengthKm`, not anything derived from a region's weather file.

## Time

A scenario period runs in market time: one fixed UTC offset with no daylight saving
adjustment, carried on both period bounds and shared by every series in the run. The
offset is whatever the demand artifact declares, which for AEMO operational demand is
the NEM's UTC+10. Point the model at a single-timezone market by ingesting that market's
demand and weather under its own offset instead.

## Which inputs change dispatch, and which change only cost

Worth knowing before you read a result: some of the fields above feed back into the hour-by-hour
dispatch, and the rest are priced on top of a dispatch that would have been identical without them.
Change one of the second kind and the physical result — unserved energy, curtailment, the battery
the search lands on — is unchanged; only the money moves.

The split follows from short-run marginal cost. `GenerationCostParameters.ShortRunMarginalCostFor`
(`NEMSweep.Model/Scenarios/GenerationCostParameters.cs`) is
`variableOperatingCostAudPerMwh + fuelPriceAudPerGj * heatRateGjPerMwh`, and
`NEMSweep.Model/Simulation/GenerationMeritOrder.cs` orders fleets by it. Nothing else in a cost
block reaches the dispatch.

**Changes dispatch, and therefore cost as well:**

| Field | Why |
|---|---|
| `generatingFleets[].costParameters.fuelPriceAudPerGj` | A term of short-run marginal cost, so it can reorder merit order. |
| `generatingFleets[].costParameters.variableOperatingCostAudPerMwh` | The other term of short-run marginal cost. |
| `generatingFleets[].technologyProfile.heatRateGjPerMwh` | Multiplies fuel price into short-run marginal cost. |
| `generatingFleets[].nameplateCapacityMw` | How much a fleet can deliver in an hour. |
| `generatingFleets[].monthlyCapacityFactors[]` | Hydro's monthly energy budget. |
| `storageFleets[].initialEnergyCapacityMwh`, `initialPowerCapacityMw` | What storage exists before the search. |
| `storageFleets[].technologyProfile.roundTripEfficiency` | Energy lost on charge. |
| `regions[].dataCentreNameplateMw` | Adds flat demand. |
| `regions[].demandFile`, `weatherFile` | The series being dispatched against. |
| `interconnectors[].capacityMw` | Caps directed transfer. |
| `executionMode`, `reliability.targetUsePercentage`, `storageSizing.*` | Whether the battery search runs, what it aims at, and the bounds it may use. |

**Changes only the reported cost:**

| Field | Why |
|---|---|
| `generatingFleets[].costParameters.capitalCostAudPerMw` | Annuitised capital; never consulted by dispatch. |
| `generatingFleets[].costParameters.fixedOperatingCostAudPerMwYear` | A per-MW-year charge on declared capacity. |
| `generatingFleets[].technologyProfile.technicalLifeYears` | Annuitisation period only. |
| `storageFleets[].costParameters.*` (`powerCapitalCostAudPerMw`, `energyCapitalCostAudPerMwh`, `fixedOperatingCostAudPerMwYear`) | Priced after the search; the search targets reliability, not cost. |
| `storageFleets[].technologyProfile.technicalLifeYears` | Annuitisation period only. |
| `interconnectors[].routeLengthKm`, `capitalCostAudPerKmPerMw`, `fixedOperatingCostAudPerKmPerMwYear`, `technicalLifeYears` | Transmission cost rates; the only interconnector field dispatch reads is `capacityMw`. |
| `costBasis.year`, `costBasis.realDiscountRate` | The cost basis every AUD figure is expressed and annuitised in. |
| `reliability.standardName` | A label recorded with the result. |

`generatingFleets[].technologyProfile.emissionsIntensityTonnesPerMwh` is a third case: it changes
neither dispatch nor cost, only the emissions accounting.

One consequence is worth stating plainly. Sweeping a **capital** cost does not change the system
that gets built, because the sizing search is driven by the reliability target rather than by cost.
A battery capital-cost sweep therefore reprices one fixed system across the axis; it does not tell
you what system a cheaper battery would have led someone to build. Sweeping a **fuel** price does
move the dispatch, and so can move the battery the search lands on.

## Recipe: change one input

Two routes. Use the first when you want one answer; use the second when you want the before and
after published together on an axis.

**Edit a copy of the scenario.** A scenario config is one JSON file, so changing one input is
changing one value. To take the gas fuel price up by 50%, from 10 to 15 AUD/GJ:

```bash
cp scenarios/sized-scenario.json scenarios/sized-gas15.json
```

Then in the copy, set the `Gas` fleet's `fuelPriceAudPerGj` to `15` and give the file its own `id`.
The field is per fleet, per region, so a multi-region scenario has one to change in each region
that declares `Gas`:

```json
{
  "regionId": "Island-X",
  "generatingFleets": [
    {
      "technology": "Gas",
      "costParameters": { "fuelPriceAudPerGj": 15 }
    }
  ]
}
```

Validate before running, which catches a typo in seconds rather than after a dispatch:

```bash
nemsweep --validate-scenario scenarios/sized-gas15.json --data-root ./data --format json
```

```bash
nemsweep --run-scenario scenarios/sized-gas15.json --data-root ./data --output ./out/gas15
```

Compare `cost.slcoeAudPerMwh` and `reliability.achievedUsePercentageOfDemand` in each run's
`results-overview.json`. Because fuel price is a dispatch input, expect the physical result to move
too, not only the cost.

**Or write a two-point sweep**, which keeps the baseline and the variant in one artifact and names
what was varied. This is the same change as a [sweep](sweeps.md) override patch:

```json
{
  "schemaVersion": 2,
  "sweepId": "gas-price-x15",
  "name": "Gas fuel price +50%",
  "axis": { "label": "Gas fuel price", "unit": "AUD/GJ" },
  "baselineConfigPath": "scenarios/sized-scenario.json",
  "points": [
    {
      "pointId": "base",
      "axisValue": 10,
      "label": "10 AUD/GJ (baseline)",
      "overrides": {}
    },
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

The keyed-array merge means naming the `Gas` fleet's one changed field is enough: every other cost
parameter on it, and every other fleet and region, carries over from the baseline. See
[JSON merge-patch semantics](sweeps.md#json-merge-patch-semantics).

## Recipe: sweep a cost parameter

Battery capital cost from 50% to 150% of the baseline, in five points. The baseline here declares
`powerCapitalCostAudPerMw: 200000` and `energyCapitalCostAudPerMwh: 150000`, so each point states
the multiple of those two:

```json
{
  "schemaVersion": 2,
  "sweepId": "battery-capex",
  "name": "Battery capital cost 50-150%",
  "axis": { "label": "Battery capital cost", "unit": "% of baseline" },
  "baselineConfigPath": "scenarios/sized-scenario.json",
  "points": [
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
  ]
}
```

Repeat that point for `p75`, `p100`, `p125` and `p150` at 75%, 100%, 125% and 150% of each figure,
with `axisValue` matching. `p100` can use `"overrides": {}` instead, since it is the baseline.
Nothing cross-checks `axisValue` against `overrides`, so keeping the two in agreement is yours to
get right.

```bash
nemsweep --fan-out-sweep sweeps/battery-capex.json --data-root ./data --output ./out/capex
```

```bash
nemsweep --run-sweep sweeps/battery-capex.json --data-root ./data --output ./out/capex --csv
```

Fan out first and read `sweeps/battery-capex/configs/p50.json` to confirm each point patched what
you meant, then run. Read the result from `sweeps/battery-capex/index.json`, whose `points[]` carries
each point's `axisValue` and `scalars`, or from `csv/points/<pointId>/fact_scalars.csv` with `--csv`.

Two things to expect, both consequences of the section above. The storage the search installs is the
same at every point, because capital cost does not steer the search. And `slcoeAudPerMwh` moves
close to linearly with the axis, because the only thing changing is the annuitised capital on a
fixed quantity of storage. A capital-cost sweep is a repricing of one system, which is a narrower
finding than it can look like on a chart.

To vary a parameter that *does* change dispatch, use the same shape with `fuelPriceAudPerGj` on a
generating fleet.

## See also

- [Scenario parameters](../assumptions/scenario-parameters.md): what these values mean in the
  model and how to think about choosing them. They are assumptions you supply, not constants the
  model derives.
- [Sweeps](sweeps.md): applying override patches to this file across a series of runs on one
  labelled axis, instead of hand-editing copies of it.
