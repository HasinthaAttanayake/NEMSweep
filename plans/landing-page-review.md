# Landing page review — nemsweep.com

A review of `NEMSweep.Web/Pages/Home.razor` and its marketing shell, read against `docs/`,
`README.md` and the published artifacts under `NEMSweep.Web/wwwroot/data`.

The execution plan derived from this review is [`landing-page-rework.md`](landing-page-rework.md).
No product code was changed by either document.

**Scope of the read:** `docs/` (index, concepts, guide, assumptions, exploring), `README.md`,
`Home.razor` and `Home.razor.css`, `MarketingLayout.razor`, `NavMenu.razor`, `Services/Insights/*`,
`Services/DocumentationLinks.cs`, `Program.cs`, `wwwroot/index.html`, and the committed artifacts.

**Counts:** 17 findings · 2 factual defects · 806 words of static copy · 1 chart.

---

## The short version

NEMSweep's documentation is unusually good. It earns trust, states its own biases, and teaches a
method — `docs/exploring/sensitivity-analysis.md` in particular argues by demonstration: it shows the
cliff, then teaches you how to read it.

The landing page inherits the honesty but not the argument. It explains what the model *is* before it
shows what the model *found*, and the one finding that would stop a reader scrolling is a sentence in
the hero rather than a chart.

| | Verdict | |
|---|---|---|
| **Does it entice?** | Partly | The hero sells with a paragraph and a stock-genre photograph. The most arresting thing the model produces — a 110× reliability cliff — is buried in prose and never drawn. |
| **Does it educate?** | Yes | Section 02 teaches what a sweep is and why one run isn't enough. That's the strongest thing on the page. It arrives after two sections of argument, before any proof. |
| **Does it sell?** | No | No install command, no code, no author, no citation, no licence above the footer. The framework layer — region-agnostic, embeddable, zero dependencies — is invisible. |

---

## The chart that should be above the fold

Drawn from the committed sweep index — `data/sweeps/datacentre-nameplate-fy2026/index.json`,
25 points, no new modelling. The landing page currently has exactly one chart: a fuel-mix bar in
section 04.

| Point | Added load (MW) | Unserved energy | Within target | Sizing outcome | Storage (MWh) | SLCoE |
|---|---|---|---|---|---|---|
| p0 | 0 | 0% | yes | `notRequired` | 14,387.1 | 148.40 |
| p7 | 3,500 | 0.001174% | yes | `resized` | 46,023.0 | **141.17** ← minimum |
| p8 | 4,000 | 0.000982% | yes | `resized` | 85,212.2 | 144.07 |
| **p9** | **4,500** | **0.001373%** | **yes** | `resized` | 97,640.8 | 144.45 |
| **p10** | **5,000** | **0.150564%** | **no** | `storageNoLongerImprovesReliability` | 97,137.6 | 143.96 |
| p24 | 12,000 | 5.925275% | no | `storageNoLongerImprovesReliability` | 108,870.4 | 134.49 |

Three readings, each of which is a headline the page does not currently make:

1. **Unserved energy — 0.0014% → 0.15% in one 500 MW step.** The standard holds to 4,500 MW. At
   5,000 MW unserved energy jumps roughly **110×** and more battery stops closing the gap. 15 of 25
   points miss the standard.
2. **Storage — 14.4 GWh → 97.6 GWh, then a plateau.** One 500 MW step (p7→p8) adds **≈39 GWh** and
   nearly doubles the fleet. Storage need is not proportional to load. The 108,870 MWh plateau is
   where the search gave up, not what the system needs.
3. **System levelised cost — falls, turns, then falls again while failing.** The turn at +3,500 MW is
   the finding. The fall after the cliff is the trap section 01 warns readers about.

Three charts, about ninety seconds of reading, and a stranger understands what NEMSweep is for, that
it produces non-obvious results, and that the results are honest about their own failure modes. The
current page takes four scrolls to get there and never draws any of it. Most of what follows is
downstream of this.

---

## Findings register

Ordered by severity. Line references are to `NEMSweep.Web/Pages/Home.razor` unless noted. The first
two are correctness defects, not opinions — on a page whose entire proposition is traceability, they
cost more than they would anywhere else.

