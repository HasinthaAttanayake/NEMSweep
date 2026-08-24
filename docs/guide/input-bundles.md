# Input bundles

An input bundle is a directory of raw upstream source files (AEMO demand archives, an AEMO
generation workbook and EnergyPlus weather files) plus a manifest describing what the bundle claims
to cover. It exists so that everything a run depends on can be validated as a unit, in one place,
before anything derived from it is written. Bundle ingestion is all-or-nothing: `--ingest` loads
the whole bundle, checks the shape and content of every file in it, and only then produces the
per-region JSON that scenarios actually reference.

The single-source commands (`--import-demand`, `--generation-information` and `--epw-report`) read
one kind of source at a time and are there for iterating on a single input. They bypass the
whole-bundle check, so a bundle you intend to publish from should still go through
`--validate-inputs` and `--ingest`.

The live bundle lives at `NEM.CLI/data/nemsim-inputs/` and is gitignored, because it is large and
is derived from third-party sources you fetch yourself rather than something the repository
carries. What follows describes its required shape rather than assuming you have a copy of it
open.

## Directory layout

`InputBundle.Load` enforces this shape exactly; a bundle that deviates from it fails to load rather
than being partially accepted.

```
<bundle-root>/
  manifest.json
  demand/
    operational-demand-hh/
      *.zip                  (at least one; anything under a "reference" path segment is excluded)
  weather/
    <REGION>/
      *.epw                  (exactly one file, used for both solar and wind), OR
      solar/
        *.epw                (exactly one file)
      wind/
        *.epw                (exactly one file)
  generation/
    generation-information/
      *.xlsx                 (exactly one file)
```

One `weather/<REGION>/` folder is required for every region listed in the manifest's `regions`
array. Each region's weather folder must take one of two shapes: either a single `.epw` file
directly in the region folder (used for both the solar and wind role), or a `solar/` subdirectory
and a `wind/` subdirectory, each containing exactly one `.epw` file. Any other shape is rejected:
two files loose in the region folder, an extra subdirectory, or zero files in a role folder.

Demand archives are discovered recursively under `demand/operational-demand-hh/`, but any path
containing a `reference` segment is skipped, so you can keep original downloads alongside the
archives NemSim actually reads without them being picked up twice.

## The manifest

`manifest.json` at the bundle root:

| Field | Type | Required | Meaning |
|---|---|---|---|
| `schemaVersion` | integer | yes | Must equal the version the CLI supports (currently `1`). |
| `bundleId` | string | yes | Identifier for the bundle. Expected to match the name of the folder it lives in; a mismatch is a warning, not a rejection. |
| `name` | string | yes | Human-readable name. |
| `period` | object | yes | `start` and `end`, both timestamps; `end` must be after `start`. The calendar period the bundle's inputs are intended to cover. |
| `regions` | array of string | yes, at least one | NEM region identifiers (`NSW1`, `QLD1`, `SA1`, `TAS1`, `VIC1`) the bundle supplies weather for. Every entry must be a recognised region, and duplicates are rejected. |

## Where the source data comes from

- **Demand.** AEMO's actual operational demand half-hourly archives (`PUBLIC_ACTUAL_OPERATIONAL
  _DEMAND_HH_*` and similar AEMO nemweb archive naming). NemSim reads the `.zip` archives in place
  rather than requiring you to extract them first.
- **Generation.** AEMO's Generation Information workbook, a single `.xlsx` file listing existing
  and committed generating unit capacity, technology, and status.
- **Weather.** EnergyPlus Weather (EPW) files, the standard hourly weather-file format used by
  building- and energy-simulation tools. Here they supply solar radiation, wind speed, and ambient
  temperature for one representative site per region.

## Weather role assignment

Each region names two EPW sources: a **solar** site and a **wind** site. They are chosen
independently and for different reasons, the solar site for the quality of its solar resource and
the wind site for the quality of its wind resource, so they are frequently different physical
locations within the region.

This matters beyond generation shape. The **solar** site's coordinates are the model's *only*
source of that region's location, published on every dispatch result as that region's endpoint
coordinates for map display (see [Scenario configuration](scenarios.md#interconnectors)). Transmission
cost does not depend on it: an interconnector's route length is a scenario value you declare
directly, not derived from a region's weather file.

No file in the repository records why a given site was chosen for a given role. That context is
currently held only by the person who built the bundle. Document your own reasoning somewhere
durable if you change an assignment.

### The FY2026 bundle's current assignment

Checked directly against `NEM.CLI/data/nemsim-inputs/manifest.json` and the `weather/` subdirectory
contents. The bundle currently covers all five NEM regions:

| Region | Solar site | Wind site |
|---|---|---|
| NSW1 | Dubbo City Regional Airport | Armidale Airport |
| QLD1 | Gladstone Airport | Kingaroy Airport |
| SA1 | Port Augusta Airport | Port Augusta Airport (same file as solar) |
| TAS1 | Cape Grim Baseline Air Pollution Station | Cape Grim Baseline Air Pollution Station (same file as solar) |
| VIC1 | Ballarat Airport | Ballarat Airport (same file as solar) |

SA1, TAS1 and VIC1 each supply one EPW file directly under `weather/<REGION>/` rather than separate
`solar/`/`wind/` subfolders, so the same site serves both roles for those three regions. NSW1 and
QLD1 supply distinct solar and wind sites. This table reflects the file layout at the time this page
was written; treat the manifest and the `weather/` folder as the source of truth if they have since
changed. The bundle's own `README.md` predates the QLD1, SA1 and TAS1 additions and still describes
an NSW1/VIC1-only bundle, so it is stale against the current manifest and folder contents.

## Validating and ingesting

Validation and ingestion are two separate steps, and only one of them writes anything.

```bash
dotnet run --project NEM.CLI -- --validate-inputs
```

`--validate-inputs` loads the manifest, discovers every file the bundle shape requires, parses the
demand archives, the weather files and the generation workbook, and reports what it found,
including any negative demand intervals it had to clamp to zero. It writes nothing to disk. Run it
first, especially after replacing any source file.

```bash
dotnet run --project NEM.CLI -- --ingest
```

`--ingest` runs the same validation and then writes the per-region `demand-<region>.json` and
`weather-<region>.json` files plus `generation-information.json` that scenario configs reference via
`demandFile` and `weatherFile`. Both commands accept an optional bundle path argument to validate or
ingest a bundle other than the configured default. See [CLI reference](cli.md) for the full command
list.

## What validation rejects

Malformed, incomplete, duplicated, or misaligned source data is an explicit failure, not something
NemSim tries to repair on your behalf. This includes: a demand archive missing an expected column,
two demand readings that conflict for the same interval, weather series that don't line up on the
calendar-hour grid the scenario period expects (including a weather source with no entry for 29
February), and a generation workbook missing a required column. The one narrow exception is
negative demand values, which are clamped to zero rather than rejected. That clamping is reported
as a warning at validation time, not silently absorbed.

## See also

- [Scenario configuration](scenarios.md): how `demandFile` and `weatherFile` are referenced once a
  bundle has been ingested.
- [Limitations](../assumptions/limitations.md): the consequences of the solar-site-as-location
  assumption above.
- [CLI reference](cli.md): the full set of commands, including `--validate-inputs` and `--ingest`.
