# Producer requests from NEM.Web, round two

Work brief for an agent changing `NEM.Contracts`, `NEM.CLI` and `NEM.Model`.

Round one — [`producer-requests-from-nem-web.md`](producer-requests-from-nem-web.md),
delivered in pull request #103 — closed every gap where the site was *saying
something wrong*: a region's weather basis, regional losses, transmission priced
at zero, cost by bucket rather than by fleet, sizing outcome without its path.
Those are consumed on `nem-075` and nothing here revisits them.

What is left is mostly about **weight**. The site is correct and slow-ish on
first load, and the reason is that the artifacts publish one shape for every
consumer: a page that needs ten integrated numbers downloads and parses a dozen
8,760-element arrays to get them. Item 1 below is the biggest single win, item 2
is nearly free, and items 3 to 5 are smaller.

Every figure quoted here was measured on the artifacts in
`NEM.Web/wwwroot/data/` at commit `a7e388e`, not estimated.

---

## Before starting

**Read first.** `.github/instructions/contracts.instructions.md` and
`.github/instructions/cli-data-pipeline.instructions.md` govern this work. The
rule that shapes every item:

> For a breaking shape or meaning change, increment the artifact schema version
> and update the CLI producer, every web consumer, and the relevant round-trip
> contract test in the same change.

**So each item is four edits, not one:**

1. The DTO in `NEM.Contracts`.
2. The version constant in `NEM.Contracts/ArtifactSchemaVersions.cs`.
3. The producer in `NEM.CLI` (usually `Scenarios/DispatchResultsExport.cs` or
   `Scenarios/SweepArtifactExport.cs`).
4. Every `NEM.Web` consumer, listed per item below. The web validates schema
   versions on load, so a bumped version with an unchanged consumer does not fail
   quietly: every affected page shows "Artifact schema N is not supported".

**Regenerate artifacts, never hand-edit them.**

```bash
dotnet run --project ./NEM.CLI/NEM.CLI.csproj -- --run-scenario
```

```bash
dotnet run --project ./NEM.CLI/NEM.CLI.csproj -- --run-sweep ./sweeps/datacentre-nameplate-nsw1-fy2026.json
```

```bash
dotnet run --project ./NEM.CLI/NEM.CLI.csproj -- --run-sweep ./sweeps/renewable-penetration-nsw1-fy2026.json
```

**Verify.**

```bash
dotnet build ./NemSim.slnx
```

```bash
dotnet test ./NemSim.slnx
```

```bash
dotnet run --project ./NEM.Web/NEM.Web.csproj
```

Then open `/`, `/regions`, `/dispatch`, `/dispatch?region=NSW1`,
`/dispatch?region=VIC1`, `/inputs/weather`, `/inputs/demand`,
`/inputs/generation` and both sweep pages, and confirm none of them shows a
schema or invalid-data message.

**Round-trip contract tests live in**
`NEM.CLI.Tests/Contracts/SystemAndRegionDispatchResultsContractTests.cs` and
`NEM.CLI.Tests/Scenarios/SweepIndexContractTests.cs`. Extend those rather than
adding parallel files.

**Do not change `NEM.Web` beyond what a contract change forces.** The analysis
layer, plots and pages are settled; the edits these items need are mechanical
(reading a new field, or reading a smaller artifact instead of a larger one).

---

## 1. A region has no overview, so a regional page parses a year of series to state a year's totals

**Priority: highest — this is the same change that worked for the system, applied
one level down.**

### Evidence

`SystemDispatchOverviewDTO` was the win of round one: `results-overview.json` is
**19 KB** against `results.json` at **2,101 KB**, carrying every integrated figure
and no interval series. `/` and `/regions` now open on it and are complete on
first paint.

The regional artifacts got no equivalent:

| File | Size |
| --- | --- |
| `results-overview.json` | 19 KB |
| `results.json` | 2,101 KB |
| `results-nsw1.json` | **1,616 KB** |
| `results-vic1.json` | **1,626 KB** |

`results-nsw1.json` carries **131,400 interval values** across fifteen series.
Opening `/dispatch?region=NSW1` parses all of them, and a large part of what the
page states above the fold is integrated totals it could have been handed:

