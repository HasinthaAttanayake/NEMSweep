# Sweeps

A sweep is a baseline scenario config plus a set of points, run together and published together.
Each point is a free-form override patch applied to that baseline, and the points are lined up along
one labelled axis so the shape of the system's response can be read off a chart rather than
reconstructed run by run from a pile of scenario files. Each run is called a **point**.

A point may change any number of inputs at once. The axis is the chart's x-dimension, not a
constraint on what a point varies: a point can move a data-centre load, a discount rate and a
storage limit together, and still sit at a single position on one axis.

The full published JSON Schema for the sweep definition format is available from the CLI:

```bash
dotnet run --project NEMSweep.CLI -- --describe-schema sweep
```

## The definition format

A sweep is one JSON file.

| Field | Type | Required | Meaning |
|---|---|---|---|
| `schemaVersion` | integer | yes | Must equal the version the CLI accepts (see `--describe-schema sweep`). |
| `sweepId` | string | yes | Must match `^[a-z0-9][a-z0-9-]*$`. Becomes the name of the `sweeps/<sweepId>/` output directory, hence the restricted character set. |
| `name` | string | yes | Human-readable name. |
| `axis` | object | yes | `label` and `unit`, both non-blank strings, giving how the swept quantity is captioned on a chart. |
| `baselineConfigPath` | string | yes | Path to the scenario config every point patches. |
| `points` | array of [point](#points) | yes, at least one | The runs that make up the sweep. |

Sweep version 2 accepts `executionMode` and `reliability` patches alongside the existing scenario overrides. A legacy scenario baseline is upgraded to the current scenario shape before a version-2 patch is applied; when normalization changes an existing `$schema` hint, it is updated to the current scenario schema. Version 1 keeps its historical patch shape and rejects these newer override fields.

### `points[]`

| Field | Type | Required | Meaning |
|---|---|---|---|
| `pointId` | string | yes | Must match `^[a-z0-9][a-z0-9-]*$` and be unique within the sweep. Becomes part of output filenames. |
| `axisValue` | number | yes | Where this point sits on the chart's x-axis. Display only; see below. |
| `label` | string | yes | Human-readable label for this point. |
| `overrides` | object | yes | A JSON merge patch applied to the baseline config. May be empty (`{}`) for a true baseline point. |

## `axisValue` does not drive anything

This is the single most important thing to understand about the format. `axisValue` is a plotting
coordinate and nothing else: it places the point on the x-axis of a chart, and has no effect on
what the point actually runs. The only thing that changes what a point runs is `overrides`.

A sweep is free to declare `axisValue: 500` on a point whose `overrides` changes nothing, or changes
something entirely unrelated to "500" in any unit. Nothing in `--fan-out-sweep` or `--run-sweep`
cross-checks the two. Such a sweep will fan out, run, and publish a perfectly normal-looking chart
whose x-axis has no relationship to what was actually varied. When you write a sweep, treat keeping
`axisValue` and `overrides` in agreement as entirely your responsibility. No validation will catch
a mismatch for you.

## JSON merge-patch semantics

Each point's `overrides` is applied to the baseline scenario config as an RFC 7386 JSON merge patch,
with two deliberate extensions, both about arrays: five named arrays merge by key rather than being
replaced, and a reserved `$remove` shape deletes a keyed element. This is currently documented
nowhere else, so it is worth being precise about.

**Scalars and objects.** A property present in the patch replaces the corresponding property in the
target. A property whose value is `null` in the patch deletes that property from the target
entirely, rather than setting it to null. Objects are merged key by key, recursively.

```json
// baseline
{ "costBasis": { "year": 2026, "realDiscountRate": 0.07 } }

// patch
{ "costBasis": { "realDiscountRate": 0.05 } }

// result
{ "costBasis": { "year": 2026, "realDiscountRate": 0.05 } }
```

```json
// patch that deletes a property
{ "storageSizing": { "reliabilityStandardName": null } }
```

**Most arrays are replaced wholesale**, exactly as plain RFC 7386 specifies: if a property's patch
value is an array and it is not one of the keyed arrays below, the whole array in the patch replaces
the whole array in the target.

**Five arrays are the exception** and are merged by key rather than replaced outright, so a point
can change one element without restating the rest of the array:

| Array | Key field |
|---|---|
| `regions` | `regionId` |
| `regions[].generatingFleets` | `technology` |
| `regions[].storageFleets` | `technology` |
| `monthlyCapacityFactors` | `month` |
| `interconnectors` | `fromRegionId` and `toRegionId` together |

For each of these, a patch item whose key matches an existing target item is merged into that item
(recursively, by the same rules); a patch item whose key does not match any existing item is
appended as a new item. An interconnector is identified by both of its endpoints, so a patch item
matches only the link running in that exact direction: patching `NSW1` to `QLD1` leaves the
`QLD1` to `NSW1` entry alone.

```json
// baseline region
{ "regionId": "NSW1", "dataCentreNameplateMw": 0, "generatingFleets": [ /* ... */ ] }

// patch: change one field on the existing NSW1 region, leave its fleets untouched
{ "regions": [ { "regionId": "NSW1", "dataCentreNameplateMw": 500 } ] }
```

This is exactly the shape used by the worked example below, and by `sweeps/datacentre-nameplate-fy2026.json`
in this repository: each point overrides `dataCentreNameplateMw` on a handful of regions without
needing to repeat every region's generating and storage fleets.

**Deleting a keyed array element** uses a reserved shape: an object containing *only* the key
field, or fields, and `"$remove": true`.

```json
// remove the Wind fleet from NSW1 without touching any other fleet
{
  "regions": [
    {
      "regionId": "NSW1",
      "generatingFleets": [
        { "technology": "Wind", "$remove": true }
      ]
    }
  ]
}
```

```json
// drop the NSW1-to-SA1 interconnector, leaving every other link in place
{
  "interconnectors": [
    { "fromRegionId": "NSW1", "toRegionId": "SA1", "$remove": true }
  ]
}
```

An object with `$remove: true` plus any other property, other than a key field, is rejected. The
merge cannot tell whether you meant to remove the item or edit it.

## Workflow: fan out, then run

```bash
dotnet run --project NEMSweep.CLI -- --fan-out-sweep sweeps/my-sweep.json
```

`--fan-out-sweep` applies every point's patch to the baseline, writes each resulting scenario config
to `sweeps/<sweepId>/configs/<pointId>.json`, and validates every config it writes (via the same
loading and validation `--run-scenario` uses), stopping at the first invalid point. It does not run
any dispatch. This is the fast feedback loop: while you are developing or debugging a sweep, run
fan-out repeatedly and read the generated configs to confirm each point patches what you intended.

```bash
dotnet run --project NEMSweep.CLI -- --run-sweep sweeps/my-sweep.json
```

`--run-sweep` fans out again internally, writing the same `sweeps/<sweepId>/configs/*.json` files,
and validates each config the same way `--fan-out-sweep` does. The difference is what happens when
a point is invalid: `--run-sweep` records it as that point's failure and continues on to the
remaining points, so one malformed override cannot abandon a long unattended run, whereas
`--fan-out-sweep` stops immediately so you get fast feedback while iterating.

You can run `--run-sweep` directly; there is no need to fan out first to catch a malformed override,
since `--run-sweep` validates every point itself. Running `--fan-out-sweep` first is still useful
while you are actively developing a sweep, so you can read the generated configs before committing
to a full dispatch run.

## What a run produces, and what happens when a point fails

Each point's dispatch result, status, and configuration are published under
`sweeps/<sweepId>/`. See [Outputs and provenance](outputs.md) for the full artifact layout. The
detail relevant here is failure handling: a sweep does not stop at the first failing point. Each
point gets its own `<pointId>.status.json` recording whether it succeeded or failed; if a point
fails, whatever partial dispatch artifacts it produced are deleted so a failed point never leaves
misleading output behind, and the run continues on to the remaining points. Only once every point
has been attempted does `--run-sweep` exit; if any point failed, the process exit code is `1` and
the failed point IDs are reported.

The sweep as a whole is not published atomically. Point results, status files, the sweep index and
the manifest are written as the run proceeds, so an interrupted sweep can leave a partially updated
`sweeps/<sweepId>/` directory. Rerun the sweep to bring it back into a consistent state.

## Running only some of a sweep's points

`--points` takes a comma-separated list of point IDs and runs only those.

```bash
nemsweep --run-sweep sweeps/my-sweep.json --data-root ./data --output ./out --points p0,p9,p10
```

An ID the definition does not declare is an error, and the message lists the IDs it does declare.
Running the ones that matched and staying quiet about a typo would publish a different set of
results than you asked for.

Selecting fewer than all defined points is a partial run; listing every point with `--points`
is still a full run.

**A partial run deliberately leaves the sweep's aggregate artifacts alone.** It writes each selected
point's result, status and config, and it does **not** rewrite `index.json`, the `sweeps/index.json`
manifest, or `summary.csv` and `summary.json`. Rebuilding the index from a subset would drop every
point the run did not touch, and the results site reads that index as the whole sweep. The run says
so on stdout:

```text
Partial run of 2 of 5 points. index.json, the sweep manifest and the summary files were left as they were; rerun without --points to republish them.
```

So `--points` is the iteration loop: use it while you are working on one part of an axis, and run
the whole sweep when you want the published artifacts to be current. The summary table still prints
for the points that ran.

### Without the flag: fan out, then run one config

The other route, which also works on an older CLI. `--fan-out-sweep` has already written each point
as a complete standalone scenario, so running one reproduces exactly what that point would have run
inside the sweep.

```bash
nemsweep --fan-out-sweep sweeps/my-sweep.json --data-root ./data --output ./out
```

```bash
nemsweep --run-scenario sweeps/<sweepId>/configs/p9.json --data-root ./data --output ./out/p9
```

This writes an ordinary `results*.json` set rather than a sweep artifact, so it does not join the
sweep's index at all. Fan-out writes those configs to
`sweeps/<sweepId>/configs/<pointId>.json`, relative to your working directory, and validates each
one (`NEMSweep.CLI/Scenarios/SweepFanOutCommand.cs`).

**Do not hand-trim the definition instead.** Deleting points from a copy of the sweep file works,
but the copy needs a different `sweepId` or it publishes over the full sweep's output directory, and
the trimmed file is then a second definition to keep in step with the first.

## Mapping point IDs to axis values

A point ID is an opaque label. `p9` does not say "4,500 MW"; nothing in the ID encodes the axis
value, and nothing checks that the two agree.

`--fan-out-sweep` prints the mapping and writes it beside the configs it generated, as
`sweeps/<sweepId>/configs/points.csv` relative to your working directory (the fan-out working area,
not the output root):

```text
point  Data centre nameplate added (MW)  label
p0                                    0  Baseline
p9                                 4500  +4,500 MW
p10                                5000  +5,000 MW
```

```text
pointId,axisValue,label,axisLabel,axisUnit
"p0",0,"Baseline","Data centre nameplate added","MW"
"p9",4500,"+4,500 MW","Data centre nameplate added","MW"
```

Four places hold the mapping, and which one to read depends on where you are:

| Where | When it is available | How to read it |
|---|---|---|
| `sweeps/<sweepId>/configs/points.csv`, under your working directory | after a fan-out | The table above, as CSV. |
| The sweep definition itself | always | Each entry of `points[]` carries `pointId`, `axisValue` and `label`. |
| `--run-sweep` console output | during a run | One line per point: `Running sweep point p9 (Data centre nameplate added=4500 MW).` |
| `sweeps/<sweepId>/index.json` | after a run | Each entry of `points[]` carries `pointId`, `axisValue`, `label`, `status` and that point's `scalars`. |

## The summary: what the sweep found

At the end of a full run, a sweep writes two files beside its index and prints the same rows:

```text
point  Data centre nameplate added (MW)  target  unmet %  battery MWh   SLCoE
p0                                    0     met   0.0000      14387.1  148.40
p9                                 4500     met   0.0014      97640.8  144.45
p10                                5000  NOT met   0.0241     104221.0  146.02
```

| Artifact | What it is |
|---|---|
| `sweeps/<sweepId>/summary.json` | One row per point: `pointId`, `label`, `axisValue`, `status`, `reliabilityTargetMet`, `unservedEnergyPercentageOfDemand`, `totalBatteryEnergyMwh`, `slcoeAudPerMwh`, plus the sweep's axis caption. |
| `sweeps/<sweepId>/summary.csv` | The same rows as CSV, with that header. |

Both are projections of `index.json`, which already carries every one of these values; they exist
because reading the shape of a sweep off the index means writing a program, and the shape of the
sweep is why it was run. A value a point does not have, because it failed to run, is published as
`null` in the JSON and as an empty field in the CSV, never as a zero.

Two columns are worth naming precisely. `reliabilityTargetMet` is **not** `status`: a point that
dispatched a full period and missed its target has `status: "succeeded"` and
`reliabilityTargetMet: false`. And `totalBatteryEnergyMwh` is battery alone, not the
`storageEnergyMwh` scalar, which also counts storage the sizing search cannot grow.

## Where the output lands

The output directory is named after the definition's `sweepId` field, **not** after the definition's
filename. `sweeps/my-sweep.json` with `"sweepId": "battery-capex"` publishes to
`<output root>/sweeps/battery-capex/`, and nothing warns that the two names differ. Two definitions
sharing a `sweepId` publish over each other.

`sweepId` must match `^[a-z0-9][a-z0-9-]*$`: lower-case letters, digits and hyphens, starting with a
letter or digit. That is the restriction it is, because the value becomes a directory name. The same
pattern applies to each `pointId`, which becomes a filename. Both are checked when the definition
loads, so a bad ID fails the run rather than producing an awkward path; `--fan-out-sweep` or
`--validate-scenario` catches it in seconds, without dispatching anything.

Pick a `sweepId` that names the study rather than the file, keep it stable across reruns so results
land in one place, and change it when you want a variant published alongside the original rather
than over it.

## How long it takes

Runtime is the cost of one dispatch pass multiplied by the number of passes the storage sizing search
takes. The scenario sets the first: a pass walks every interval of every region, so more regions,
more fleets or a finer resolution make each pass dearer. The search sets the second, and it is the
one that varies wildly, from a single pass to dozens between neighbouring points on the same axis.
Within one sweep the scenario is fixed and the search is what you are watching.

Measured on ordinary hardware, for the published 25-point data-centre sweep over all five regions
at hourly resolution. A scenario of a different size will not match these:

| | Time |
|---|---|
| A point needing no storage (`notRequired`) | about 2 seconds |
| A typical point that resizes | 15 to 20 seconds |
| The hardest point in the sweep | about 60 seconds |
| The whole 25-point sweep | about 8 minutes |

So a single scenario is something you iterate on, and a sweep is something you start and come back
to. A point that takes much longer than its neighbours is usually the sizing search working hard
near a transition, which is often the interesting part of the axis rather than a problem. While you
are still shaping a definition, `--fan-out-sweep` validates every point in seconds without
dispatching any of them, which is the loop to stay in until the definition is right.

## A complete minimal worked example

A two-point sweep of storage capital cost, run against the committed FY2026 example scenario.

```json
{
  "schemaVersion": 2,
  "sweepId": "battery-capex-sensitivity",
  "name": "Battery capital cost sensitivity",
  "axis": {
    "label": "Battery power capital cost",
    "unit": "AUD/MW"
  },
  "baselineConfigPath": "scenarios/nem-fy2026-all-regions.json",
  "points": [
    {
      "pointId": "baseline",
      "axisValue": 470000,
      "label": "Baseline (470,000 AUD/MW)",
      "overrides": {}
    },
    {
      "pointId": "half-capex",
      "axisValue": 235000,
      "label": "Half capital cost",
      "overrides": {
        "regions": [
          {
            "regionId": "NSW1",
            "storageFleets": [
              {
                "technology": "Battery",
                "costParameters": {
                  "powerCapitalCostAudPerMw": 235000
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

The first point's `overrides` is empty, so it reproduces the baseline exactly; its `axisValue`
records what the baseline's actual Battery capital cost is, for the chart's sake. The second point
changes only `powerCapitalCostAudPerMw` on NSW1's `Battery` fleet. The keyed-array merge means the
rest of that fleet's cost parameters, its technology profile, and every other region's fleets are
carried over from the baseline untouched.

## See also

- [Scenario configuration](scenarios.md): the format every point's `overrides` patches.
- [Outputs and provenance](outputs.md): the full artifact layout a sweep run produces.
