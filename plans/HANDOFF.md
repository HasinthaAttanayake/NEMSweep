# Session handoff — NEMSweep landing page work

Everything a new session needs to pick this up. Written 2026-09-07.

**Start here, then read `landing-page-rework.md`.**

---

## 1. What exists

All on branch `claude/nemsweep-landing-page-analysis-ft22bp` in `HasinthaAttanayake/NEMSweep`,
under `plans/`:

| File | What it is |
|---|---|
| `HANDOFF.md` | This file. |
| `landing-page-review.md` | The findings: 17 findings, 2 factual defects, copy rewrites, proposed section order, what already works. |
| `landing-page-rework.md` | The execution plan derived from the review: guardrails, ground truth, 16 tasks in 4 phases, 2 open decisions. |
| `split-out-nemsweep-web.md` | The runbook used to split `NEMSweep.Web` into its own repo. **Spent** — the split has been done. Keep for reference only. |

The review also exists as a published HTML artifact with three rendered charts:
**https://claude.ai/code/artifact/794b5736-647e-49f3-836f-3b990f10dbef**

The markdown version carries the same content; the artifact adds the charts as SVG. If you need the
artifact's content in a new session, read it with the Artifact tool (`action: "read"`, that URL) —
or just use `landing-page-review.md`, which is self-contained.

Commits on the branch:

```
ceabbec Drop NEMSweep.Model from the split runbook's new repository
6431c25 Add runbook for splitting NEMSweep.Web into its own repository
867e459 Add landing page review as markdown
ffe1e43 Add landing page rework execution plan
d881c5f Open the landing page with a photograph of the grid   <-- review baseline
```

**The review and plan were written against `d881c5f`.** See §4 — they are now partly stale.

---

## 2. Current state, and what is blocked

**The split has happened.** `NEMSweep.Web` now lives at
`HasinthaAttanayake/NEMSweep.Web`, with an open PR:
https://github.com/HasinthaAttanayake/NEMSweep.Web/pull/1

**That PR has not been reviewed.** Nobody has checked it against the runbook's acceptance criteria.

**Blocker: the previous session could not reach `NEMSweep.Web`.** Three attempts at `add_repo`
returned `you don't have access to hasinthaattanayake/nemsweep.web`, and `list_repos` showed only
`NEMSweep`, `2019-engineering-thesis-code`, `javascript-exercises-odin`, `learning-notes`.

The suspected cause is that a session's GitHub credential is minted at container start and does not
pick up repository grants made afterwards. **First thing to try in a new session: call `list_repos`
and see whether `NEMSweep.Web` now appears.** If it does, the blocker was staleness and it is gone.
If it does not, the grant did not save — check the repo is selected by name at
https://claude.ai/admin-settings/claude-tag. (The repo name contains a dot, which is legal but
unusual; worth ruling out only if everything else checks out.)

**Not yet shared: the marketing pack.** The user has one, plus in-flight copy improvements. Neither
has been seen. This matters — see §4.

---

## 3. Verified facts — do not re-derive these

These cost real effort to establish. They were checked against committed artifacts and source, not
inferred.

### 3.1 The two defects (the highest-value work, and copy-independent)

**Defect 1 — `BreakingPoint` is off by one sweep point.**
In `NEMSweep.Web/Pages/Home.razor` (~line 532 at `d881c5f`):

```csharp
SweepRun? breach = sweep?.Runs.FirstOrDefault(run => run.OutsideReliabilityTarget);
// ...then describes it as "holds its reliability standard up to {breach.AxisValue}"
```

`breach` is the first run that **fails**. The copy calls it the last one that **holds**. Against the
published sweep this renders "holds up to 5,000 MW" when the last compliant point is 4,500 MW.

The same expression drives `Questions[0]` (~line 586), rendering "the standard holds up to 5,000 MW,
**and is missed from there on**" — self-contradictory in one sentence.

Fix: `Runs.TakeWhile(r => !r.OutsideReliabilityTarget).LastOrDefault()`. Move the derivation into
`Services/Insights/SweepAnalysis.cs` (which has `SweepAnalysisTests.cs`) so it can be tested — it is
currently in a Razor `@code` block and cannot be.

**Defect 2 — the sweep card compares a compliant point with a failing one.**
`Home.razor` ~line 230 renders `first.Scalars.SlcoeAudPerMwh → last.Scalars.SlcoeAudPerMwh`, i.e.
**148.40 → 134.49**. The last point leaves 5.93% of demand unserved; cost per MWh *served* fell
because the unserved MWh left the denominator.

