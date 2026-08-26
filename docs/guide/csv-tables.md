# CSV tables

CSV tables are a `NEMSweep.CLI` output format, projected from the JSON artifacts. JSON is the
contract every artifact is defined by, but it is not something you can open. Add `--csv` to any run
and it also writes a **star schema**: narrow fact tables at the hour grain, joined to small
dimension tables.

```bash
nemsweep --run-scenario scenarios/my-scenario.json --output ./study --csv
```

Tables land in `{outputRoot}/csv/`. Nothing else about the run changes: the JSON is written exactly
as before, so asking for CSV never costs you the contract.

## Why this shape

The two obvious consumers want opposite things. A spreadsheet wants a wide table you can pivot; a
data model wants long facts you can join and filter. The useful part is that the shape which
strains a spreadsheet is the shape every analytical tool prefers, so this resolves in favour of
tidy rather than being a genuine trade-off.

The layout is ordinary [Kimball](https://en.wikipedia.org/wiki/Star_schema) dimensional modelling,
which predates and will outlive any particular tool. Nothing here is specific to one vendor: the
same folder loads into a BI tool, a notebook, an R session or an embedded analytical database
without alteration.

## The tables

| Table | One row per | Carries |
|---|---|---|
| `fact_dispatch` | point, region, hour | demand, curtailment, unserved, charge, discharge, imports, exports, losses |
| `fact_generation` | point, region, hour, **technology** | `deliveredMw` |
| `fact_storage` | point, region, hour, **technology** | `stateOfChargeMwh` |
| `fact_interconnector` | point, link, hour | `flowMw`, `lossesMw`, capacity, route length |
| `fact_scalars` | point, scope | the published scalars, one column each |
| `dim_time` | hour | timestamp, date, hour of day, month, quarter, financial year, weekday |
| `dim_region` | region | latitude and longitude |
| `dim_technology` | technology | category, whether renewable |
| `dim_scalar` | scalar | label, unit, whether chartable |
| `dim_point` | sweep point | label, axis value, status, storage sizing outcome |

Join facts to dimensions on `hourIndex`, `regionId`, `technology` and `pointId`. `dim_scalar` is the
exception: `fact_scalars` is wide, so its `scalarName` names one of that table's measure columns
rather than matching a value in a row. Use it to label and unit a column, not as a join key.
`dim_point` is written for a sweep only; a standalone run is a single point and has nothing to
describe.

## Two deliberate choices

**Technology is unpivoted.** It is a real dimension, so one row per technology per hour rather than
a column per technology. That is what lets you filter and colour by technology without the schema
changing the day a sixth one appears.

**Scalars stay wide.** Their units are heterogeneous, and collapsing AUD/MWh, MWh, fractions and
hours into a single `value` column is a units error waiting to happen. It also keeps
`fact_scalars.csv` down to a handful of rows, which makes it the one table you can open in a
spreadsheet and read directly. `dim_scalar` carries the label and unit for each column, keyed by the
column name: it describes the shape of `fact_scalars`, and is the one dimension you do not join to.

## Things that will bite you otherwise

**Hours are keyed by an integer, not a timestamp.** `hourIndex` runs from 1 to 8,760, and the ISO
timestamp rides alongside it in `dim_time`. Spreadsheets reinterpret ISO 8601 dates by locale on
import, which silently swaps day and month on an Australian machine. An integer cannot be mangled,
and it makes the join back to the JSON obvious.

**A sweep splits facts per point and shares its dimensions.**

```
sweeps/{sweepId}/csv/
  dim_time.csv  dim_region.csv  dim_technology.csv  dim_scalar.csv  dim_point.csv
  points/
    p0/  fact_dispatch.csv  fact_generation.csv  ...
    p1/  ...
```

Facts are split because a combined `fact_generation` for a twenty-five point sweep runs to millions
of rows, and a spreadsheet **truncates past 1,048,576 rows without erroring**: you get a file that
looks complete and a total that is quietly wrong. Every emitted table stays inside that ceiling.

Dimensions are shared because they are identical across points, and repeating the calendar
twenty-five times would be megabytes of the same rows.

Recombining the facts is the easy direction, and every row carries `pointId` so a concatenated set
stays self-describing. Point any tool that reads a folder of identically shaped files at `points/`,
or glob it. A rerun deletes the directories of points it no longer produces, so what the folder
holds is always the run `dim_point.csv` describes.

**Numbers are rounded the way the JSON rounds them.** Same rule, one definition, so a figure read
here matches the one read there.

## See also

- [Outputs and provenance](outputs.md): the JSON these tables are projected from.
- [CLI reference](cli.md#the-workspace): where the output root comes from.
- [Sweeps](sweeps.md): the definition format a study is built on.
