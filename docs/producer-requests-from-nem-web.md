# Producer requests from NEM.Web

Work brief for an agent changing `NEM.Contracts`, `NEM.CLI` and `NEM.Model`.

Everything here was found while building the analysis views on branch `nem-075`
(pull request #102). Each item belongs to the artifact producer: `NEM.Web` should
not reconstruct evidence a result is supposed to carry, and every workaround the
site holds is a place it says less than it could, or says something looser than
it should.

> **Superseded for new work.** The remaining items are restated with current
> measurements, file lists and acceptance criteria in
> [`producer-requests-round-2.md`](producer-requests-round-2.md). Start there.
> This document is kept as the record of what round one asked for and what it
> changed.

**Status.** Items 1 to 6 and P1 were delivered by pull request #103 and are
consumed on `nem-075`. What each one unblocked is recorded under
[Delivered](#delivered) so the next reader can see which views exist because of
which contract change. The [open items](#open-items) are P2, P3 and P4, plus the
one part of item 2 that was not closed.

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
4. Every `NEM.Web` consumer. The web validates schema versions on load, so a
   bumped version with an unchanged consumer does not fail quietly: every
   affected page shows "Artifact schema N is not supported".

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

Then run the site and open `/`, `/regions`, `/dispatch`, `/dispatch?region=VIC1`,
`/inputs/weather` and both sweep pages, and confirm none of them shows a schema
or invalid-data message.

**Round-trip contract tests live in**
`NEM.CLI.Tests/Contracts/SystemAndRegionDispatchResultsContractTests.cs`. Extend
that file rather than adding a parallel one.

---

## Open items

### P2. Interval series dominate parse time, and most are unused per view

**Priority: high — the largest remaining cost, and P1 showed the shape of the
fix works.**

Deserialising `results.json` accounted for about 1.7 s of the 1.7 s it cost to
open the comparison page; **network transfer was 14 ms**. A dispatch artifact
carries a dozen 8,760-element arrays and any given view reads a few of them.

`results-overview.json` (item P1) proved the point for the whole-system result:
19 KB against 2.05 MB for the same integrated figures, and `/` and `/regions` now
open on it. Two gaps remain.

- **The regional artifacts have no overview.** `results-nsw1.json` and
  `results-vic1.json` are about 1.6 MB each and carry no integrated totals, so
  `EnergyMix` still integrates their series when a page opens one region. A
  region-level equivalent of `SystemDispatchOverviewDTO` would finish the job.
- **A full-period chart never needs hourly data.** Every full-period chart on
  the site buckets 8,760 points down to a few hundred before drawing
  (`NEM.Web/Services/DispatchWindow.cs`). Publishing a pre-bucketed daily or
  weekly series alongside the hourly one would let a year view load without the
  hourly data at all, and the hourly artifact would be fetched only when a reader
  opens a single day.

`/regions` still fetches `results.json` after its overview, for one thing: the
interconnector series behind the trade table and the two flow plots. Splitting
interval series into their own artifact — the way `baseDemandSeriesPath` already
does for demand — would let it fetch only those.

### P3. Sweep points repeat the whole system artifact per run

**Priority: medium.**

`NEM.Web/wwwroot/data/sweeps/` is 69 MB across 109 files. Each point's detail is
a full dispatch result, so where points share a fleet and differ in one input,
the unchanged series are duplicated per point. The externalised `series/`
directory already exists for base demand; extending it to any series identical
across points would cut this sharply.

**One loose end from #103.** The sweeps now publish a per-point overview
(`points/pN-overview.json`, 20 files, 0.3 MB total) but `SweepIndexPointDTO`
carries only `DetailPath`, so nothing can reach them: the site has no path to
follow and does not know they exist. They are published and unreachable. Adding
an `OverviewPath` alongside `DetailPath` — the same pairing `results.json` and
`results-overview.json` already have — would make them usable; without it they
are 0.3 MB of the 69 MB that nothing can read.

### P4. No content hashing on artifact filenames

**Priority: medium.**

`results.json` is fetched by a stable name, so a browser must revalidate on every
visit and cannot cache it immutably. `NEM.Web/wwwroot/_headers` now marks the
externalised sweep series immutable and everything else `no-cache`, which is the
correct pair of answers for the names as they stand — but it is a ceiling, not a
fix. A content hash in the filename, or an ETag-friendly manifest, would let the
site cache every artifact permanently and re-fetch only what a rerun actually
changed.

The site caches *parsed* artifacts in memory for a session
(`NEM.Web/Services/ArtifactLoader.cs`), which fixes repeat navigation. It cannot
fix first load; only the producer can.

### 2b. Topology is declared, but a path still cannot be traced

**Priority: medium — does not bite until a third region lands.**

Item 2 delivered the declared topology and stable link ids, which is the half the
site needed most: `NEM.Web/Components/Viz/NetworkMap.razor` now draws the graph
the run solved rather than one inferred from whichever links carried something,
and an idle link is drawn idle rather than being absent.

What is still missing is **per-interval path attribution**: for each region and
interval, where the imported energy originated. With two regions the difference
is invisible. With SA1 importing through VIC1 from NSW1, the site can show three
separate link flows and still cannot say South Australia was supplied by New
South Wales. The network caption continues to disclaim routing for that reason.

**Acceptance:** a run with three or more regions can answer "where did this
region's imports come from" from the artifact alone, and the caption drops its
routing qualifier.

---

## Delivered

Recorded so the next reader can see which view exists because of which contract
change. All of these landed in pull request #103 and are consumed on `nem-075`.

| # | Was | Now | What it unblocked on the site |
| --- | --- | --- | --- |
| 1 | `WeatherBasisDTO` recorded one site; NSW1 uses Dubbo for solar and Armidale for wind, ~300 km apart | `WeatherSiteDTO` per role | `/dispatch` names both sites, and matches `/inputs/weather` for the same region. The caveat paragraph in `WeatherBasisNote.razor` is gone, and so is the note on the weather page saying a dispatch result would not mention the wind site |
| 2 | No topology; links keyed by the display string `"{From} to {To}"` | `DispatchTopologyDTO` plus a stable `Id` per link | The network is the graph the run declares, not the links that happened to carry energy. An idle link is drawn idle; a link whose series have not loaded yet is distinguishable from one that carried nothing. Partly open — see 2b |
| 3 | Regional `transmissionLossesMw` was zero at all 8,760 intervals while the system reported 13.3 GWh lost | Each link's losses allocated to the receiving region | A region's dispatch page states the losses it received — 13,305 MWh in VIC1, reconciling to the system figure — with the note saying it is a reporting allocation rather than a charge |
| 4 | `DispatchCostDTO` split three ways, never by technology | `GenerationCostContributions`, reconciling exactly | The cost pages answer what is driving the levelised cost. Coal is 55.4% of the system's generation bill for 49.7% of its energy; NSW1 gas costs $1,491/MWh of the energy it delivers against coal's $168 — both invisible in a three-way split |
| 5 | `transmissionSlcotAudPerMwh` was 0 against a real $164,919.70 | `TransmissionCostStatus`, and four decimals on small values | "$0.00/MWh" is now either `$0.0015/MWh` or "not modelled". Regional scopes declare it not modelled rather than drawing a zero, and a sweep whose every run declares it out of scope says so instead of showing a flat line labelled "no change" |
| 6 | `StorageSizingOutcomeDTO` gave the outcome, not the passes | `Trajectory` per pass | Unserved energy against storage capacity, which is the shape that says whether the target was expensive to reach. The run's own search cost 3,529 MWh of storage to remove 2,788 MWh of unserved energy, and one of its three passes re-ran a capacity already reached — a probe the search did not accept, which the view distinguishes from a step it took |
| P1 | `/regions` downloaded 5.3 MB to compute ten numbers | Integrated generation totals on `RegionDispatchSummaryDTO`, plus `results-overview.json` | `/` and `/regions` open on a 19 KB artifact instead of 2.05 MB and 5.27 MB. Both are complete on first paint; the progressive mix placeholder is gone. `/regions` fetches `results.json` afterwards for the interconnector series alone, and derives each region's trade from those rather than from a 1.6 MB artifact per region |

---

## Views the current schema cannot support

Listed so they are not silently dropped rather than as requests.

| View | Blocked by |
| --- | --- |
| Which regions an import travelled through | No path attribution (item 2b) |
| Marginal or hourly price | No price series in any artifact |
| Emissions intensity or totals | No emissions factor or series in any artifact |

The last two are noted because they are the obvious next questions of a model
answering what electricity costs under a renewable target, not because anything
on the site is waiting for them.