- The renewable share and the generation-mix bar come from
  `NEM.Web/Services/Insights/EnergyMix.From`, which integrates
  `deliveredGenerationByTechnologyMw` — five 8,760-element arrays — to produce
  five numbers the system artifact already publishes per region as
  `deliveredGenerationByTechnologyMwh`.
- The transmission-losses figure integrates `transmissionLossesMw` to one number.
- The cost decomposition needs the mix and nothing else from the series.

So the regional artifact is parsed in full to produce figures the *system*
artifact already carries, purely because the regional page reads the regional
file.

### Change

Publish `results-{regionId}-overview.json` with a
`RegionDispatchOverviewDTO`: the same fields as `RegionDispatchResultsDTO` minus
`DataSeries`, plus the integrated `DeliveredGenerationByTechnologyMwh` and
`TransmissionLossesMwh` that the page currently derives.

Mirror `SystemDispatchOverviewDTO` exactly — same naming, same "deliberately
contains no interval series" remark, its own `RegionDispatchOverview` schema
constant. A reviewer should be able to read the two side by side.

Reference it from `RegionDispatchSummaryDTO` next to `DetailPath`, so a consumer
finds it without constructing a filename:

```csharp
public sealed record RegionDispatchSummaryDTO(
    ...,
    Dictionary<string, double> DeliveredGenerationByTechnologyMwh,
    string? DetailPath = null,
    string? OverviewPath = null);
```

### Files

- `NEM.Contracts/RegionDispatchOverviewDTO.cs` — new, alongside
  `SystemDispatchOverviewDTO.cs`.
- `NEM.Contracts/SystemDispatchResultsDTO.cs` — `OverviewPath` on the summary.
- `NEM.Contracts/ArtifactSchemaVersions.cs` — new `RegionDispatchOverview`
  constant; bump `SystemDispatchResults`, `SystemDispatchOverview`,
  `RegionDispatchResults`.
- `NEM.CLI/Scenarios/DispatchResultsExport.cs` — emission.
- `NEM.Web/Services/ArtifactLoader.cs` — register the type in
  `ArtifactSchemaRegistry`.
- `NEM.Web/Pages/Dispatch.razor` — the one consumer that matters, and the only
  `NEM.Web` change this item should need beyond registration.

### Acceptance

- `results-nsw1-overview.json` is under 30 KB and carries no array of length 8,760.
- `/dispatch?region=NSW1` states peak demand, energy delivered, renewable share,
  the full cost decomposition and the sizing trajectory **before** any artifact
  over 100 KB has been fetched.
- The hourly artifact is still fetched, for the charts — but after the page is
  readable, the way `/regions` fetches `results.json` today.

---

## 2. Sweep point overviews are published but unreachable

**Priority: high — the work is done, and one nullable string finishes it.**

### Evidence

#103 emits a per-point overview for every sweep run:

```
NEM.Web/wwwroot/data/sweeps/datacentre-nameplate-nsw1-fy2026/points/p0-overview.json
```

20 files, **0.31 MB**, about 10 KB each. `SweepIndexPointDTO` carries
`DetailPath` and `ConfigPath` and nothing else, so the site has no path to
follow and no way to know they exist. They are published and dead.

### Change

Add `OverviewPath` to `SweepIndexPointDTO` beside `DetailPath`, and to
`SweepPointRegionDetailDTO` for the regional ones.

### Files

- `NEM.Contracts/SweepIndexDTO.cs`.
- `NEM.Contracts/ArtifactSchemaVersions.cs` — bump `SweepIndex`.
- `NEM.CLI/Scenarios/SweepArtifactExport.cs`.
- `NEM.Web/Services/SweepIndexLoader.cs` — `ValidatePointStatus` requires a
  succeeded point to declare a detail path and forbids a failed one from
  declaring anything; the new path needs the same treatment on both arms, or a
  failed point can carry an overview it has no results for.

### Acceptance

- Every succeeded point in a regenerated index carries an `overviewPath` that
  resolves to a file on disk.
