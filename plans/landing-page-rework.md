# Landing page rework — execution plan

**Target:** `NEMSweep.Web/Pages/Home.razor` and the marketing shell around it.
**Source:** a review of `docs/`, `README.md` and the published artifacts against the live landing page.
**Status:** not started. No code has been changed by the review that produced this plan.

This document is written to be executed by an agent starting cold. Read §1–§4 before touching
anything; they carry the guardrails and the ground-truth figures that several tasks depend on.

---

## 1. What this project is, in the terms this plan uses

NEMSweep is a deterministic hourly grid dispatch, reliability, storage-sizing and cost engine. Three
layers, and the distinction matters for several tasks below:

| Layer | What it is | Where |
|---|---|---|
| Framework | The engine. No hardcoded region list, no AEMO coupling, region ids are free-form strings, fixed one-hour timestep, no package dependencies, embeddable, BSD-3-Clause. | `NEMSweep.Model`, `NEMSweep.Contracts` |
| NEM scoping | The CLI that binds the framework to Australia: AEMO demand, EnergyPlus weather, five NEM regions. | `NEMSweep.CLI` |
| Published example | One baseline scenario plus one sweep. Everything on nemsweep.com is this one example. | `NEMSweep.Web`, `NEMSweep.Web/wwwroot/data` |

The landing page currently presents only the third layer. Several tasks below exist to fix that.

**Key files**

- `NEMSweep.Web/Pages/Home.razor` — the landing page markup and its `@code` block (729 lines).
- `NEMSweep.Web/Pages/Home.razor.css` — its scoped stylesheet (835 lines).
- `NEMSweep.Web/Layout/MarketingLayout.razor` — the landing page's shell: top bar, footer.
- `NEMSweep.Web/Services/DocumentationLinks.cs` — every URL into `docs.nemsweep.com`.
- `NEMSweep.Web/Services/Insights/SweepAnalysis.cs` — derived facts about a sweep. Unit-tested.
- `NEMSweep.Web/Services/Insights/SystemAnalysis.cs` — derived facts about the baseline run.
- `NEMSweep.Web/Components/Viz/` — `LinePlot`, `ScatterPlot`, `MixBar`, `StackPlot`, `CompareBars`,
  `PlotGeometry` (which carries `PlotFormat`). The landing page currently uses only `MixBar`.
- `NEMSweep.Web/wwwroot/index.html` — boot splash, `<title>`, meta and Open Graph tags.
- `NEMSweep.Web/wwwroot/data/` — the committed artifacts the site reads. **Read-only for this plan.**

**Build and test**

```bash
dotnet build NEMSweep.slnx
dotnet test NEMSweep.slnx
```

Existing web tests live in `NEMSweep.Web.Tests/`. `SweepAnalysisTests.cs` and `SystemAnalysisTests.cs`
are the ones this plan adds to.

---

## 2. Guardrails

**Do not:**

- Change anything under `NEMSweep.Web/wwwroot/data/`. Those are published artifacts with recorded
  SHA-256 provenance. No task here requires regenerating them.
- Change `NEMSweep.Model`, `NEMSweep.Contracts` or `NEMSweep.CLI`. This is a presentation-layer plan.
- Hardcode any modelled figure into markup. Every number the page states about a run must be read
  from the artifacts at render time. The page already works this way and it is the strongest proof
  of the site's own honesty claim — preserve it. §4 exists so you can *verify* what the code should
  produce, not so you can paste numbers into copy.
- Introduce a card-and-drop-shadow visual language on the landing page. The design deliberately uses
  rules, a measured column and two inverting bands. Read the comments at the top of
  `Home.razor.css` before adding any component.
- Weaken any caveat. The refusal to overclaim is the product's differentiator. Tasks that reframe
  caveats keep every claim and change only the framing.
- Add a package dependency to `NEMSweep.Model` or `NEMSweep.Contracts`.

**Must:**

- Keep the accessibility standard already set: skip link, real alt text on every image, intact
  heading order, visible focus states, and colour tokens that pass 4.5:1 for text. See the worked
  contrast comments in `NEMSweep.Web/wwwroot/css/app.css`.