### F-01 · Defect · The hero's headline number is off by one sweep point

`BreakingPoint` takes `Runs.FirstOrDefault(run => run.OutsideReliabilityTarget)` — the first point
that *fails* — and then writes "holds its reliability standard up to" that value. In the published
sweep that point is `p10` at 5,000 MW, where unserved energy is 0.1506% against a 0.002% standard.
The last point that actually holds is `p9` at 4,500 MW.

The same expression drives the first entry in `Questions`, which renders as "the standard holds up to
5,000 MW, *and is missed from there on*" — a single sentence that contradicts itself.

> **Do this.** Take the last compliant run, not the first breaching one, and name the jump:
> `Runs.TakeWhile(r => !r.OutsideReliabilityTarget).LastOrDefault()`. Then the hero reads *"holds to
> 4,500 MW; at 5,000 MW unserved energy jumps 110×"* — correct, and considerably more arresting than
> the current sentence.

*Files: `Home.razor` § `BreakingPoint`, § `Questions[0]`*

### F-02 · Defect · The sweep card commits the exact error section 01 accuses everyone else of

Section 01 carries the heading *"A headline number that improves as the system fails."* Two screens
later the sweep card reports **Levelised cost 148.40 → 134.49**, first point to last point. The last
point is +12,000 MW, where the system leaves 5.93% of demand unserved. Cost per MWh *served* falls
because the unserved megawatt-hours left the denominator. That is the trap, quoted approvingly on the
page that names it.

The documentation is explicit: `docs/exploring/sensitivity-analysis.md`, step 2 — *"Everything past
p10 is a different kind of result… Do not compare them with the compliant points."* Showing unserved
energy in an adjacent row mitigates but does not undo the first-read impression, because the cost
figure is set larger and read first.

> **Do this.** Report the range across the *compliant* points (148.40 → 144.45) as the primary
> figure, and put the endpoint behind a "standard not met" chip in the same card. Better still: make
> the card's lead statistic the breach point itself — that is the card's actual news.

*Files: `Home.razor` § `sweep-entry-facts`*

### F-03 · High · The page has one chart, and it's the least interesting one you own

Figure 1 is a delivered-energy-by-technology mix bar in section 04. Every energy site on the internet
has that bar. Nobody else has the cliff. A reader who lands, scrolls, and leaves in twenty seconds
currently sees a photograph, four numbers, and a paragraph of argument.

> **Do this.** Move a live sweep chart above the fold, driven by the same `SweepAnalysis` the card
> already loads. `LinePlot` and `ScatterPlot` exist in `Components/Viz/`; the landing page uses
> neither.

### F-04 · High · The hero calls its own product "low-fidelity"

The lead reads "lets policy and investment professionals run *low-fidelity models* of the grid."
`README.md` frames the same trade correctly — "fast feedback, no proprietary solver, no
linear-programming background required, and it runs on a laptop" — which is a benefit. In the hero it
is an apology.

The honesty is the brand and must stay. But honesty belongs in the proof line, not in the value
proposition. Nobody buys the thing that introduces itself as the worse thing.

> **Do this.** Lead with the trade as an advantage — "screening-grade, in minutes, on a laptop" — and
> put "deliberately low-fidelity: no unit commitment, no market, no forecast" in a proof strip
> immediately underneath, where it reads as rigour instead of hedging.

### F-05 · High · Both hero CTAs are vague, and one goes to the wrong page

**"See the possibilities"** could sit on any SaaS page in the world; it tells the reader nothing about
what opens. **"Get started"** points at `docs.nemsweep.com/exploring/llm-workflow.html` — an advanced
page about driving the model with a language model, not a getting-started guide. The actual
getting-started page is `/guide/`, and `DocumentationLinks.cs` has no constant for it. The same wrong
target appears in `NavMenu.razor`.

> **Do this.** Add `GettingStarted = BaseUrl + "/guide/"` and point the secondary button at it. Name
> both buttons after what happens: *"See where the NEM breaks"* and *"Run it on your laptop"*.

*Files: `MarketingLayout.razor`, `NavMenu.razor`, `Services/DocumentationLinks.cs`*

