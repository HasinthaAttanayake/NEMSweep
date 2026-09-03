# Scenario configuration

A scenario config is the `NEMSweep.CLI` input format. It is a single JSON file describing every
region a run will dispatch: its demand and weather inputs, its generating and storage fleets, the
interconnectors linking it to other regions, the cost basis the run is priced against, and the
bounds the storage-sizing search may use to grow a region's Battery capacity. `--run-scenario` reads
one of these, dispatches it over the period its demand series cover, and writes the results.

The five-region rule below is the CLI's, enforced because the demand and weather artifacts it
ingests are National Electricity Market data. The framework itself takes region identifiers as
free-form strings.

The authoritative machine-readable form of everything on this page is the published JSON Schema:

```bash
dotnet run --project NEMSweep.CLI -- --describe-schema scenario
```

Run that to see the exact `schemaVersion` the installed CLI accepts, and to validate a config
before you run it. This page does not repeat that number, because it changes independently of the
documentation.

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
| `id` | string | yes | n/a | Scenario identifier. Recorded in each result's provenance. It does not appear in any published path: an ordinary run writes fixed `results*.json` names, and sweep paths are keyed by sweep and point ID. |
| `name` | string | yes | n/a | Human-readable name. |
| `costBasis` | object | yes | n/a | See [`costBasis`](#costbasis). |
| `regions` | array of [region](#regions) | yes, at least one | n/a | One entry per NEM region the scenario dispatches. |
| `storageSizing` | object | yes | n/a | See [`storageSizing`](#storagesizing). |
| `interconnectors` | array of [interconnector](#interconnectors) | no | n/a | Directed transmission links between regions. |
| `provenance` | object | no | n/a | Free-form; the CLI overwrites this on sweep-generated configs, so treat it as informational rather than as configuration. |

## `costBasis`

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `year` | integer | yes | calendar year, 2000–2100 | The cost-basis year for all AUD figures in the scenario. |
| `realDiscountRate` | decimal | yes | fraction (e.g. `0.07` = 7%) | Real discount rate used to annuitise capital costs. |

## `regions[]`

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `regionId` | string | yes | n/a | One of the five NEM regions: `NSW1`, `QLD1`, `SA1`, `TAS1`, `VIC1`. Must be distinct across `regions`. |
| `demandFile` | string | yes | n/a | Path to this region's demand series (as written by `--ingest`). |
| `weatherFile` | string | yes | n/a | Path to this region's weather series (as written by `--ingest`). |
| `generatingFleets` | array of [generating fleet](#regionsgeneratingfleets) | yes, at least one | n/a | This region's generation. Technologies must be distinct within the region. |
| `storageFleets` | array of [storage fleet](#regionsstoragefleets) | yes, at least one | n/a | This region's storage. Technologies must be distinct within the region. |
| `dataCentreNameplateMw` | number | no, default 0 | MW | A flat, full-load-factor additive demand component, representing new load such as a data centre. |

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
| `emissionsIntensityTonnesPerMwh` | number | yes | t CO2-e per MWh generated | Operational emissions per MWh generated, on the same gross basis as fuel. Combustion only, not life-cycle. Zero for non-emitting technologies, stated rather than defaulted. |

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

| Field | Type | Required | Unit | Meaning |
|---|---|---|---|---|
| `maximumPowerMw` | number | yes | MW | Largest Battery power the sizing search may consider, applied **per region**, not to the system total. |
| `maximumEnergyMwh` | number | yes | MWh | Largest Battery energy the sizing search may consider, applied **per region**. Must support at least four hours at `maximumPowerMw`. |
| `targetUsePercentage` | number | no, default `0.002` | percentage of demand energy, `(0, 100]` | The reliability target: maximum unserved energy as a percentage of demand energy. Both the whole system and every individual region must be within it. `0.002` is the National Electricity Rules reliability standard. |
| `maximumPasses` | integer | no, default `256` | count | Cap on whole-system dispatch passes the sizing search may spend. Each pass re-dispatches every region for the full period, so this bounds wall-clock cost. Reaching it is a reportable outcome, not a crash. |
| `reliabilityStandardName` | string or null | no | n/a | Free-form label recorded against the run's reliability result. Does not affect dispatch. |

The two maxima are commercial limits you supply, not physical ones. If the search reaches either
one without meeting the target, the run still completes and reports which region hit the limit and
by how much it remained short. "No Battery within these bounds meets the standard" is itself a
published result, not a failure of the tool.

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

## See also

- [Scenario parameters](../assumptions/scenario-parameters.md): what these values mean in the
  model and how to think about choosing them. They are assumptions you supply, not constants the
  model derives.
- [Sweeps](sweeps.md): applying override patches to this file across a series of runs on one
  labelled axis, instead of hand-editing copies of it.