- Keep every landing-page section resilient to a failed artifact load. The current page distinguishes
  "no sweeps published" from "the manifest could not be read" and renders `ArtifactLoadStateView`
  rather than silently dropping a section. Any new section that reads artifacts does the same.
- Run `dotnet build NEMSweep.slnx && dotnet test NEMSweep.slnx` before each commit.

---

## 3. Ground truth for verification

All figures below are read from committed artifacts. Use them to check that your changes render
what they should. Do not paste them into markup.

**Baseline run** — `NEMSweep.Web/wwwroot/data/results-overview.json`

| Fact | Value |
|---|---|
| Period | 2025-07-01 to 2026-07-01, UTC+10 (FY2026) |
| Resolution / intervals | 1 hour / 8,760 |
| Regions | NSW1, QLD1, SA1, TAS1, VIC1 |
| Directed interconnector links | 10 |
| SLCoE | 148.40 AUD/MWh served |
| Total annualised cost | 27,697,567,560.96 AUD |
| Grid-scale renewable share | 40.56% |
| Unserved energy | 0 MWh (0%), against a 0.002% standard |
| Storage sizing outcome | `notRequired`, 5,624.2 MW / 14,387.1 MWh |
| Curtailed energy | 8,892,453.6 MWh |

**Published sweep** — `data/sweeps/datacentre-nameplate-fy2026/index.json`, axis "Data centre
nameplate added" in MW, 25 points.

| Point | Axis MW | USE % of demand | Within target | Sizing outcome | Storage MWh | SLCoE |
|---|---|---|---|---|---|---|
| p0 | 0 | 0 | yes | `notRequired` | 14,387.1 | 148.40 |
| p7 | 3,500 | 0.001174 | yes | `resized` | 46,023.0 | 141.17 |
| p8 | 4,000 | 0.000982 | yes | `resized` | 85,212.2 | 144.07 |
| **p9** | **4,500** | **0.001373** | **yes** | `resized` | 97,640.8 | 144.45 |
| **p10** | **5,000** | **0.150564** | **no** | `storageNoLongerImprovesReliability` | 97,137.6 | 143.96 |
| p11–p24 | 5,400–12,000 | 0.248 → 5.925 | no | `storageNoLongerImprovesReliability` | 108,870.4 | 144.18 → 134.49 |

Derived facts this plan relies on:

- **p9 is the last compliant point. p10 is the first breach.** 15 of 25 points miss the standard.
- USE rises from 0.001373% to 0.150564% between them — a factor of **≈110×** in one 500 MW step.
- p7 → p8 adds **≈39 GWh** of storage for 500 MW of load, nearly doubling the fleet.
- SLCoE falls to a minimum of 141.17 at p7 among compliant points, then turns and rises to 144.45
  at p9. It falls again across the failing points to 134.49 only because unserved energy leaves the
  denominator.

The documentation already works this sweep through: `docs/exploring/sensitivity-analysis.md`.
Read it before Phase 1.

---

## 4. Phasing

Four phases. Phase 1 is correctness and should ship on its own. Phases 2–4 are separable and can be
one commit each or one branch each; do not batch Phase 1 with anything else.

Two items (D-1, D-2 in §9) need a human decision before their tasks can start. Everything else is
specified enough to execute.

---

## 5. Phase 1 — Correctness (do first, ship separately)

### T-01 — `BreakingPoint` names the wrong sweep point

**Severity: defect.** The landing page's headline claim overstates the system's headroom by one
sweep point.

**Current state** — `NEMSweep.Web/Pages/Home.razor`, `BreakingPoint` property (~line 532):

```csharp
SweepRun? breach = sweep?.Runs.FirstOrDefault(run => run.OutsideReliabilityTarget);
return sweep is null || breach is null
    ? null
    : $"the NEM holds its reliability standard up to {PlotFormat.Compact(breach.AxisValue)} "
        + $"{sweep.AxisUnit} of {sweep.AxisLabel.ToLowerInvariant()}. Past that, building "
        + "more storage stops closing the gap.";
```