### F-06 · High · The framework is invisible; the page reads as an Australian results site

`README.md` and `docs/index.md` both open on the three-layer distinction — framework, NEM scoping,
published example — and call mistaking one for another "the most common way to misread a result." The
landing page collapses entirely into layer three.

What a visitor therefore never learns: region identifiers are free-form strings, so this models any
grid, not just the NEM; `NEMSweep.Model` and `NEMSweep.Contracts` have *zero package dependencies*
and can be referenced straight into another codebase; the licence is BSD-3-Clause, so commercial use
is fine. Each of those is a reason a different person adopts it. All three appear only in the
repository.

> **Do this.** One band, three columns, mirroring the docs table: *The engine* (any grid, any regions,
> embeddable, BSD-3) / *The NEM binding* (AEMO demand, EnergyPlus weather, five regions) / *This site*
> (one worked example, not the limit of the tool). It costs 80 words and doubles the addressable
> audience.

### F-07 · High · An open-source tool with no command on its landing page

There is not one line of code, one terminal command, or one install instruction anywhere on the page.
For a developer or a modeller — the two audiences most likely to actually adopt this — a visible
`docker run` is worth more than every paragraph of argument above it, because it proves the thing runs
and shows exactly how much work adoption is.

> **Do this.** Two blocks, near the end, before the caveats. Both already exist verbatim in
> `README.md`:
>
> ```bash
> docker run --rm -v ./reference:/data:ro -v ./study:/out \
>   ghcr.io/hasinthaattanayake/nemsweep:latest \
>   --run-scenario /data/my-scenario.json
>
> dotnet run --project NEMSweep.CLI -- --run-scenario
> ```

### F-08 · High · The most differentiating capability is hidden behind a button labelled "Get started"

`docs/exploring/llm-workflow.md` is the most unusual asset in this project. Published JSON Schema
regenerated by CI so it cannot drift from the validator; deserialisation that rejects unknown
properties rather than ignoring them; `--fan-out-sweep` that validates 25 generated configs in seconds
before any dispatch runs; determinism so a bad generation has no noise to hide in. That is a genuine,
defensible answer to "can I point an LLM at this without it inventing numbers?" — and almost nobody
else in energy modelling can give it.

On the landing page it is three link labels. No section, no example, no sight of the validation error
that makes the loop work.

> **Do this.** Give it a band with the mechanism visible — the schema URLs, and the one JSON error
> that makes the regenerate loop reliable:
>
> ```json
> {"valid":false,"path":"scenarios/generated.json",
>  "error":{"stage":"Input","code":"invalidConfig",
>  "message":"The JSON property 'nameplateMw' could not be mapped ..."}}
> ```

### F-09 · Medium · No author, no institution, no citation, no date

The page asks policy and investment professionals to quote its figures in their own work, and never
says who built it or how to cite it. `CITATION.cff` exists in the repository — with a rather good line
in it: *"Citation is how a method gets scrutinised rather than just used"* — and the site never
surfaces it.

In this category, provenance of the *author* works the same way as provenance of the *inputs*: it is
what converts a careful reader into a user.

> **Do this.** A byline near the close, and a "Cite this" block rendered from `CITATION.cff`. Add the
> run date of the published example — currently a reader cannot tell whether these results are from
> this month or from 2023.

### F-10 · Medium · Blazor WebAssembly with no prerendering: the landing page has no HTML

`Program.cs` is a plain `WebAssemblyHostBuilder`, and `index.html` ships a boot splash. The `<h1>`,
the value proposition, the section headings and every finding exist only after the .NET runtime
downloads, boots and renders. Search crawlers, link previews, and anyone on a slow connection get a
progress bar. The only indexable copy on the site is the one-sentence `meta description`.

The results app absolutely should stay a WASM app — it's the right tool for reading 8,760 hourly
intervals client-side. The *marketing page* being inside it is the problem.

*Not measured here: the WASM payload size — no .NET SDK was available in the review environment. The
absence of prerendering is read from `Program.cs` and `index.html`, not inferred.*

