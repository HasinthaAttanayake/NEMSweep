# Data licensing and provenance

The code in this repository is BSD-3-Clause (see [LICENSE.md](LICENSE.md)).
**That software licence does not relicense third-party input data.** This page
identifies the published NEM example's sources and their separate reuse conditions.
EPW is a file format, not a publisher or a licence.

## The short version

| What | Licence |
|---|---|
| Everything under `NEMSweep.Model`, `NEMSweep.Contracts` and `NEMSweep.CLI` | BSD-3-Clause |
| The published JSON schemas under `schema/` | BSD-3-Clause |
| Scenario and sweep configurations under `scenarios/` and `sweeps/` | BSD-3-Clause |
| Third-party source inputs and demand, weather or generation artifacts made from them | **Source-specific terms; not automatically BSD-3-Clause. See below.** |

If you are redistributing NEMSweep, the code is straightforward. If you are redistributing the
**data**, or results derived from it, satisfy yourself about the upstream terms first.

## Upstream sources

### Operational demand

Derived from the Australian Energy Market Operator (AEMO)'s actual operational demand
half-hourly archives, published through
[NEMWeb market data](https://www.aemo.com.au/energy-systems/electricity/national-electricity-market-nem/data-nem/market-data-nemweb).
Ingested by `--ingest` into `demand-{region}.json`. The bundle behind the published example uses the
weekly archives covering 1 July 2025 to 30 June 2026, the 2026 Australian financial year.

AEMO's [Copyright Permissions Notice](https://www.aemo.com.au/privacy-and-legal-notices/copyright-permissions)
gives general permission to use publicly available AEMO Material for any purpose
with accurate and appropriate attribution to the material and AEMO. It says specific
permission is not needed for those uses. Confidential documents and third-party-owned
commissioned reports are excluded. Its
[Privacy and Legal Notices](https://www.aemo.com.au/privacy-and-legal-notices)
also apply. This is AEMO's own permission, not a BSD or Creative Commons licence.

Attribute the provider, actual operational-demand dataset and period used, and link
the original source and permission notice. Identify NEMSweep modelling and results
separately rather than implying that AEMO authored or endorsed them.

### Generation information

Derived from AEMO's Generation Information workbook, which lists existing and committed generating
unit capacity, technology and status. Ingested into `generation-information.json`. The bundle behind
the published example uses the April 2026 edition of the workbook.

Check the workbook's source notice and the AEMO permissions above; the software
licence does not itself license the workbook. Legacy format-1 imports use this
input, but format-2 bundles do not require it.

### Weather

Derived from EnergyPlus Weather (EPW) files in
[Climate.OneBuilding.org's Australian collection](https://climate.onebuilding.org/WMO_Region_5_Southwest_Pacific/AUS_Australia/index.html).
These are ingested into `weather-{region}.json`. The published example uses seven
TMYx 2011-2025 EPWs, with separate solar/wind sites for NSW1 and QLD1 and one site
shared by both solar and wind roles in each of SA1, TAS1 and VIC1. These are historical typical-year profiles,
not weather observations for FY2026.

The publisher's [source description](https://climate.onebuilding.org/sources/default.html)
credits NOAA/NCEI Integrated Surface Database observations and ERA5 reanalysis
solar-radiation data, courtesy of Oikolab. Its
[official citation and disclaimer](https://climate.onebuilding.org/contact/default.html) are:

> Lawrie, Linda K, Drury B Crawley. 2026. Development of Global Typical Meteorological Years (TMYx). https://climate.onebuilding.org

Copyright (c) 2014-2026 Climate.OneBuilding. All Rights Reserved.

Retain original filenames, all eight header lines and every weather-record byte.
Record attribution and processing explanations separately, not by editing the EPWs.
Weather is supplied as-is; the publisher disclaims liability for data quality,
completeness and consequences of use.

Check the terms for the specific files you use before redistributing them.
Public download availability, a citation, or the underlying sources' licences do
not by themselves establish redistribution permission for the compiled EPWs.
This notice does not grant that permission or relicense the files under BSD.
The separate [samples repository's licence notice](https://github.com/HasinthaAttanayake/NEMSweep.Samples/blob/3f863feb9822eb5eb508d8374db8c40dc4fa559f/LICENSE.md#third-party-advanced-input-data)
links the seven original publisher downloads and identifies the unaltered files
included in its Advanced candidate; this is separate from this repository's software.

## What this repository redistributes

Neither the raw source files nor the ingested artifacts derived from them are committed
in this software repository. The input bundle is gitignored, because it is large and because it is third-party material you
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

For Australian NEM demand, start with [AEMO NEMWeb](https://www.aemo.com.au/energy-systems/electricity/national-electricity-market-nem/data-nem/market-data-nemweb).
For EPW weather, start with [Climate.OneBuilding.org](https://climate.onebuilding.org/)
or its [Australian collection](https://climate.onebuilding.org/WMO_Region_5_Southwest_Pacific/AUS_Australia/index.html).
These are optional upstream sources, not a restriction on portable bundles.
Keep source URLs, filenames, the demand period, the EPW record window, any known
download date, SHA256 hashes and applicable reuse terms with your study. Do not
infer an original download date from a repackaged ZIP timestamp.

## A note on the weather sites

Which EPW site represents a region is an editorial judgement, not a fact the model derives. The
choice affects both the generation shape and, because the solar site's coordinates are the model's
only source of a region's location, where the region is drawn on a map. See
[Input bundles](docs/guide/input-bundles.md) for the current assignment and the reasoning behind it.

## Corrections

If any statement here misrepresents an upstream licence, please open an issue. This page is a
good-faith summary written by the maintainer, not legal advice, and it should be corrected rather
than relied upon where it is wrong.