`breach` is the first run that **fails**. The sentence then describes it as the last run that holds.
Against the published sweep this renders "holds its reliability standard up to 5,000 MW" — but at
5,000 MW unserved energy is 0.1506% against a 0.002% standard. The last holding point is 4,500 MW.

The same expression drives the first entry of the `Questions` property (~line 586), which renders
"the standard holds up to 5,000 MW, and is missed from there on" — self-contradictory.

**Change**

1. Add two derived properties to `NEMSweep.Web/Services/Insights/SweepAnalysis.cs`, beside the
   existing `First`, `Last` and `CheapestUnitCost`:
   - `LastCompliantRun` — the last run before the first breach, i.e.
     `Runs.TakeWhile(run => !run.OutsideReliabilityTarget).LastOrDefault()`.
   - `FirstBreachingRun` — the existing `FirstOrDefault(run => run.OutsideReliabilityTarget)`.

   Both must be null-safe for a sweep where every run holds, and for one where the *first* run
   already breaches (then `LastCompliantRun` is null and the copy must handle it).

2. Rewrite `BreakingPoint` to use `LastCompliantRun` for the "holds to" value and `FirstBreachingRun`
   for the jump. Target copy:

   > The NEM holds its reliability standard to 4,500 MW of added always-on load. At 5,000 MW
   > unserved energy jumps 110×, and no battery the search could find closes the gap.

   The multiplier is computed from the two runs' `UnservedEnergyPercentageOfDemand`, not written
   down. Guard against a zero denominator: when the last compliant run has 0% unserved, state the
   breach percentage instead of a ratio.

   The final clause is only true when the breach run's sizing outcome is
   `storageNoLongerImprovesReliability`, `batteryCapacityLimitReached` or `energyLimited`. Branch on
   the outcome rather than asserting it — `docs/assumptions/limitations.md` §5 explains why these
   are different claims.

3. Fix `Questions[0]` the same way. Its current sentence must not survive.

**Acceptance**

- New tests in `NEMSweep.Web.Tests/Services/SweepAnalysisTests.cs` cover: all runs compliant;
  first run breaching; a breach in the middle; and the published shape (compliant run at index 9,
  breach at index 10).
- Rendered against the committed sweep, the hero says 4,500 MW, not 5,000 MW.
- `dotnet test NEMSweep.slnx` passes.

---

### T-02 — The sweep card compares a compliant point against a failing one

**Severity: defect.** Section 01 of this same page carries the heading *"A headline number that
improves as the system fails."* The sweep card then does exactly that.

**Current state** — `Home.razor` ~line 230, inside `.sweep-entry-facts`:

```razor
<dd>@PlotFormat.Money(first.Scalars.SlcoeAudPerMwh) → @PlotFormat.Money(last.Scalars.SlcoeAudPerMwh)</dd>
```

`first` and `last` are the first and last runs of the sweep. Against the published sweep this renders
**148.40 → 134.49**, which reads as "adding 12 GW of load makes power 9% cheaper". The last point
leaves 5.93% of demand unserved; the cost per MWh *served* fell because the unserved megawatt-hours
left the denominator.

`docs/exploring/sensitivity-analysis.md` step 2 states the rule directly: *"Everything past p10 is a
different kind of result … Do not compare them with the compliant points."*

Unserved energy is already shown in an adjacent row, which is honest, but the cost figure is read
first and sets the impression.

**Change**

1. Report the levelised-cost and annual-cost ranges across the **compliant** runs: `First` →
   `LastCompliantRun` (from T-01). Against the published sweep that is 148.40 → 144.45.
2. Where the sweep contains breaching runs, render the endpoint as a separate, visibly marked fact
   rather than as the other end of a range — a "standard not met" chip carrying the endpoint's axis
   value, SLCoE and unserved percentage together.
3. Apply the same treatment to the "Storage energy built" and "Annual cost" rows, which use the same
   `first`/`last` pair via the `StorageBuild(first, last)` helper (~line 672).
4. Where every run is compliant, the card keeps its current first → last behaviour.

**Acceptance**

