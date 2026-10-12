# Input bundles

An input bundle is the raw material `NEMSweep.CLI` turns into the per-region demand and weather
artifacts a scenario reads. `--validate-inputs` parses the complete bundle without writing; `--ingest`
validates it first, then writes the derived artifacts to the data root. The current bundle format
accepts portable region identifiers and does not need a generation workbook. Legacy format 1 remains
available for existing NEM bundles that carry one.

## Bundle layout

```text
<bundle-root>/
  manifest.json
  demand/
    operational-demand-hh/
      *.zip                  (one or more archives; nested ZIPs are also read)
  weather/
    <weatherDirectory>/
      *.epw                  (one file used for both roles), OR
      solar/*.epw            (exactly one EPW),
      wind/*.epw             (exactly one EPW)
  generation/                (format 1 only)
    generation-information/
      *.xlsx                  (exactly one workbook)
```

Demand ZIP archives are discovered recursively below `demand/operational-demand-hh/`; paths with a
`reference` segment are excluded. Every manifest region must map to one weather directory. That
directory contains either one EPW directly, used for both solar and wind, or exactly one EPW in each
of `solar/` and `wind/`; mixed layouts, extra entries and missing files are rejected.

## Manifest formats

New bundles use schema version 2. Add the optional `$schema` hint for editor completion and use an
explicit mapping from a free-form region identity to its weather folder:

```json
{
  "$schema": "https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/schema/manifest-v2.json",
  "schemaVersion": 2,
  "bundleId": "island-study",
  "name": "Synthetic island grid",
  "period": {
    "start": "2025-01-01T00:00:00+10:00",
    "end": "2026-01-01T00:00:00+10:00"
  },
  "regions": [
    { "regionId": "Island-X", "weatherDirectory": "station-01" }
  ]
}
```

`regionId` is preserved in demand/weather artifacts and scenario configurations. It must be nonblank
and have no surrounding whitespace; internal spaces and punctuation are allowed. Custom IDs must
fit the filename encoder's 100 UTF-8 byte limit after invariant uppercase normalization; invalid
UTF-16 is rejected. Commas, quotes and Unicode are preserved when demand CSV uses standard
escaping. IDs are unique case-insensitively. `weatherDirectory` is a separate, readable, single safe
path segment below `weather/`, is at most 255 characters, and is also unique case-insensitively. The
loader rejects path separators, dot-only names, trailing spaces or dots, invalid/control characters
and Windows reserved device names. Custom region artifact filenames use uppercase-invariant UTF-8
hex encoding, so the original ID never becomes a path component; known NEM names retain their
established lowercase filenames.
The `period` is half-open: `start` is inclusive and `end` exclusive. Both are timestamps with the
same usable fixed UTC offset (within UTC−12 to UTC+14 at quarter-hour steps). This offset becomes the
market-time offset for ingested demand and weather; daylight-saving transitions are not modelled.

A version 2 manifest is strict: unknown or malformed properties fail, except root `$schema`, which
is removed before deserialisation. See [`manifest-v2.json`](https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/schema/manifest-v2.json). Version 1
is a compatibility format: `regions` is an array of known NEM region strings, extra fields are
permissively ignored, and the generation workbook remains required. See
[`legacy-manifest-v1.json`](https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/schema/legacy-manifest-v1.json). New bundles should use version 2.

## Demand ZIP records

The ZIP contains AEMO-style CSV records. The importer selects `OPERATIONAL_DEMAND` / `ACTUAL` rows,
uses the `I` row to locate columns, and reads matching `D` records. The relevant header and record
shape is:

```text
I,OPERATIONAL_DEMAND,ACTUAL,3,INTERVAL_DATETIME,REGIONID,OPERATIONAL_DEMAND
D,OPERATIONAL_DEMAND,ACTUAL,3,2025/01/01 00:30:00,Island-X,45.0
```

`INTERVAL_DATETIME` uses `yyyy/MM/dd HH:mm:ss` and labels the **end** of the half-hour interval;
the importer associates the value with the preceding 30 minutes. Values are interval-average MW.
The manifest offset is applied to the timestamps. Every interval for every declared region across the
manifest period must be present. Identical overlapping records are deduplicated; conflicting values
for one interval fail validation. Negative demand is clamped to zero and reported as a warning with
a count; other malformed, missing, non-finite, or misaligned demand fails.