- `SweepIndexContractTests` asserts it round-trips and that the file named exists.

---

## 3. Every sweep point re-publishes series that are identical across the whole sweep

**Priority: medium — 69 MB is most of the repository's weight.**

### Evidence

`NEM.Web/wwwroot/data/sweeps/` is **69.1 MB across 109 files**:

| Kind | Size |
| --- | --- |
| Point detail (`pN.json`) | 35.65 MB |
| Point region detail (`pN-nsw1.json`) | 32.73 MB |
| Point overview (`pN-overview.json`) | 0.31 MB |
| Externalised series (`series/`) | 0.21 MB |
| Index and configs | 0.23 MB |

Hashing each interval series across the 15 succeeded points of the data-centre
sweep shows how much of that is the same bytes written fifteen times:

| Series | Distinct values across 15 points |
| --- | --- |
| `demand.baseDemandMw` | **1** |
| `exportsMw` | **1** |
| `importsMw` | **1** |
| `transmissionLossesMw` | **1** |
| `deliveredGenerationByTechnologyMw.Solar` | 9 |
| `unservedDemandMw` | 10 |
| `chargeMw`, `dischargeMw`, `stateOfChargeByTechnologyMwh.Battery` | 12 |
| `curtailmentMw`, `deliveredGenerationByTechnologyMw.Wind` | 13 |
| everything else | 15 |

Four of the fifteen series are byte-identical in every point of the sweep. That
is **103 KB per point**, roughly **4.6 MB** across both sweeps once the region
details are counted too.

**The mechanism to fix this already exists and is half-applied.** Base demand is
already externalised under a content hash:

```
series/base-demand-831978c8987c6b9845fcf1aa98a36e34df562355ecb84ef9754e9145718435ad.json
```

and every point carries `dataSeries.demand.baseDemandSeriesPath` pointing at it —
**while also carrying the full inline `baseDemandMw` array**, 53 KB per point.
The externalised copy is written and then duplicated inline anyway.

### Change

Two steps, either useful alone:

1. **Stop writing the inline copy** when `baseDemandSeriesPath` is set. This is
   the cheapest win in this document, and **the consumer is already built for
   it**: `ResolveBaseDemandAsync` in `NEM.Web/Pages/Dispatch.razor` returns
   immediately when `BaseDemandMw` is present and otherwise fetches the
   referenced `RegularSeriesDTO`, caches it per path, and checks its start and
   resolution against the run before splicing it in. That branch is dead code
   today because the producer always writes both. `BaseDemandMw` is already
   nullable; this is a producer-side change with no contract shape change at all.
2. **Extend the same content-addressed externalisation** to any series identical
   across points of a sweep. The hash is the natural identity: write each
   distinct series once under its own hash, and have each point reference it.
   Points that genuinely differ keep their own file, and the sweep's shape
   decides how much is saved rather than a guess.

### Files

- **Step 1: `NEM.CLI/Scenarios/SweepArtifactExport.cs` only.** `DispatchDemandDTO`
  already declares `double[]? BaseDemandMw` alongside `string? BaseDemandSeriesPath`,
  so writing one instead of both is a producer change with no contract shape
  change, no schema bump, and no `NEM.Web` edit.
- Step 2: `NEM.Contracts/DispatchResultsDTO.cs` for whatever reference shape the
  other series need, plus `SweepArtifactExport.cs` and the matching consumer in
  `NEM.Web/Pages/Dispatch.razor`.

### Acceptance

- No point detail contains an interval array that is byte-identical to one in
  another point of the same sweep.
- `NEM.Web/wwwroot/data/sweeps/` is materially under 69 MB, and every sweep page
  and every run opened from one still renders with no invalid-data message.

---

## 4. No content hashing on artifact filenames, so first load can never be cached

**Priority: medium.**

### Evidence

`results.json`, `results-nsw1.json` and the rest are fetched by stable names, so
a browser must revalidate on every visit and can never treat them as immutable.
`NEM.Web/wwwroot/_headers` marks the externalised sweep series immutable and
everything else `no-cache`, which is the correct pair of answers *for these
names* — it is the ceiling, not a fix.