> **Do this.** Either prerender the `/` route to static HTML at build time, or serve a static
> hand-authored landing page at `/` and mount the WASM app at `/app`. The second is less elegant and
> would take an afternoon. The first keeps one codebase.

### F-11 · Medium · One audience is named; three are being spoken to

The lead addresses "policy and investment professionals." The body then uses *merit order*, *SLCoE*,
*curtailment*, *unserved energy* and *storage sizing outcome* without defining any of them, and the
CTAs go to developer documentation. `docs/guide/glossary.md` exists and the landing page never links
to it.

Meanwhile the two audiences the page is actually written for — energy modellers and developers — are
never named, so neither gets a path built for them.

> **Do this.** Either gloss the four terms inline on first use (a `<dfn>` with a tooltip and a
> glossary link), or split the closing CTA into three explicit doors: *read a result* / *run a study*
> / *embed the engine*. Pick one. Doing neither is the current state.

### F-12 · Medium · The page closes on a disclaimer

Section 07 is titled *"Three things these numbers are not."* It is the last argument a reader meets,
and all three items are negations. The content is right and should absolutely stay — refusing to
overclaim *is* the differentiator. The framing is working against you.

> **Do this.** Same three claims, inverted heading: *"Why you can quote this"* or *"What we refuse to
> claim, and why that makes the rest usable."* Then close on the doors from F-11 rather than on the
> docs button.

---

## Smaller items

**F-13 · The hero image is the LCP element and ships one format.** 113 KB JPEG at 1800×950, no
`srcset`, no AVIF or WebP. It is also the least differentiating thing on the page — transmission
towers and turbines are the stock genre for every energy site there is. If it survives F-03, at least
make it responsive.

**F-14 · Nothing states that it is free.** "Open source" appears once in the eyebrow; BSD-3-Clause
appears only in the footer. For a tool competing against licensed software, "free, and you may use it
commercially" is a headline benefit, not a legal footnote.

**F-15 · GitHub is a plain text link in the top bar.** No star count, no repo card, no contributing
link. For an open-source project, stars are the social proof you have and you are not asking for them.

**F-16 · The numbered sections are a real device applied to a sequence that isn't one.** 01 problem →
02 why sweep → 03 sweeps → 04 baseline → 05 method → 06 inputs → 07 caveats. Numbering promises an
order the content does not have. Either reorder so it does, or drop the numerals and keep the rules.

**F-17 · No result date anywhere on the page.** The run stamps a `runId` and the period is FY2026, but
a reader cannot tell when the sweep was executed or against which commit — even though the artifacts
carry exactly that. Provenance is the whole pitch; surface one line of it.

---

## Copy, line by line

The current copy is well-written — careful, unhedged, no marketing mush. Its problem is order and
weight: it argues before it demonstrates, and it leads with category rather than consequence.

### H1 · hero

| | |
|---|---|
| **Now** | Define your energy assets. Stress-test them. Understand the impacts. *(three imperatives, no subject; could headline any modelling tool in any industry)* |
| **Option A** | Add load to the grid until it breaks. Watch the hour it happens. |
| **Option B** | Where does the grid stop coping — and what does it cost to stop that? |
| **Option C** | A whole year of the NEM, dispatched hour by hour, on your laptop. |

### Lead paragraph · hero

| | |
|---|---|
| **Now** | 61 words, opening on "open-source dispatch framework," containing "low-fidelity," and ending on three abstract nouns. |
| **New** | NEMSweep dispatches a grid hour by hour for a full year, grows storage until it meets a reliability standard, and prices what that took. Change one input, re-run, read what moved. Open source, deterministic, minutes on a laptop. *(38 words)* |

### Hero claim · read live from the sweep

| | |
|---|---|
| **Now** | …the NEM holds its reliability standard up to 5,000 MW of data centre nameplate added. Past that, building more storage stops closing the gap. *(wrong point; see F-01)* |
| **New** | The NEM holds its reliability standard to 4,500 MW of added always-on load. At 5,000 MW unserved energy jumps 110×, and no battery the search could find closes the gap. |

### Buttons

| | |
|---|---|
| **Now** | "See the possibilities" → sweep page · "Get started" → the LLM workflow doc |
| **New** | "See where the NEM breaks" → sweep page · "Run it on your laptop" → `/guide/` |