- The card no longer presents a compliant figure and a non-compliant figure as two ends of one range.
- A sweep with no breaching runs renders unchanged.
- The chip is distinguishable without colour alone (text label, not just a red background).

---

### T-03 — "Get started" points at the wrong page, and both CTAs are unnamed

**Current state**

- `Home.razor` ~line 50: primary button reads **"See the possibilities"** → the first sweep's route.
- `Home.razor` ~line 55: secondary button reads **"Get started"** → `DocumentationLinks.LlmWorkflow`,
  which is `https://docs.nemsweep.com/exploring/llm-workflow.html` — an advanced page about driving
  the model with a language model, not a getting-started guide.
- `NEMSweep.Web/Services/DocumentationLinks.cs` has constants for `Home`, `Limitations`,
  `Assumptions` and `LlmWorkflow`. There is no constant for the guide.
- The same "Get started" label and wrong target appear in `NEMSweep.Web/Layout/NavMenu.razor`
  (`.nav-foot`).

The actual getting-started page is `docs/guide/index.md`, published at
`https://docs.nemsweep.com/guide/`.

**Change**

1. Add to `DocumentationLinks.cs`, with an XML doc comment matching the style of its neighbours:

   ```csharp
   /// <summary>The guide: prerequisites, first scenario run, and where results are written.</summary>
   public const string GettingStarted = BaseUrl + "/guide/";
   ```

2. Repoint both "Get started" links (`Home.razor`, `NavMenu.razor`) at `GettingStarted`.
3. Rename both hero buttons after what happens when they are pressed:
   - primary → **"See where the NEM breaks"**
   - secondary → **"Run it on your laptop"**

**Acceptance**

- No link labelled "Get started" resolves to the LLM workflow page.
- `LlmWorkflow` is still referenced from the places that genuinely mean it (the sweep invitation, the
  "Trace it back" list, the footer).

---

## 6. Phase 2 — Lead with the finding

### T-04 — Put a sweep chart above the fold

The page currently has exactly one chart: a delivered-energy-by-technology `MixBar` in section 04.
The most arresting thing the model produces — the reliability cliff — is stated in prose and never
drawn.

**Change**

Add a chart to the hero, or immediately below it and above section 01, driven by the same
`SweepAnalysis` the page already loads in `OnInitializedAsync`. Three series are worth plotting; at
minimum plot the first:

1. **Unserved energy vs axis.** Flat and effectively zero through the compliant points, then the
   cliff. Shade the compliant and non-compliant regions of the axis distinctly and mark the boundary
   at the last compliant run.
2. **Storage sized vs axis.** Shows the non-linearity and the plateau where the search stops
   succeeding. The plateau must be labelled as *where the search gave up*, not as a requirement —
   `docs/assumptions/limitations.md` §5.
3. **SLCoE vs axis.** Shows the turn among the compliant points. If plotted, the failing stretch must
   carry the same non-compliant marking as T-02's chip, or it repeats T-02's error in chart form.

Use the existing `LinePlot` / `ScatterPlot` in `NEMSweep.Web/Components/Viz/`. Do not add a charting
library. Every chart needs a real `aria-label` describing the shape and the endpoints, and a
`figcaption` naming the finding.

**Watch:** the page deliberately loads `results-overview.json` rather than the full result so the
landing page costs ~19 KB rather than two megabytes (see the comment in `OnInitializedAsync`). The
sweep index is already loaded; do not start pulling per-point detail artifacts into the landing page.

**Acceptance**

- The chart renders from the sweep index already in memory; no additional artifact fetch.
- Non-success artifact states still render `ArtifactLoadStateView`.
- Page still renders correctly with no sweeps published.

---

### T-05 — Rewrite the hero copy

**Current state** — `Home.razor` ~lines 28–37. The `<h1>` is *"Define your energy assets.
Stress-test them. Understand the impacts."* — three imperatives with no subject, which could headline
any modelling tool in any industry. The lead is 61 words and describes the product as
**"low-fidelity models of the grid"**.

