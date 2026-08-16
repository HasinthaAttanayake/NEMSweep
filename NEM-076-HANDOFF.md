# NEM-076 handoff: 5-region FY2026 baseline + data-centre sweep

**Status as of this note:** working tree has substantial uncommitted work (about
to be committed as a single `wip` commit — see bottom of this file for the exact
commit). Branch `nem-076`, PR [#109](https://github.com/HasinthaAttanayake/NemSim/pull/109)
("NEM-076: cleanup wip"), currently open with an empty description.

**Read this whole file before touching anything.** It supersedes any summary in
the PR description or commit message — those are necessarily compressed. If
anything here conflicts with what you observe in the repo, trust the repo and
update this file.

## The original brief

1. Remove **all** legacy published results (NSW1/VIC1-only FY2026 modelling)
   and replace with a new baseline covering all 5 NEM regions (NSW1, QLD1, SA1,
   TAS1, VIC1), from a scenario config the user supplied
   (`scenarios/nem-fy2026-all-regions.json`).
2. Design and build a new data-centre nameplate sweep, replacing the old
   NSW1-only one. Design agreed with the user: the sweep's national aggregate
   axis is anchored on real committed grid-connection capacity per state (NSW1
   3,200 / VIC1 2,200 / QLD1 1,000 / SA1 800 / TAS1 300 MW, summing to 7,500 MW
   at the anchor point), scaling all 5 regions proportionally away from that
   anchor, stepped in nominal 500 MW national increments from 0 to 12,000 MW
   (25 points). No per-region ceiling clamp — confirmed with the user.
3. Drop the renewable-penetration sweep entirely (results are being
   simplified) — **done**, no remaining work here.
4. Everything lands as commits on `nem-076`.

## What's actually done (verified — build is clean, 828 tests pass)

Run `dotnet test NemSim.slnx` to reverify. As of this note: 447 NEM.Model.Tests
+ 230 NEM.Web.Tests + 151 NEM.CLI.Tests, all green.

### Code changes (all with regression tests)

- **Scenario schema gate bumped 3→4** — `NEM.CLI/Configuration/ScenarioConfig.cs:38`
  now accepts `schemaVersion: 4`, matching the interconnector cost model already
  shipped in commit `2aeb7f9` (distance-based `capitalCostAudPerKmPerMw` /
  `fixedOperatingCostAudPerKmPerMwYear` instead of flat per-MW costs). This is
  why the old scenario files were deleted mid-"cleanup wip" before this
  session started — they declared schema 3 but used incompatible fields.
- **SA1 weather timezone support** — `NEM.CLI/Weather/EpwParser.cs`. South
  Australia's TMYx weather station (Port Augusta) reports its LOCATION
  timezone as 9.5 (ACST), not 10 (AEST/NEM market time) like every other NEM
  region. Added `ShiftToNemTime` (half-hour linear interpolation, wraps at
  year boundary) applied to the 5 raw hourly traces (GHI/DNI/DHI, dry bulb,
  wind speed) for any station not already at TZ 10; solar zenith is
  unaffected since it's computed astronomically from the NEM-time grid, not
  from the file. Validation now accepts TimeZone 10 or 9.5 only — still
  rejects arbitrary offsets (regression test for TZ=8 still passes).
- **Negative operational demand floored to zero** —
  `NEM.CLI/Demand/OperationalDemandParser.cs`. SA1 genuinely records negative
  operational demand at times (rooftop solar exceeding underlying demand);
  the parser used to hard-reject any negative value. Now floors to 0 MW
  rather than throwing. The domain model has no concept of negative demand
  and enforces non-negative demand throughout (`DemandComponent`,
  `DispatchOutcome`), so this was the minimal correct fix, confirmed with the
  user before implementing.
- **Interconnector capacity tolerance bug (real solver bug, found by this
  work)** — `NEM.Model/Simulation/SystemDispatchOutcome.cs`,
  `ValidateInterconnectorFlows`. The capacity upper-bound check
  (`flow.Flow[index] > link.Capacity`) had **no floating-point tolerance**,
  unlike the otherwise-identical check in
  `NEM.Model/StorageSizing/StorageSizingRunResult.cs:194-195` which already
  had `+ FlowTolerance`. This only ever mattered once a link's flow actually
  saturated its capacity, which never happened in the old 2-region
  single-link topology. In the 5-region mesh, `QLD1->NSW1` saturates its
  1,610 MW capacity and the accumulated floating-point drift (flow =
  1610.0000000000002) tripped the un-tolerant check. Fixed to match the
  `StorageSizingRunResult` pattern (`+ BalanceTolerance`). Regression test:
  `Create_AcceptsSolverFlowWithinFloatingPointToleranceOfCapacity` in
  `NEM.Model.Tests/Simulation/SystemDispatchOutcomeTests.cs`.
- **Loss-ledger validator tolerance too tight for multi-link systems** —
  `NEM.Web/Services/DispatchArtifactValidator.cs`. Published `*Mw` fields are
  independently rounded to 1 decimal place at export time
  (`NEM.CLI/Infrastructure/JsonFile.cs:137-143`, deliberate, pre-existing,
  do not change). Summing several links' independently-rounded losses can
  legitimately drift from the system total's own rounding by more than a
  naive epsilon — worst case ~0.05 MW per term. The check comparing system
  `TransmissionLossesMw` against the sum of per-link `LossesMw` used the same
  `1e-9` tolerance as genuine correctness checks elsewhere in the file. Fixed
  by scaling the tolerance with link count: `0.05 * (links + 1) +
  FlowToleranceMw`. This is a **display-rounding accommodation, not a
  loosened correctness check** — real mismatches (tested: mismatch of 1.5 MW
  on a single-link system) still fail.
- **Sweep provenance crash on any multi-region sweep (real bug, found by this
  work, the one explicitly authorized to fix)** —
  `NEM.CLI/Scenarios/SweepArtifactExport.cs:CreateProvenance`. Used to read
  `settings.DemandFile` / `settings.WeatherFile`, legacy single-region
  convenience properties on `ScenarioSettings`
  (`NEM.CLI/Configuration/ScenarioConfig.cs:277-283`, literally
  `Regions.Single().DemandFile`) that throw `InvalidOperationException:
  Sequence contains more than one element` for any config with more than one
  region. This ran once, **after every sweep point had already executed**,
  outside any point's try/catch, so the whole `--run-sweep` command exited
  non-zero and `sweeps/{id}/index.json` was never written — even though every
  individual point's own artifacts were fine. Fixed to iterate
  `settings.Regions` and add a demand/weather provenance entry per region.
  Regression test: `CreateProvenance_ListsDemandAndWeatherInputsForEveryRegion`
  in `NEM.CLI.Tests/Scenarios/SweepRunTests.cs` (also added a
  `WriteTwoRegionBaseline()` fixture helper there, reusable for other
  multi-region sweep tests).

### Data / config changes

- `NEM.CLI/data/nemsim-inputs/manifest.json` — regions list expanded to all 5
  (this directory is gitignored, local-only, not part of the commit).
- `scenarios/nem-fy2026-all-regions.json` — the user-supplied 5-region
  baseline, landed verbatim except `storageSizing.maximumPasses` raised
  `256 → 512` (see "p7 mystery" below for why).
- `scenarios/nem-fy2026-nsw1-vic1.json` — deleted (superseded).
- `NEM.CLI/appsettings.local.json` / `appsettings.example.json` —
  `defaultScenarioPath` repointed to the new baseline.
- Ingested and republished: `NEM.Web/wwwroot/data/{demand,weather}-{nsw1,
  qld1,sa1,tas1,vic1}.json`, `generation-information.json`, and the full
  `results*.json` set for all 5 regions (via `NEM.CLI --run-scenario`).
- `sweeps/datacentre-nameplate-nsw1-fy2026.json` and the old
  `NEM.Web/wwwroot/data/sweeps/{renewable-penetration-nsw1-fy2026,
  datacentre-nameplate-nsw1-fy2026}/` trees — deleted.
- `sweeps/datacentre-nameplate-fy2026.json` — the new sweep definition, 25
  points (`p0`..`p24`), generated programmatically from the anchor table
  agreed with the user (see brief above). `baselineConfigPath` points at the
  new scenario. Each point's `overrides.regions` lists only regions with a
  nonzero value at that point.

## What's NOT done — in priority order

### 1. The sweep has never completed a clean run. Rerun it.

The provenance bug (fixed above) was only discovered *after* a full 25-point
sweep run had already executed and then crashed at the very end. That means:

- `NEM.Web/wwwroot/data/sweeps/datacentre-nameplate-fy2026/` currently has
  `configs/`, `points/`, `series/` populated for whichever points succeeded in
  that run, but **no `index.json`** for the sweep and **no
  `NEM.Web/wwwroot/data/sweeps/index.json` manifest** — the crash happened
  before either was written. `/sweeps` will not show this sweep at all right
  now.
- The provenance fix means a rerun should complete cleanly to the end (all 25
  points will get an index entry, whether they individually succeeded or
  failed).

**Action:** run, in order:

```bash
dotnet run --project NEM.CLI -- --fan-out-sweep sweeps/datacentre-nameplate-fy2026.json
dotnet run --project NEM.CLI -- --run-sweep sweeps/datacentre-nameplate-fy2026.json
```

This will take a long time — 25 points, each a full 8760-hour, 5-region,
interconnected storage-sizing search. The last observed run took well over 10
minutes; run it in the background and wait for the notification rather than
polling. **Do not `tail` or truncate the output** — capture the full log, it's
the only record of which points succeeded/failed and why.

Expected outcome, based on the last (crashed) run at `maximumPasses: 512`:
p0–p6 and p8 succeed; p9–p24 fail `storageNoLongerImprovesReliability` for
NSW1 (unserved energy climbing from ~25,000 MWh at p9 to ~11.2M MWh at p24 —
**this is a legitimate model finding, not a bug**: past a point NSW1 is short
on firm capacity, not storage duration, so a bigger battery can't help. Do
not try to "fix" this by raising battery caps or loosening constraints — the
user was explicit about this). p7 is the one genuine open question (below).

**After a clean run, re-verify in the browser** (`/dispatch` and `/sweeps`
pages) per the original plan's verification section — this was never done in
this session because the sweep never finished.

### 2. p7 (`+3,500 MW`, nominal) hits `passLimitReached` — unexplained

Even at `maximumPasses: 512` (raised from the baseline's original 256), p7
fails with `PassLimitReached` specifically during the "compliant frontier
refinement" phase — i.e. the growth phase *already met* the reliability
target, it just couldn't finish trimming the battery to 1 MW/1 MWh precision
within budget. What makes this a real mystery rather than "just needs a
bigger budget": **p6 (3,000 MW) converges in 151 passes and p8 (4,000 MW)
converges in 179 passes — both comfortably under 512 — but p7, sitting
between them, does not converge even at 512.** This is not a smooth function
of load.

A diagnostic was started (p7's config with `maximumPasses` temporarily set to
4,000, run standalone via `--run-scenario` against a scratch copy) but was
**aborted before completion** to unblock other work — it never finished, so
we don't know if p7 converges given enough passes, or if something about that
specific load level causes the refinement search to cycle indefinitely
(oscillating between two candidates, for instance). Worth checking: dump
`storageSizing.trajectory` from a rerun and look for the pass sequence
oscillating between the same 2-3 `(energyCapacityMwh, powerCapacityMw)` pairs
repeatedly near the end (p6's own trajectory, still on disk at
`NEM.Web/wwwroot/data/sweeps/datacentre-nameplate-fy2026/points/p6-overview.json`
if not yet overwritten by the rerun, shows what *healthy* late-stage
refinement oscillation looks like for comparison — it does wobble between a
couple of nearby candidates near the end too, so "some oscillation" is
normal; the question is whether p7's is bounded or truly non-terminating).

If it's a genuine non-termination bug in `NEM.Model/StorageSizing/StorageSizingSearch.cs`'s
refinement loop, that's a real fix, most likely in the multi-region refinement
ordering. If it's just "needs a much bigger pass budget than 512 for this one
point," that's a data point worth having (and might argue for a
per-point-count-aware budget, or just accepting that point as a documented
gap). Don't guess — finish the diagnostic run first.

### 3. Sizing-stage "failures" discard their dispatch detail — design agreed, not implemented

The user was explicit: `storageNoLongerImprovesReliability` (and by the same
logic `passLimitReached`, `batteryCapacityLimitReached`) results **are
valuable and should be viewable on the platform**, not just recorded as a
free-text failure message. Full investigation already done; here is the
exact, ready-to-implement fix:

**Root cause:** `NEM.CLI/Scenarios/ScenarioRunner.cs`, private method `Size()`
(around line 206-230):

```csharp
return result.Status is StorageSizingStatus.TargetMet or StorageSizingStatus.EnergyLimited
    ? result
    : throw new ScenarioRunException(
        SweepFailureStage.Sizing,
        JsonNamingPolicy.CamelCase.ConvertName(result.Status.ToString()),
        $"Storage sizing ended with {result.Status}: {result.TerminationEvidence}");
```

This throws for any `StorageSizingStatus` other than `TargetMet` or
`EnergyLimited` — i.e. `StorageNoLongerImprovesReliability`,
`BatteryCapacityLimitReached`, `PassLimitReached`. Because this throw happens
*before* `DispatchResultsExport.WritePublication` ever runs (both for the
standalone `--run-scenario` command and for each sweep point, which shares
this same code path via `ScenarioRunner.RunForPublication`), **no output
files are ever written** for these statuses today — it's not that
`SweepRunCommand` deletes them after the fact (its cleanup `File.Delete`
calls in the catch block are actually no-ops for this failure mode, since the
files were never created). This means the fix has to happen here, at the
source — there is no shallower place to intervene.

**Why this is safe to change:** `StorageSizingRunResult` is validated at
construction to be internally consistent *regardless of status* (see
`docs/domain-model.md`, "StorageSizingRunResult validates that its regional
results correspond exactly to final PowerSystem regions..."). Every one of
these statuses represents "the search stopped and here is a fully valid,
dispatchable final candidate," not "no candidate exists." The
`EnergyLimited` status is already treated this way (passed through, not
thrown) — this is extending existing, already-proven precedent to the other
three statuses, not inventing new behavior. The export layer
(`DispatchResultsExport.cs`, `StorageSizingOutcomeDTO.OutcomeFor`) already
exhaustively switches over all 5 `StorageSizingStatus` values and has no
special-casing to remove. **The web UI already fully supports this** —
`NEM.Web/Components/SweepRunTable.razor` already renders
`point.Reliability.WithinTarget` as a "Within"/"Outside" badge and has a
complete `SizingLabel` switch over every `StorageSizingOutcome` value
including `StorageNoLongerImprovesReliability`, `BatteryCapacityLimitReached`,
`PassLimitReached` — it was evidently built anticipating this, but never
got exercised because the backend always threw first. **No NEM.Contracts or
NEM.Web changes should be needed at all** — verify this assumption before
writing new UI code.

**Recommended fix:**

```csharp
return result;
```

i.e. remove the status check entirely — `Size()` never throws on status, it
just returns whatever the search produced. (`JsonNamingPolicy` import in this
file will become unused — remove it too, check nothing else in the file still
needs `using System.Text.Json;`.)

**Two things to decide/verify before landing this, not yet done:**

- **Standalone `--run-scenario` UX.** Today, a baseline that doesn't meet its
  reliability target fails loudly (non-zero exit, thrown exception). After
  this change it will silently succeed (exit 0), with non-compliance visible
  only in the published JSON's `reliability.withinTarget: false`. Recommend
  adding a console warning line in `ScenarioCommand.RunPublication` (both the
  0-arg/1-arg `--run-scenario` path and — check whether it's worth doing for
  — the 4-arg sweep-point path too, though for sweep points the *index* is
  the source of truth, not console output) when `!withinTarget`, so a human
  running it directly still gets a visible signal without it being a hard
  failure. Not implemented — needs a decision on exact wording/placement.
- **No existing test currently exercises the `Size()` throw path directly**
  (confirmed by search — only DTO/JSON round-trip tests reference these
  status names, nothing integration-level). Before landing, add a test that
  drives a scenario to a non-`TargetMet`/non-`EnergyLimited` status and
  asserts it now **succeeds** and publishes with `reliability.withinTarget ==
  false`, using the same fast-converging-fixture pattern already established
  in `NEM.CLI.Tests/Scenarios/ScenarioRunnerMultiRegionTests.cs`'s
  `RunnerFixture` (note: that fixture already sets `maximumPasses: 4` for
  exactly this kind of fast-termination test — reuse it, don't rebuild it).

Once this lands, rerun the sweep (step 1 above, again) and confirm p9–p24 show
up as succeeded-but-outside-target points on `/sweeps` with full dispatch
detail, not as bare failure strings.

### 4. Run timing / performance metrics — discussed, not started

User's ask: "ensure each run is timed so we have performance metrics for the
future." Not implemented at all — this is a fresh design, not a bug fix.
Rough plan discussed with the user (confirm before building further, this
wasn't explicitly signed off the way the sizing-failure fix was):

- Wrap each sweep point's dispatch+sizing execution in a `Stopwatch` inside
  `NEM.CLI/Scenarios/SweepRunCommand.cs`'s per-point loop; add a
  `DurationMs` (or similar) field to `SweepIndexPointDTO` in
  `NEM.Contracts/SweepIndexDTO.cs`.
- Add a total run duration to `SweepProvenanceDTO` (same file) — it already
  carries other "how this artifact was produced" metadata (git SHA, dirty
  flag), a total duration fits the same slot conceptually.
- This is a **breaking additive change to a published JSON contract** — the
  codebase is disciplined about bumping `ArtifactSchemaVersions.SweepIndex`
  (`NEM.Contracts/ArtifactSchemaVersions.cs`, currently `8`) for exactly this
  kind of change; bump it to `9` and check
  `NEM.CLI.Tests/Scenarios/SweepIndexContractTests.cs` for any pinned-version
  assertions that need updating, same pattern as the schema-3→4 migration
  done in this session.
- Whether the standalone `--run-scenario` / `DispatchResultsDTO` also needs a
  duration field wasn't settled — the user's framing was specifically about
  sweep points ("performance metrics" came up while discussing per-point
  pass counts), but ask if unclear rather than assuming either way.

### 5. Branch divergence with `origin/nem-076` — do not push until resolved

`git status -sb` shows `nem-076...origin/nem-076 [ahead 2, behind 1]` — the
local branch and the remote have each gained a commit the other doesn't have,
both titled `NEM-076: cleanup wip` but with **different diffs** (local
deletes `scenarios/nem-fy2026-nsw1-vic1.json`; remote's version of that same
commit message deletes a different pair of files). This predates this
session — flagged early on, never resolved, and still unresolved. **Do not
force-push, and do not merge/rebase without checking with the user first** —
this needs a human decision about which "cleanup wip" commit's intent should
win, since they're not simply fast-forwardable. Surface this before any push.

### 6. Commit history needs proper chunking

Everything in this session (through the point this handoff was written) is
about to land as a **single `wip` commit** — that was an explicit, deliberate
choice for this handoff, not the final state. The original plan's step 9
called for reviewable, logically-separated commits (schema-gate code change;
ingest manifest + generated input data; new baseline scenario + settings;
legacy-results removal; new sweep definition + generated sweep artifacts).
Once the remaining work above is done and the sweep has a clean successful
run, this history should be reorganized into that shape before the PR is
considered ready — check with the user on whether to `reset --soft` and
re-commit in chunks, or leave the wip commit and add clean follow-up commits
on top (simpler, less risky, probably preferable given the PR is already
public).

## Quick-start for the next session

```bash
# Confirm current state
dotnet build NemSim.slnx && dotnet test NemSim.slnx

# Priority 1: finish the sweep run (long-running, background it)
dotnet run --project NEM.CLI -- --fan-out-sweep sweeps/datacentre-nameplate-fy2026.json
dotnet run --project NEM.CLI -- --run-sweep sweeps/datacentre-nameplate-fy2026.json

# Then: p7 diagnosis, sizing-failure visibility fix (Size() in ScenarioRunner.cs),
# run timing, rerun sweep again, browser-verify /dispatch and /sweeps, then
# revisit commit chunking and the origin/nem-076 divergence before any push.
```

PR: https://github.com/HasinthaAttanayake/NemSim/pull/109
