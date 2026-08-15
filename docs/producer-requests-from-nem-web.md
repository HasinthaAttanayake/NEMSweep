# Producer requests from NEM.Web

Work brief for an agent changing `NEM.Contracts`, `NEM.CLI` and `NEM.Model`.

Everything here was found while building the analysis views on branch `nem-075`
(pull request #102). None of it is fixed there: each item belongs to the artifact
producer, and `NEM.Web` should not reconstruct evidence a result is supposed to
carry. The site currently works around each one, and every workaround is a place
the site says less than it could, or says something looser than it should.

Items are ordered by what they cost today. Take them one at a time; each is a
self-contained change with its own acceptance criteria.

---

## Before starting

**Read first.** `.github/instructions/contracts.instructions.md` and
`.github/instructions/cli-data-pipeline.instructions.md` govern this work. The
rule that shapes every item below:

> For a breaking shape or meaning change, increment the artifact schema version
> and update the CLI producer, every web consumer, and the relevant round-trip
> contract test in the same change.

**So each item is four edits, not one:**

1. The DTO in `NEM.Contracts`.
2. The version constant in `NEM.Contracts/ArtifactSchemaVersions.cs`.
3. The producer in `NEM.CLI` (usually `Scenarios/DispatchResultsExport.cs`).
4. Every `NEM.Web` consumer — listed per item below. The web validates schema
   versions on load, so a bumped version with an unchanged consumer does not
   fail quietly: every affected page shows "Artifact schema N is not supported".

**Regenerate artifacts, never hand-edit them.**

```bash
dotnet run --project ./NEM.CLI/NEM.CLI.csproj -- --run-scenario
dotnet run --project ./NEM.CLI/NEM.CLI.csproj -- --run-sweep ./sweeps/datacentre-nameplate-nsw1-fy2026.json
dotnet run --project ./NEM.CLI/NEM.CLI.csproj -- --run-sweep ./sweeps/renewable-penetration-nsw1-fy2026.json
```

**Verify.**

```bash
dotnet build ./NemSim.slnx
dotnet test ./NemSim.slnx
dotnet run --project ./NEM.Web/NEM.Web.csproj
```

Then open `/`, `/regions`, `/dispatch`, `/dispatch?region=VIC1` and both sweep
pages, and confirm none of them shows a schema or invalid-data message.

**Round-trip contract tests live in**
`NEM.CLI.Tests/Contracts/SystemAndRegionDispatchResultsContractTests.cs`. Extend
that file rather than adding a parallel one.

**Do not change `NEM.Web` beyond what a contract change forces.** The site's
analysis layer, plots and pages are settled; the required edits are mechanical
(reading a new field, or reading two where there was one).

---

## 1. A region records one weather site but uses two

**Priority: high — the site currently states something untrue.**

### Evidence

`WeatherBasisDTO` in `NEM.Contracts/ModelFactsDTO.cs` records a single site:

```csharp
public sealed record WeatherBasisDTO(
    WeatherBasisKind Kind, string SourceFile, string LocationName, string Description);
```

`WeatherDataDTO` (schema 6) records one **per role**, and says in its own remarks
that this is deliberate:

> Source ownership is intentionally recorded on each role because solar and wind
> traces may be assembled from different EPW files and locations.

The published data already uses two:

| Region | Solar site | Wind site |
| --- | --- | --- |
| NSW1 | Dubbo City Rgnl AP (WMO 957190) | **Armidale AP (WMO 957730)** |
| VIC1 | Ballarat AP (WMO 948520) | Ballarat AP (WMO 948520) |

Dubbo and Armidale are roughly 300 km apart. In `results.json`,
`dataSourcesByRegion.NSW1.weatherBasis.sourceFile` names only the Dubbo file, and
the emitted `Description` reads "Typical meteorological year from
AUS_NSW_Dubbo…epw … applied to the dispatch period by calendar hour" — untrue of
the wind trace, which drives 11.4% of NSW1's delivered energy. VIC1 is lossless
only by coincidence.

The same collapse reaches `SweepScopeDTO.WeatherBasis`.

### Change

```csharp
public sealed record WeatherBasisDTO(
    WeatherBasisKind Kind,
    WeatherSiteDTO Solar,
    WeatherSiteDTO Wind,
    string Description);

public sealed record WeatherSiteDTO(string SourceFile, string LocationName);
```

A role-keyed collection would work equally well and would extend to a third
trace later. Either shape is fine; what matters is that one region can state more
than one site.

The emitted `Description` must stop naming a single site as the region's basis.
Where the two roles share a site, saying so once is correct; where they differ,
it needs to name both.

### Files

- `NEM.Contracts/ModelFactsDTO.cs` — the DTO.
- `NEM.Contracts/ArtifactSchemaVersions.cs` — bump `DispatchResults`,
  `SystemDispatchResults`, `RegionDispatchResults`, `SweepIndex`.
- `NEM.CLI/Weather/WeatherBasis.cs` — construction.
- `NEM.CLI/Scenarios/DispatchResultsExport.cs`, `ScenarioRunner.cs`,
  `SweepArtifactExport.cs` — emission.
- `NEM.Web/Components/WeatherBasisNote.razor` — currently shows one site and
  carries a caveat paragraph pointing at `/inputs/weather`. **Delete the caveat**
  once both sites are carried; it exists only because this gap does.
- `NEM.Web/Pages/Dispatch.razor`, `Regions.razor`, `Sweep.razor` — call sites.
- `NEM.Web/Services/SweepIndexLoader.cs` — `IsUsableScope` validates
  `WeatherBasis.SourceFile` and `LocationName`; update to the new shape.

### Acceptance

- `/dispatch` for NSW1 names both Dubbo and Armidale.
- `/inputs/weather` and `/dispatch` state the same sites for the same region.
- The caveat paragraph in `WeatherBasisNote.razor` is gone.

---

## 2. No network topology, so no import can be traced

**Priority: high — blocks a view the model exists to support, and gets worse
with every region added.**

### Evidence

`DispatchInterconnectorDTO` in `NEM.Contracts/DispatchResultsDTO.cs`:

```csharp
public sealed record DispatchInterconnectorDTO(
    string FromRegionId, string ToRegionId, double CapacityMw,
    double[] FlowMw, double[] LossesMw);
```

A pairwise directed edge. There is no node identity beyond a region id, no path,
and no attribution of which regions an import crossed.

The site's network view (`NEM.Web/Components/Viz/NetworkMap.razor`) draws who is
joined to whom and which way each joint ran, and its caption explicitly says it
does not draw a route — because the artifact does not record one.

A two-region run cannot expose the difference. A five-region one will: with SA1
importing through VIC1 from NSW1, the site can show three separate link flows and
cannot say South Australia was supplied by New South Wales.

### Change

Either of:

- **Per-interval path attribution** — for each region and interval, where the
  imported energy originated. Richest, and the only thing that answers the
  question directly.
- **A declared topology** — nodes and the links joining them, emitted once per
  run. Cheaper, and at minimum stops the site inferring the graph from whichever
  links happen to appear in the array.

Also worth carrying on the interconnector itself: a stable link identifier. The
site currently keys links by `"{From} to {To}"`, which is a display string doing
an identity job.

### Files

- `NEM.Contracts/SystemDispatchResultsDTO.cs`, `DispatchResultsDTO.cs`.
- `NEM.Model/Grid/**` — whatever solves the flows knows the topology.
- `NEM.CLI/Scenarios/DispatchResultsExport.cs`.
- `NEM.Web/Components/Viz/NetworkMap.razor` — remove the "no route" caption
  qualifier once routing exists.
- `NEM.Web/Services/Insights/SystemAnalysis.cs` — `LinkFlow`.
- `NEM.Web/Services/DispatchArtifactValidator.cs` — validates interconnector
  series alignment.

### Acceptance

- A run with three or more regions can answer "where did this region's imports
  come from" from the artifact alone.
- The network view's caption no longer disclaims routing.

---

## 3. Regional transmission losses are all zero

**Priority: medium.**

### Evidence

`results-nsw1.json` and `results-vic1.json` both carry `transmissionLossesMw`
that is zero at every one of 8,760 intervals. The system artifact reports 13.3
GWh lost on the NSW1→VIC1 link over the same period.

Losses exist but are not attributed to a region, so a regional page cannot show
them and a regional cost cannot include them. `NEM.Web/Pages/Dispatch.razor`
carries a prose note saying regional costs "exclude system transmission and do
not price transferred energy, so they can understate imported supply costs" —
that note exists because of this gap.

### Change

Attribute interconnector losses to regions, or state explicitly in the contract
that the regional series is not populated so the site can say so rather than
drawing a flat zero.

### Files

- `NEM.Model/Grid/**`, `NEM.CLI/Scenarios/DispatchResultsExport.cs`.
- `NEM.Web/Pages/Dispatch.razor` — the regional cost note.

### Acceptance

- Either regional `transmissionLossesMw` carries real values, or the contract
  documents that it does not and the site stops implying zero means zero.

---

## 4. Costs are published per region but not per technology

**Priority: medium — limits the site's central question.**

### Evidence

`DispatchCostDTO` splits three ways: generation, storage, transmission. Nothing
says what the coal fleet cost against the wind fleet.

The site can decompose *energy* by technology (it integrates
`deliveredGenerationByTechnologyMw`) but not *cost*, so the cost pages answer
"which of three buckets did this fall in" rather than "what is driving the
levelised cost". Given the project's stated research question — what electricity
costs under an 82% renewable target — that is the decomposition a reader wants.

### Change

Add annualised cost and levelised contribution per technology or per fleet,
alongside the existing three-way split.

### Files

- `NEM.Contracts/DispatchResultsDTO.cs` — `DispatchCostDTO`.
- `NEM.Model/Economics/PowerSystemCostCalculator.cs`.
- `NEM.CLI/Scenarios/DispatchResultsExport.cs`.

### Acceptance

- A dispatch result states cost per technology, and the shares sum to the
  published generation cost.

---

## 5. Transmission priced at zero reads as a defect

**Priority: low.**

### Evidence

In `results.json`: `annualisedTransmissionCostAud` is `164919.7` while
`transmissionSlcotAudPerMwh` is `0`. The site shows "$0.00/MWh" faithfully, which
reads as broken rather than as a real rounding of a genuinely small number.

### Change

Either more precision on the levelised figure, or a status flag distinguishing
"not priced in this run" from "priced, and small". `DispatchCostDTO.Status`
already exists for the whole cost block; the same idea per component would do.

### Acceptance

- A reader can tell a zero that means "not modelled" from a zero that means
  "less than half a cent".

---

## 6. Sizing evidence stops at the outcome

**Priority: low — enables a new view rather than fixing a wrong one.**

### Evidence

`StorageSizingOutcomeDTO` gives initial and final capacity, the ceiling and the
pass count, but not the path: what each pass tried and what unserved energy it
achieved.

The site can say storage grew from 3,243 MWh to 6,772 MWh over three passes. It
cannot draw reliability against capacity, which is the shape that says whether
the target was expensive or nearly free to reach — and that shape is the argument
for or against a storage build.

### Change

Carry the sizing trajectory: per pass, the capacity tried and the unserved energy
and hours it produced.

### Files

- `NEM.Contracts/ModelFactsDTO.cs` — `StorageSizingOutcomeDTO`.
- `NEM.Model/StorageSizing/**`, `NEM.CLI/Scenarios/DispatchResultsExport.cs`.

### Acceptance

- A run publishes enough to plot unserved energy against storage capacity across
  the sizing passes.

---

## Performance: producer changes that would make the site materially faster

These are measured costs the site pays today, not estimates.

### P1. The comparison page downloads 5.3 MB to compute ten numbers

**This is the single highest-value change on this document.**

`/regions` fetches `results.json` (2.05 MB) plus a region detail per region (1.58
MB and 1.59 MB) — 5.27 MB measured. The only thing the regional artifacts are
needed for is each region's **generation mix**: five totals per region. Every
other figure on that page comes from `regionSummariesById`, which the system
artifact already carries.

**Change:** add integrated generation-by-technology totals to
`RegionDispatchSummaryDTO` (`NEM.Contracts/SystemDispatchResultsDTO.cs`).

**Effect:** removes 3.2 MB and roughly three seconds of parsing from that page's
first load, and removes the progressive-loading placeholder the site currently
needs (`NEM.Web/Pages/Regions.razor`, `.scorecard-mix`).

**Files:** `SystemDispatchResultsDTO.cs`, `NEM.CLI/Scenarios/DispatchResultsExport.cs`,
`NEM.Web/Services/Insights/EnergyMix.cs` and `SystemAnalysis.cs`.

### P2. Interval series dominate parse time and most are unused per view

Deserialising `results.json` accounted for about 1.7 s of the 1.7 s it cost to
open the comparison page; **network transfer was 14 ms**. A dispatch artifact
carries a dozen 8,760-element arrays and any given view reads a few of them.

Either helps:

- **Split interval series into their own artifact per run**, referenced from the
  summary, the way `baseDemandSeriesPath` already does for demand. Pages needing
  only integrated figures would never fetch them.
- **Publish a pre-bucketed daily or weekly series** alongside the hourly one.
  Every full-period chart on the site buckets 8,760 points down to a few hundred
  before drawing (`NEM.Web/Services/DispatchWindow.cs`). Doing that once in the
  producer would let a year view load without hourly data at all, and the hourly
  artifact would be fetched only when a reader opens a single day.

### P3. Sweep points repeat the whole system artifact per run

`NEM.Web/wwwroot/data/sweeps/` is 66 MB across 89 files. Each point's detail is a
full dispatch result, so where points share a fleet and differ in one input, the
unchanged series are duplicated per point. The externalised `series/` directory
already exists for base demand — extending it to any series identical across
points would cut this sharply.

### P4. No content hashing on artifact filenames

`results.json` is fetched by a stable name, so a browser must revalidate on every
visit and cannot cache it immutably. A content hash in the filename, or an
ETag-friendly manifest, would let the site cache artifacts permanently and
re-fetch only what a rerun actually changed.

The site now caches *parsed* artifacts in memory for a session
(`NEM.Web/Services/ArtifactLoader.cs`), which fixed repeat navigation. It cannot
fix first load; only the producer can.

---

## Views the current schema cannot support

Listed so they are not silently dropped rather than as requests.

| View | Blocked by |
| --- | --- |
| Which regions an import travelled through | No path or topology (item 2) |
| Regional transmission cost or losses | System-level only; zero per region (item 3) |
| Cost per technology | `DispatchCostDTO` splits three ways (item 4) |
| Reliability against storage capacity within a run | Sizing publishes outcome, not passes (item 6) |
| Weather per role on a dispatch page | `WeatherBasisDTO` holds one site (item 1) |
| Marginal or hourly price | No price series in any artifact |
| Emissions intensity or totals | No emissions factor or series in any artifact |

The last two are noted because they are the obvious next questions of a model
answering what electricity costs under a renewable target, not because anything
on the site is waiting for them.