The site caches *parsed* artifacts in memory for a session
(`NEM.Web/Services/ArtifactLoader.cs`), which fixed repeat navigation within a
visit. It cannot fix a first load or a return visit. Only the producer can.

Note the `series/` directory already does this correctly:
`base-demand-831978c8...json` is content-addressed and immutably cacheable. The
pattern is established; it is the top-level artifacts that do not use it.

### Change

Either:

- **A content hash in the filename**, as `series/` already does, with a small
  manifest mapping logical name to hashed name. The site loads the manifest — the
  only thing that must stay `no-cache` — and every artifact behind it becomes
  permanently cacheable.
- **Or an ETag-friendly build** where filenames stay stable but the hosting emits
  strong ETags, which is a smaller change and a smaller win: it saves the
  transfer on a return visit, not the round trip.

The manifest is preferred: it also lets a rerun that changes one region re-fetch
one artifact rather than all of them.

### Files

- `NEM.CLI/Scenarios/DispatchResultsExport.cs` and `SweepArtifactExport.cs`.
- A new manifest DTO in `NEM.Contracts`.
- `NEM.Web/Services/ArtifactLoader.cs` — resolve logical names through the
  manifest.
- `NEM.Web/wwwroot/_headers` — hashed artifacts become `immutable`.

### Acceptance

- A second visit to `/regions` in a fresh session fetches the manifest and
  nothing else it already holds.
- Re-running one region's scenario changes one hashed filename, not all of them.

---

## 5. No path attribution, so an import cannot be traced past one hop

**Priority: low today, rising the moment a third region lands.**

### Evidence

This is the open half of round-one item 2. The declared topology landed and did
the important part: `NEM.Web/Components/Viz/NetworkMap.razor` now draws the graph
the run solved rather than one inferred from whichever links carried something,
and links have stable ids.

What is still missing is per-interval path attribution. With two regions the
difference is invisible — every import crosses exactly one link. With SA1
importing through VIC1 from NSW1, the site can show three separate link flows and
still cannot say South Australia was supplied by New South Wales. The network
caption continues to disclaim routing for exactly this reason, and
`NEM.Web/Services/Insights/SystemAnalysis.cs` has no way to answer it.

### Change

For each region and interval, where the imported energy originated. The solver
knows this; nothing else does.

Worth deciding deliberately: per-interval attribution across five regions is
another set of 8,760-element arrays, which runs straight into items 1 and 3.
**An integrated origin matrix per region-pair over the period** — "of VIC1's
252,796 MWh imported, X came from NSW1 and Y from SA1" — would answer the
question the site actually asks and cost a handful of numbers rather than another
series. Prefer that unless an hourly view of provenance is specifically wanted.

### Files

- `NEM.Contracts/SystemDispatchResultsDTO.cs`, `SystemDispatchOverviewDTO.cs`.
- `NEM.Model/Grid/**` — whatever solves the flows knows the origin.
- `NEM.CLI/Scenarios/DispatchResultsExport.cs`.
- `NEM.Web/Components/Viz/NetworkMap.razor` — drop the routing qualifier from the
  caption once routing exists.
- `NEM.Web/Services/Insights/SystemAnalysis.cs` — `LinkFlow` and the trade finding.

### Acceptance

- A run with three or more regions answers "where did this region's imports come
  from" from the artifact alone.
- The network view's caption no longer disclaims routing.

---

## Not blocked on the producer

Listed so nobody picks them up here by mistake — these are `NEM.Web`'s own work,
tracked on the branch rather than in this brief.

- Chart tooltips are native SVG `<title>`: slow, unstyled, no touch support.
- A scrubbable moving window over the dispatch period, designed but not built.

## Still not supported by any artifact

| View | Blocked by |
| --- | --- |
| Marginal or hourly price | No price series in any artifact |
| Emissions intensity or totals | No emissions factor or series in any artifact |

Both are the obvious next questions of a model answering what electricity costs
under a renewable target. Neither is requested here: they are a modelling layer
before they are a contract change, and the site is not waiting on them.