This is the exact error section 01 of the same page names ("A headline number that improves as the
system fails"), and the comparison `docs/exploring/sensitivity-analysis.md` step 2 forbids.

### 3.2 Sweep ground truth

From `NEMSweep.Web/wwwroot/data/sweeps/datacentre-nameplate-fy2026/index.json`, 25 points, axis
"Data centre nameplate added" (MW).

| Point | MW | USE % | Within target | Sizing outcome | Storage MWh | SLCoE |
|---|---|---|---|---|---|---|
| p0 | 0 | 0 | yes | `notRequired` | 14,387.1 | 148.40 |
| p7 | 3,500 | 0.001174 | yes | `resized` | 46,023.0 | **141.17** (min) |
| p8 | 4,000 | 0.000982 | yes | `resized` | 85,212.2 | 144.07 |
| **p9** | **4,500** | **0.001373** | **yes** | `resized` | 97,640.8 | 144.45 |
| **p10** | **5,000** | **0.150564** | **no** | `storageNoLongerImprovesReliability` | 97,137.6 | 143.96 |
| p24 | 12,000 | 5.925275 | no | `storageNoLongerImprovesReliability` | 108,870.4 | 134.49 |

- p9 is the last compliant point; p10 is the first breach. **15 of 25 points miss the standard.**
- USE jumps **≈110×** between them (0.150564 / 0.001373).
- p7→p8 adds **≈39 GWh** of storage for 500 MW of load.
- The 108,870 MWh plateau is where the search gave up, not a requirement.

### 3.3 Baseline run

From `NEMSweep.Web/wwwroot/data/results-overview.json`: FY2026 (2025-07-01 → 2026-07-01, UTC+10),
8,760 hourly intervals, 5 regions, 10 directed links. SLCoE 148.40 AUD/MWh. Total annualised cost
27,697,567,560.96 AUD. Grid-scale renewable share 40.56%. Unserved energy 0 against a 0.002%
standard. Sizing `notRequired` at 5,624.2 MW / 14,387.1 MWh. Curtailment 8,892,453.6 MWh.

### 3.4 Dependency facts (established during the split)

- `NEMSweep.Web` → `NEMSweep.Contracts` (used) and `NEMSweep.Model` (**declared but never used** —
  the only occurrence of the text `NEMSweep.Model` in Web or Web.Tests was the `.csproj` line).
- `NEMSweep.Contracts` references nothing. Every DTO/enum the site consumes lives there.
- `NEMSweep.Web.Tests` → `NEMSweep.Web` only.
- **`.gitignore` trap:** `data/` is ignored and un-ignored only by the literal
  `!NEMSweep.Web/wwwroot/data/`. Flattening or renaming that folder silently drops ~300 files
  (103 MB) with no error.
- `docs/` mentions `NEMSweep.Web` only in code spans, never as markdown links, so
  `docfx --warningsAsErrors` still passes after removal.
- `NEMSweep.CLI.Tests/Scenarios/RunMetadataTests.cs` contains `"NEMSweep.Web"` as synthetic path
  strings only. It touches no filesystem and still passes. **Do not "fix" it.**

### 3.5 Known consequence of the split, unresolved

`NEMSweep.CLI/appsettings.example.json` sets `"dataRoot": "NEMSweep.Web/wwwroot/data"`. Once the
removal PR merges, the CLI still compiles and its tests still pass, but
`dotnet run --project NEMSweep.CLI -- --run-scenario` fails at runtime — the demand and weather
artifacts left with the web project. **This needs a decision and has not had one.**

Also: `NEMSweep.Contracts` now exists in both repos and can drift, until the planned NuGet package
replaces the copy.

---

## 4. Why the plan is partly stale

`landing-page-rework.md` was written against `d881c5f`, in the old repo, before:

1. the code moved to `NEMSweep.Web`,
2. the user began improving copy,
3. the user produced a marketing pack.

**The marketing pack should win** on positioning, voice and audience. Specifically it likely
supersedes or resolves:

- **T-05** — hero H1 and lead rewrites. My options were a critic's guesses.
- **T-12** — reframing the closing section.
- **D-2** — the audience-strategy decision (gloss the jargon vs. build three doors).

**What survives untouched**, because it is correctness or structure rather than voice:

- **T-01, T-02** — the two defects. No marketing decision touches them.
- **T-03** — put a sweep chart above the fold.
- **T-06** — the three-layer band.
- **T-07** — a runnable `docker run` / `dotnet run` on the page.
- **T-09, T-10, T-11** — author, citation, run date, licence, GitHub affordance.
- **D-1** — prerendering.

**The recommendation given, unchanged:** decide D-1 (prerendering) *before* the copy push lands.
The page is Blazor WASM with no prerendering, so the H1, the value proposition and every heading
exist only after the runtime boots — the only indexable text on the site is one meta description.
Writing marketing copy that nothing can index, then retrofitting prerendering, means integrating
twice. The split has made this cheaper: the web repo is standalone now, so `/` can go static-first
without touching the model repo.

---

## 5. Suggested order of work

1. **CI in `NEMSweep.Web`.** The runbook flagged the new repo has none. Until it exists every PR is
   unverified. Build + test on push. Gates everything else.
2. **T-01 + T-02 as one correctness PR.** Copy-independent, unit-testable, and proves the new repo's
   build → test → PR loop end to end.
3. **Decide D-1**, then structure prerendering / static-first.
4. **Re-baselined copy and structure**, driven by the marketing pack.
5. **T-06, T-07, T-10** — additive, pack-independent.

Before any of that: **review PR #1** on `NEMSweep.Web` against §6.

---

## 6. Acceptance checks for NEMSweep.Web PR #1

The four things most likely to have gone wrong in the split:

- [ ] `NEMSweep.Web.slnx` lists exactly three projects: `NEMSweep.Contracts`, `NEMSweep.Web`,
      `NEMSweep.Web.Tests`.
- [ ] `NEMSweep.Web/NEMSweep.Web.csproj` has exactly one `ProjectReference`, to
      `NEMSweep.Contracts`. `grep -c "NEMSweep.Model"` on it returns 0.
- [ ] No `NEMSweep.Model` folder in the repo.
- [ ] ~300 files under `NEMSweep.Web/wwwroot/data` are actually committed (≈102 MB).
      **This is the one that fails silently** — see the `.gitignore` trap in §3.4. Confirm even if
      the build is green.

Plus: `dotnet build NEMSweep.Web.slnx` and `dotnet test NEMSweep.Web.slnx` both pass.

---

## 7. Prompts to restart

### 7.1 Resume the landing page work

> I'm continuing work on the NEMSweep landing page. Context is in the `NEMSweep` repo on branch
> `claude/nemsweep-landing-page-analysis-ft22bp`, under `plans/` — read `HANDOFF.md` first, then
> `landing-page-rework.md`. The web project now lives in its own repo,
> `HasinthaAttanayake/NEMSweep.Web`. Start by calling `list_repos` to check whether you can reach it,
> and attach it with `add_repo` if so.
>
> Before doing anything else, re-baseline the plan: it was written against commit `d881c5f` in the
> old repo, and since then the code moved and I've been improving the copy. I'm attaching my
> marketing pack — treat it as authoritative on positioning, voice and audience, and tell me which
> plan tasks it supersedes, which survive, and which change shape.

*(Attach the marketing pack. Without it, the copy tasks can't be re-baselined.)*

### 7.2 Just fix the two defects

> In `HasinthaAttanayake/NEMSweep.Web`, fix the two correctness defects described in
> `plans/HANDOFF.md` §3.1 of the `NEMSweep` repo (branch
> `claude/nemsweep-landing-page-analysis-ft22bp`): the `BreakingPoint` off-by-one in `Home.razor`,
> and the sweep card ranging levelised cost across a compliant and a non-compliant point.
>
> Move the derivation into `Services/Insights/SweepAnalysis.cs` so it can be unit-tested, add tests
> to `SweepAnalysisTests.cs` covering all-compliant, first-run-breaching, mid-sweep breach, and the
> published shape (compliant at index 9, breach at index 10). Don't touch any copy beyond the
> sentences these two produce. One PR.

### 7.3 Review the split PR

> Review https://github.com/HasinthaAttanayake/NEMSweep.Web/pull/1 against the acceptance checks in
> `plans/HANDOFF.md` §6 of the `NEMSweep` repo (branch
> `claude/nemsweep-landing-page-analysis-ft22bp`). Pay particular attention to whether the ~300 data
> files under `NEMSweep.Web/wwwroot/data` were actually committed — the `.gitignore` rule makes that
> fail silently.

### 7.4 Decide prerendering

> Read `plans/HANDOFF.md` §4 and `plans/landing-page-rework.md` §9 D-1 in the `NEMSweep` repo
> (branch `claude/nemsweep-landing-page-analysis-ft22bp`). The landing page is Blazor WASM with no
> prerendering, so none of its copy is indexable. Now that `NEMSweep.Web` is a standalone repo, work
> up the two options — prerender `/` at build time, or serve a static landing page at `/` and mount
> the app at `/app` — with the trade-offs, and recommend one. Note that option 2 would forfeit the
> page's live-copy-from-artifacts property, which is currently its best proof of the honesty claim.

### 7.5 Resolve the CLI data path

> After `NEMSweep.Web` is removed from the `NEMSweep` repo,
> `NEMSweep.CLI/appsettings.example.json` points `dataRoot` at a folder that no longer exists, so
> `--run-scenario` fails at runtime although everything still compiles and tests pass. See
> `plans/HANDOFF.md` §3.5. Work up the options for where the CLI's input data should live and
> recommend one. Do not change published artifacts.

---

## 8. Conventions carried from this session

- Git: develop on `claude/nemsweep-landing-page-analysis-ft22bp`, `git push -u origin <branch>`,
  never force-push, don't open PRs unless asked.
- Don't modify anything under `NEMSweep.Web/wwwroot/data` — published artifacts with recorded
  SHA-256 provenance.
- Don't hardcode modelled figures into markup. The page reads them from artifacts at render time,
  which is its strongest honesty proof. §3.2 exists for *verification*, not for pasting into copy.
- Keep the landing page's design language: rules not cards, no drop shadows, a measured column, two
  inverting bands, serif display against sans body. The CSS comments explain each choice — extend
  them, don't strip them.
- Preserve the accessibility standard: skip link, real alt text, intact heading order, 4.5:1 text
  contrast (see the worked comments in `wwwroot/css/app.css`).
- Never show a cost figure without unserved energy beside it.