`README.md` frames the same trade correctly, as a benefit: *"fast feedback, no proprietary solver, no
linear-programming background required, and it runs on a laptop."* The honesty must stay; it belongs
in a proof line rather than in the value proposition.

**Change**

1. Replace the `<h1>`. Candidates, in preference order:
   - "Add load to the grid until it breaks. Watch the hour it happens."
   - "Where does the grid stop coping — and what does it cost to stop that?"
   - "One run tells you what happened. A sweep tells you when it breaks."
     *(this is the current section 02 heading and the best sentence on the site; if it is promoted,
     give section 02 a new heading)*
2. Replace the lead with roughly 38 words. Target:

   > NEMSweep dispatches a grid hour by hour for a full year, grows storage until it meets a
   > reliability standard, and prices what that took. Change one input, re-run, read what moved.
   > Open source, deterministic, minutes on a laptop.

3. Move the fidelity statement into a proof strip under the hero, phrased as scope rather than
   apology — "no unit commitment, no market, no forecast: deliberately small enough to sweep" — and
   link it to `docs/assumptions/limitations.md`.
4. Remove the word "low-fidelity" from the hero. It may stay in the documentation, where it is
   correct and read in context.

**Acceptance**

- The `<h1>` names the grid or the failure, not a generic capability.
- The lead is under 45 words and contains no self-deprecating adjective.
- The fidelity caveat still appears on the page, above the fold or immediately below it.

---

### T-06 — Say that the page writes itself from the run

The hero claim, the four question answers, the capability list in section 05 and the finding cards
are all generated from the published artifacts at render time rather than written by hand. That is
the single best proof of the honesty claim and the page never tells the reader it is happening.

**Change:** one line, near the metric strip or the findings block — that every figure on this page is
read from the run's own artifacts each time it loads, so it cannot go stale without the run changing.
Section 05 already says a version of this about itself; lift it somewhere a reader meets earlier.

---

## 7. Phase 3 — Sell the thing

### T-07 — Add the three-layer band

`README.md` and `docs/index.md` both open on the framework / NEM-scoping / published-example
distinction, and `docs/index.md` calls mistaking one for another "the most common way to misread a
result". The landing page collapses entirely into the third layer.

What a visitor therefore never learns: region identifiers are free-form strings so this models any
grid; `NEMSweep.Model` and `NEMSweep.Contracts` have zero package dependencies and can be referenced
straight into another codebase; the licence is BSD-3-Clause so commercial use is fine.

**Change:** one band, three columns, mirroring the table in §1 of this plan — *The engine* (any grid,
any regions, embeddable, BSD-3) / *The NEM binding* (AEMO demand, EnergyPlus weather, five regions) /
*This site* (one worked example, not the limit of the tool). Roughly 80 words total. Place it after
the sweeps section, before the baseline.

---

### T-08 — Put a runnable command on the page

There is currently no code block, terminal command or install instruction anywhere on the landing
page. For the two audiences most likely to adopt — modellers and developers — a visible `docker run`
proves the thing runs and shows exactly how much work adoption is.

**Change:** a block near the end, before the caveats. Both commands already exist verbatim in
`README.md`; take them from there so they cannot drift:

```bash
docker run --rm -v ./reference:/data:ro -v ./study:/out \
  ghcr.io/hasinthaattanayake/nemsweep:latest \
  --run-scenario /data/my-scenario.json
```

```bash
dotnet run --project NEMSweep.CLI -- --run-scenario
```

Add a copy-to-clipboard affordance only if it can be done without a JS dependency; the site currently
ships one small `wwwroot/js` surface and a `ViewportScroller` interop.

---

### T-09 — Give the LLM workflow its own band

`docs/exploring/llm-workflow.md` is the most differentiating asset in the project: published JSON
Schema regenerated by CI so it cannot drift from the validator; deserialisation that rejects unknown
properties rather than ignoring them; `--fan-out-sweep` validating 25 generated configs in seconds
before any dispatch; determinism so a bad generation has no run-to-run noise to hide in. On the
landing page it is three link labels.