## EPW weather sources

A weather file is an hourly EnergyPlus Weather (EPW) file. NEMSweep reads the `LOCATION` header for
city, WMO station, latitude, longitude and source time zone; the `HOLIDAYS/DAYLIGHT SAVING` header
for the leap-year flag; and `DATA PERIODS` for the one hourly period. The data must contain one
contiguous calendar year: 8,760 rows when the leap flag is `No`, or 8,784 when it is `Yes`. EPW hour
labels are 1–24, with hour 1 denoting the first hour of its date. The source year itself is ignored.
Subhourly EPW sources and multiple data periods are rejected.

From each 22-column data row the importer consumes dry-bulb temperature (column 7), global
horizontal radiation (14), direct normal radiation (15), diffuse horizontal radiation (16), and wind
speed (22). EnergyPlus missing-value sentinels and implausible values are errors: radiation must be
finite in `[0, 9999)`, wind speed finite in `[0, 40]` m/s (so its `999` sentinel is rejected), and
dry-bulb temperature finite in `[-70, 70]` °C. The EPW time zone must be within one hour of the
manifest market offset; accepted series are shifted/interpolated onto that offset's hourly grid.
Leap-year sequence validation includes February 29 when the header says the source is a leap year.

Solar and wind roles can use separate EPWs. The solar site's coordinates supply the model's solar
geometry and reported regional location; the wind site's coordinates remain source provenance.

## Derived artifacts

Successful ingestion writes one demand artifact and one weather artifact per region into the data
root. Filenames are `demand-<region-file-name>.json` and `weather-<region-file-name>.json`; use those
basenames in scenario `demandFile` and `weatherFile` fields. Format schemas are available with
`--describe-schema demand` and `--describe-schema weather`, or at
[`demand-v2.json`](https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/schema/demand-v2.json) and [`weather-v6.json`](https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/schema/weather-v6.json).

The demand artifact has `schemaVersion`, `scenario` (`id`, `region`, `periodStart`, exclusive
`periodEnd`, `resolution`, `aggregation`), `generatedAt` in UTC, `dataSources.demandSourceFiles`, and
`dataSeries.demandMw`. The values remain half-hour interval-average MW; the scenario runner resamples
them to the model's hourly timestep. Timestamps are ISO 8601 date-times carrying the market offset;
`resolution` is a serialized `TimeSpan` (`00:30:00`).

The weather artifact has `schemaVersion`, `regionId`, `start`, `resolution`, `solar` and `wind`.
Each role records its source filename and `location`; solar contains radiation, solar zenith and
ambient-temperature arrays, and wind contains speed and measurement height. Those arrays are
positional against `start` at the hourly `resolution` (`01:00:00`). The `production...` arrays are
model-derived, recomputed during ingestion from the EPW weather inputs; they are result traces, not
configuration controls to edit. All series in one run share the same fixed UTC offset.

For output values, dispatch traces and provenance digests, see [Outputs](outputs.md). The version
numbers of other artifacts are listed by `--describe-schema`; result layouts are documented in the
[API reference](../api/index.md).

## Validate and ingest

From the repository root, pass the bundle path and choose a data root. These commands do not require
the repository's ignored NEM source bundle:

```bash
dotnet run --project NEMSweep.CLI -- --validate-inputs examples/custom-grid/generated/bundle

dotnet run --project NEMSweep.CLI -- --ingest examples/custom-grid/generated/bundle \
  --data-root examples/custom-grid/generated/data
```

Validation writes nothing. Ingestion validates all demand and weather inputs before writing; for v1
it additionally reads the generation workbook and emits `generation-information.json`. For a
runnable all-synthetic example, including generation of the complete annual bundle and scenario
files, see [Custom grids](custom-grids.md).

## See also

- [Scenario configuration](scenarios.md): how a scenario binds its regions to ingested artifacts.
- [CLI reference](cli.md): command syntax and root overrides.
- [Limitations](../assumptions/limitations.md): model assumptions that affect results.