### Section 01 heading

| | |
|---|---|
| **Now** | Most modelling of the transition is inaccessible and demands your trust *(accurate, but it opens on competitors rather than on you)* |
| **New** | Every number here can be checked, including the ones that make us look bad |

---

## Structure: what the page should say, in what order

The current sequence spends its first two sections on argument and reaches proof at section 03. A
reader who bounces at the second scroll has been told the field is untrustworthy and that sweeps are
useful, but shown nothing. Invert it: lead with the finding, then explain why the method produced it.

| # | Now | Proposed |
|---|---|---|
| 01 | Hero — photo, 61-word lead, claim in prose | **Hero** — the cliff chart, 38-word lead, two named CTAs |
| 02 | Metric strip — four baseline figures | **The finding** — three charts, in full *(new)* |
| 03 | Why this exists — three problems with the field | **Why one run isn't enough** — current section 02, unchanged |
| 04 | Why sweep — four questions, answered live | **Published sweeps** — cards fixed per F-02 |
| 05 | Published sweeps — the first actual proof | **Why you can trust it** — determinism, SHA-256, tested register *(new)* |
| 06 | The baseline — scope + the one chart | **Three layers** — engine / NEM binding / this site *(new)* |
| 07 | Findings — generated from the run | **Run it** — two commands, and the LLM loop *(new)* |
| 08 | What the model does — capability list | **The baseline and its inputs** — current 04 + 06, merged |
| 09 | Trace it back — six input links | **What we refuse to claim** — current 07, reframed |
| 10 | Three things these are not — disclaimers | **Cite it, fork it, ask us** — author, citation, repo *(new)* |

Section 01 as it stands ("most modelling is inaccessible") does real work but it is *defensive* work,
and it currently runs before the reader has any reason to care who you are competing with. Fold its
three points into the trust band at position 05, where they land as proof rather than as grievance.

---

## What is already working

Do not lose these in the rewrite.

1. **Live copy read from artifacts.** The hero claim, the four question answers, the capability list
   and the findings are all generated from the published run rather than written by hand. That is
   genuinely rare and it means the page cannot go stale silently. It is also the single best proof of
   the honesty claim — and the page never tells the reader it is doing it. Say so, in one line.

2. **Section 02.** *"One run tells you what happened. A sweep tells you when it breaks."* That is the
   best sentence on the site. It teaches the core concept in one sentence and it should probably be
   the H1 candidate you test against Option A.

3. **The editorial design system.** Rules instead of cards, no drop shadows, a measured column, two
   inverting bands, serif display against sans body. It reads as a document rather than a product
   page, which is exactly right for this audience. The CSS comments explaining *why* each choice was
   made are better than most design documentation.

4. **Deliberate accessibility work.** Skip link, real alt text, heading order intact, and colour
   tokens with contrast ratios worked out in comments (`--color-notice` darkened from `#a86000` for a
   4.5:1 pass). Keep that standard.

5. **Cost never shown without unserved energy beside it.** The instinct is right throughout — F-02 is
   a lapse in one card, not a pattern.

---

## Order of work

**Today**
- F-01 — fix `BreakingPoint` to the last compliant run. A wrong number in the hero of a trust-first
  page.
- F-02 — sweep card reports the compliant range; endpoint gets a "standard not met" chip.
- F-05 — add `DocumentationLinks.GettingStarted`, repoint the secondary CTA, rename both buttons.

**This week**
- F-03 + F-04 — cliff chart above the fold, 38-word lead, drop "low-fidelity" from the value
  proposition.
- F-07 — two commands on the page.
- F-09 + F-17 — byline, citation block, run date.

**Next**
- F-06 — the three-layer band.
- F-08 — the LLM band, with the validation error visible.
- F-12 + F-16 — reframe the close, reorder to the proposed sequence.

**Bigger call**
- F-10 — prerender `/`, or split the landing page out of the WASM bundle. Worth deciding before
  writing much more marketing copy that nothing can index.
- F-11 — pick an audience strategy: gloss the jargon, or build three explicit doors.