**Change:** a band showing the mechanism, not just asserting it. Include the schema URLs
(`schema/scenario-v5.json`, `schema/sweep-v1.json` on raw.githubusercontent) and the validation error
that makes the regenerate loop reliable:

```json
{"valid":false,"path":"scenarios/generated.json",
 "error":{"stage":"Input","code":"invalidConfig",
 "message":"The JSON property 'nameplateMw' could not be mapped ..."}}
```

Carry across at least one guardrail from the docs — the `axisValue` check is the highest-value one —
so the band does not read as an unqualified "point an LLM at it" claim.

---

### T-10 — Author, citation, and a run date

The page asks policy and investment professionals to quote its figures and never says who built it or
how to cite it. `CITATION.cff` exists in the repository and the site never surfaces it. There is also
no date anywhere: a reader cannot tell whether the published results are from this month or from two
years ago.

**Change**

1. A byline near the close, sourced from `CITATION.cff`.
2. A "Cite this" block rendering the citation in one standard form. `CITATION.cff` is the single
   source; do not duplicate the metadata into the Razor markup by hand — either read it at build
   time or reference it.
3. Surface one line of run provenance: the period, the commit the run was built at, and whether the
   working tree was dirty. The sweep index's `provenance` block already carries this;
   `NEMSweep.Web/Components/ProvenanceFooter.razor` already renders provenance elsewhere on the site
   and should be reused rather than reimplemented.

---

### T-11 — Say that it is free

"Open source" appears once, in the hero eyebrow. BSD-3-Clause appears only in the footer. For a tool
whose main competition is licensed software, "free, and you may use it commercially" is a headline
benefit. Fold it into T-07's engine column and the closing CTA.

---

### T-12 — Ask for the GitHub star

GitHub is a plain text link in the top bar of `MarketingLayout.razor`. No star count, no repo card,
no contributing link. Add a repo affordance to the closing section, linking to the repository and to
`CONTRIBUTING.md`. Do not fetch live star counts from the GitHub API at render time — the site is a
static WASM deploy and should not take a runtime dependency on api.github.com.

---

## 8. Phase 4 — Structure and framing

### T-13 — Reframe the close

Section 07 is titled *"Three things these numbers are not"* and all three items are negations. It is
the last argument a reader meets. The content is right and must stay — refusing to overclaim is the
differentiator — but the framing works against it.

**Change:** same three claims, inverted heading. "Why you can quote this", or "What we refuse to
claim, and why that makes the rest usable." Then close on the audience doors (T-15) rather than on
the documentation button.

---

### T-14 — Reorder the page

The numbered sections (01–07) promise a sequence, but the current order spends two sections on
argument before any proof arrives at 03. Either reorder so the numbering earns itself, or drop the
numerals and keep the rules.

Proposed order:

| # | Section | Source |
|---|---|---|
| — | Hero — cliff chart, 38-word lead, two named CTAs | T-04, T-05, T-03 |
| 01 | The finding — the charts in full | T-04 |
| 02 | Why one run isn't enough | current section 02, unchanged |
| 03 | Published sweeps | current section 03, cards fixed per T-02 |
| 04 | Why you can trust it — determinism, SHA-256 provenance, tested assumptions register | new; absorbs the three points of current section 01 |
| 05 | Three layers | T-07 |
| 06 | Run it — commands, and the LLM loop | T-08, T-09 |
| 07 | The baseline and its inputs | current sections 04 and 06, merged |
| 08 | What we refuse to claim | current section 07, reframed per T-13 |
| — | Cite it, fork it | T-10, T-11, T-12 |

Current section 01 ("most modelling is inaccessible") does real work but it is defensive work, and it
currently runs before the reader has any reason to care who NEMSweep competes with. Its three points
land better as proof inside the trust section at position 04.

Update the in-page anchor nav in `MarketingLayout.razor` (`#problem`, `#why-sweep`, `#sweeps`,
`#case-study`, `#method`) to match whatever the final section ids are. Keep the existing ids as
anchors where a section survives, so external links do not break.

---

### T-15 — Decide an audience strategy

