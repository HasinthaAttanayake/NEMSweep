# Schemas

`NEMSweep.CLI` emits JSON Schema (draft 2020-12) for its editable inputs and the two derived input
artifacts. Editors use `$schema` for completion, inline validation and field documentation; the CLI
also prints the same versioned documents locally, without requiring a workspace or network access.

## Formats

| `--describe-schema` format | Committed document | Describes |
|---|---|---|
| `manifest` | `schema/manifest-v2.json` | Current strict input-bundle manifest; region ID to weather-folder mapping. |
| `legacy-manifest` | `schema/legacy-manifest-v1.json` | Permissive compatibility manifest with string NEM regions. |
| `demand` | `schema/demand-v2.json` | Half-hour demand artifact produced by ingestion. |
| `weather` | `schema/weather-v6.json` | Hourly weather and derived renewable traces produced by ingestion. |
| `scenario` | `schema/scenario-v7.json` | Current scenario configuration. |
| `sweep` | `schema/sweep-v2.json` | Current sweep definition and override patches. |

`schema/scenario-v6.json` and `schema/sweep-v1.json` are retained unchanged for files already
pinned to those historical schema URLs. New files should point to version 7 and 2, respectively.
Schema filenames and `$id` values are versioned so a later document cannot silently change the
meaning of a cached schema.

Add the current schema URL at the root of an editable JSON file:

```json
{
  "$schema": "https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/schema/scenario-v7.json",
  "schemaVersion": 7,
  "id": "my-scenario"
}
```

The same hint is accepted for scenario and sweep configs and for strict bundle manifest version 2. When a legacy scenario is normalized for a version-2 sweep, an existing `$schema` hint is updated to the current scenario URL.
It is not a serialized field and does not enter input provenance or hashes. The CLI otherwise rejects
unknown properties in these current config shapes. Legacy manifest version 1 is the exception: it
is deliberately permissive for compatibility and remains limited to known NEM region names.

## Scenario execution modes

Scenario version 7 defaults an omitted `executionMode` to `fixedFleet`. The required `reliability`
object always carries an explicit `targetUsePercentage` in `(0, 100]`; `standardName` is an optional
label. In `fixedFleet`, the declared generating and storage fleets are dispatched unchanged and
`storageSizing` must be omitted or `null`. An empty `storageFleets` array is valid.

`sizeBattery` requires `storageSizing` with `maximumPowerMw` and `maximumEnergyMwh`, and optionally
`maximumPasses`. Every region must also declare a `Battery` entry in `storageFleets`, because that
entry supplies the Battery cost and technology assumptions; a zero/zero initial capacity is valid.
These are only the bounded Battery-sizing search limits. Reliability fields belong under
`reliability`, not inside `storageSizing`. Scenario v5/v6 inputs are upgraded by the CLI to v7
sizeBattery scenarios; their reliability values are moved out of the old sizing object, with the
historic default applied when the old target was absent. An upgraded config still has to satisfy the
current Battery-template requirement.

Sweep version 2 can patch `executionMode` and `reliability`, and normalizes a legacy scenario
baseline before applying each patch. Version 1 keeps its historical override shape, accepts the
legacy `storageSizing` patches, and rejects the newer `executionMode` and `reliability` override
fields. A schema describes the patch shape; only fan-out validation against the merged scenario can
establish that a particular patch leaves a runnable scenario.

## Print or regenerate schemas

```powershell
dotnet run --project .\NEMSweep.CLI -- --describe-schema scenario
dotnet run --project .\NEMSweep.CLI -- --describe-schema manifest
```

Redirect the selected format to its committed file after changing its producer, for example:

```powershell
dotnet run --project .\NEMSweep.CLI -- --describe-schema scenario > schema/scenario-v7.json
```

CI regenerates every current document and diffs it against the committed copy. Do not hand-edit
those generated JSON files; the CLI producer is authoritative. Historical schemas are not
regenerated, which keeps them frozen.

A schema checks JSON shape, not all semantic or cross-file constraints. For example, the CLI checks
period-offset agreement, uniqueness of region IDs and weather folders, filesystem-safe names,
source coverage and array alignment in addition to the constraints expressible in JSON Schema. See
[Input bundles](input-bundles.md), [Scenario configuration](scenarios.md), [Sweeps](sweeps.md), and
[Outputs](outputs.md) for those rules and their context.