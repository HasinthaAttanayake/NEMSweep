# Data licensing and provenance

The code in this repository is BSD-3-Clause (see [LICENSE.md](LICENSE.md)). **The data is not
covered by that licence**, and this page exists so nobody has to guess where the line falls.

## The short version

| What | Licence |
|---|---|
| Everything under `NEMSweep.Model`, `NEMSweep.Contracts` and `NEMSweep.CLI` | BSD-3-Clause |
| The published JSON schemas under `schema/` | BSD-3-Clause |
| Scenario and sweep configurations under `scenarios/` and `sweeps/` | BSD-3-Clause |
| Demand, weather and generation artifacts, and anything derived from them | **See below. Upstream terms apply.** |

If you are redistributing NEMSweep, the code is straightforward. If you are redistributing the
**data**, or results derived from it, satisfy yourself about the upstream terms first.

## Upstream sources

### Operational demand

Derived from AEMO's actual operational demand half-hourly archives, published on AEMO's NEMWeb.
Ingested by `--ingest` into `demand-{region}.json`. The bundle behind the published example uses the
weekly archives covering 1 July 2025 to 30 June 2026, the 2026 Australian financial year.

AEMO publishes this data under its own terms of use. Those terms govern reuse and redistribution,
and they are not compatible-by-default with an open-source code licence. Read AEMO's current
copyright and data-use statements before redistributing the archives or artifacts derived from them.

### Generation information

Derived from AEMO's Generation Information workbook, which lists existing and committed generating
unit capacity, technology and status. Ingested into `generation-information.json`. The bundle behind
the published example uses the April 2026 edition of the workbook.

Same position as demand: AEMO's terms apply to the workbook and to what is derived from it.

### Weather

Derived from EnergyPlus Weather (EPW) files, the standard hourly weather format used by building and
energy simulation tools. Ingested into `weather-{region}.json`. The bundle behind the published
example uses TMYx typical-year files assembled over the 2011 to 2025 period of record, one site per
role per region.

EPW files are assembled from national meteorological sources and are distributed under the terms of
whoever compiled and published the particular file. Those terms vary by source and by site, and some
prohibit redistribution. Check the terms attached to the specific files you use.

## What this repository redistributes

Nothing. Neither the raw source files nor the ingested artifacts derived from them are committed
here. The input bundle is gitignored, because it is large and because it is third-party material you
fetch yourself, and the ingested demand, weather and generation artifacts are produced by `--ingest`
into a data root you choose.

The artifacts published alongside the results site are ingested derivatives of the above, retained
so the site has something to display. **They are an illustrative example, not a dataset**, and they
are not offered under the code licence.

## If you are building your own

The cleanest position is to bring your own inputs. Assemble an [input bundle](docs/guide/input-bundles.md)
from sources whose terms you have checked, run `--ingest`, and the artifacts are then yours to handle
under those terms. Nothing in the model depends on the particular data this repository happens to
carry.

## A note on the weather sites

Which EPW site represents a region is an editorial judgement, not a fact the model derives. The
choice affects both the generation shape and, because the solar site's coordinates are the model's
only source of a region's location, where the region is drawn on a map. See
[Input bundles](docs/guide/input-bundles.md) for the current assignment and the reasoning behind it.

## Corrections

If any statement here misrepresents an upstream licence, please open an issue. This page is a
good-faith summary written by the maintainer, not legal advice, and it should be corrected rather
than relied upon where it is wrong.