The lead addresses "policy and investment professionals". The body then uses *merit order*, *SLCoE*,
*curtailment*, *unserved energy* and *storage sizing outcome* without defining any of them, and both
CTAs go to developer documentation. `docs/guide/glossary.md` exists and the landing page never links
to it.

**This needs decision D-2 (§9) before execution.** Either option is a real fix; doing neither is the
current state.

---

### T-16 — Make the hero image responsive, or retire it

`NEMSweep.Web/wwwroot/img/hero-transmission-wind.jpg` is 113 KB at 1800×950, single format, no
`srcset`. It is the LCP element. It is also the stock genre for every energy site there is.

If T-04 puts a chart above the fold and the photograph survives beside it, add `srcset` and an AVIF or
WebP source. If the chart replaces it, delete the image and its `wwwroot/img/CREDITS.md` entry.

---

## 9. Decisions needed from a human

### D-1 — Prerendering

`NEMSweep.Web/Program.cs` is a plain `WebAssemblyHostBuilder` and `wwwroot/index.html` ships a boot
splash. The `<h1>`, the value proposition, every section heading and every finding exist only after
the .NET runtime downloads, boots and renders. Search crawlers, link previews and anyone on a slow
connection get a progress bar. The only indexable copy on the site is the one-sentence
`meta description` in `index.html`.

The results app should stay a WASM app — it is the right tool for reading 8,760 hourly intervals
client-side. The marketing page being inside it is the problem.

Two options:

1. **Prerender `/` to static HTML at build time.** Keeps one codebase. Needs a build step and a
   decision about how the artifact-derived copy is resolved at prerender time versus at runtime.
2. **Serve a static hand-authored landing page at `/`, mount the WASM app at `/app`.** Less elegant,
   roughly an afternoon, and splits the landing copy away from the components that generate it —
   which would forfeit T-06's proposition.

Worth deciding before writing much more marketing copy that nothing can index. **Do not start this
without a decision.** Note that this plan's Phase 2 and 3 tasks are written against the current WASM
page and remain correct under either option; option 2 would require porting them.

### D-2 — Audience

Pick one:

1. **Gloss the jargon.** Define *merit order*, *SLCoE*, *curtailment* and *unserved energy* inline on
   first use, with a link through to `docs/guide/glossary.md`. Keeps one path, serves the stated
   policy/investment audience.
2. **Build three doors.** Split the closing CTA into *read a result* / *run a study* / *embed the
   engine*, and name all three audiences explicitly. Serves the modeller and developer audiences the
   page is currently written for but never addresses.

Both are defensible. (1) is less work and matches the stated audience; (2) matches who the copy
actually speaks to and pairs naturally with T-07.

---

## 10. Preserve these

Five things already work and must survive the rework. If a change would lose one of them, stop and
flag it rather than trading it away.

1. **Live copy read from artifacts.** The hero claim, the question answers, the capability list and
   the findings are generated from the published run, so the page cannot go stale silently.
2. **Section 02's framing.** "One run tells you what happened. A sweep tells you when it breaks."
3. **The editorial design system.** Rules instead of cards, no drop shadows, a measured column, two
   inverting bands, serif display against sans body. The CSS comments explaining each choice are
   better than most design documentation — extend them, do not strip them.
4. **The accessibility standard.** Skip link, real alt text, intact heading order, contrast ratios
   worked out in comments.
5. **Cost never shown without unserved energy beside it.** T-02 is a lapse in one card, not a
   pattern. Keep the pattern.

---

## 11. Done

- [ ] Phase 1 shipped on its own: T-01, T-02, T-03, with tests.
- [ ] `dotnet build NEMSweep.slnx` and `dotnet test NEMSweep.slnx` pass.
- [ ] No file under `NEMSweep.Web/wwwroot/data/` is modified.
- [ ] No modelled figure is hardcoded into markup.
- [ ] Every landing-page section that reads an artifact handles a failed load visibly.
- [ ] The hero states 4,500 MW, not 5,000 MW.
- [ ] No link labelled "Get started" resolves to the LLM workflow page.
- [ ] D-1 and D-2 answered, and their tasks either executed or explicitly deferred.
