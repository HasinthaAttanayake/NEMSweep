# What the model does

NemSim answers one question, repeatedly and cheaply:

> Given this set of regions — with this demand, these generation assets, these interconnectors and
> these economics — what happens when you dispatch it hour by hour for a year, how much storage
> does it take to hold the reliability standard, and what does the whole thing cost per MWh served?

Everything else on this site is either how to ask that question, or how to read the answer.

## The pipeline

```text
scenario config (JSON)
        │
        │  ScenarioDerivation.Derive  ── + demand series, + weather traces
        ▼
   PowerSystem            regions, fleets, storage, directed interconnectors
        │
        │  StorageSizingService.Size  ── outer loop, only when the target is missed
        │      └── Dispatcher.DispatchSystem  ── re-dispatched once per candidate
        ▼
   dispatch evidence      per-region and whole-system series, hour by hour
        │
        ├── SystemReliabilityAssessment   did it hold the standard?
        └── PowerSystemCostCalculator     what does it cost per MWh served?
                │
                ▼
        published artifacts (JSON)
```

Four stages, each with a clean boundary:

**Intent.** A `Scenario` is what you asked for. It owns the period, one fleet plan per region, the
cost basis and any interconnectors. It knows nothing about files or data.

**Realisation.** `ScenarioDerivation` combines that intent with parsed demand and weather to
produce a `PowerSystem` — the actual grid to be dispatched. This is a pure transformation; after it,
nothing downstream is scenario-aware.

**Dispatch.** The `Dispatcher` walks the year an hour at a time. Within each hour it dispatches
generation in merit order, moves surplus between regions over the interconnectors, then operates
storage. What demand it cannot meet becomes unserved energy; what generation it cannot use becomes
curtailment. See [Dispatch](dispatch.md).

**Assessment.** Reliability is measured as unserved energy against demand energy. If a region misses
its target, `StorageSizingService` grows Battery capacity and re-dispatches the entire system, until
either every region complies or the search reports why it cannot. See
[Storage sizing](storage-sizing.md). The final system is then priced — see [Economics](economics.md).

## The two properties that matter

**It is deterministic.** No random draws, anywhere. The same inputs at the same commit produce
identical results, value for value. There are no confidence intervals because there is no
distribution, only the single realisation you asked for.

That sounds like a weakness and is mostly a strength. It means a difference between two runs is
attributable: if you changed one input and a number moved, that input moved it. It is what makes
sweeps — dozens of runs varying one axis — a sensible unit of work rather than a statistical
exercise.

**It is inspectable.** Every assumption is either a value you supplied in the scenario or a constant
documented in the [assumptions register](../assumptions/index.md), and the register is checked
against the code by a test. Every published result records the SHA-256 of the exact input bytes it
was built from. Nothing about a result requires you to take our word for it.

## The vocabulary

A small set of terms carries most of the meaning. They are used precisely throughout.

| Term | Means |
|---|---|
| **Region** | One NEM region, modelled as a single node. NSW1, QLD1, SA1, TAS1, VIC1. |
| **Fleet** | All capacity of one technology in one region, treated as a single unit. |
| **Merit order** | Dispatch order: ascending short-run marginal cost, tie-broken by technology. |
| **Residual** | Demand less generation, within an interval. Positive is a deficit; negative is surplus. |
| **Unserved energy (USE)** | Demand that could not be met. The binding reliability measure, as a percentage of demand energy. |
| **Curtailment** | Available generation that could not be used and was not stored. |
| **Delivered to load** | Demand minus unserved. The denominator of every levelised cost. |
| **Sizing** | Growing Battery capacity until the reliability target is met, or reporting that it cannot be. |
| **Sweep** | A series of runs varying one input, with everything else held constant. |

## What it deliberately does not do

NemSim is a system-planning model. It is not a market simulator, not a power-flow model, and not a
forecast.

No bidding, no prices, no settlement. No unit commitment — no minimum stable generation, no
start-up costs, no ramp rates. No forced outages. No intra-regional network. No frequency control
or system strength. No demand response.

Those absences are what make it small enough to run a 25-point sweep over five regions and 8,760
hours in a reasonable time, which is the trade it exists to make. They are also why
[Limitations](../assumptions/limitations.md) is required reading before you quote anything from it.

## Where to go next

| | |
|---|---|
| [Dispatch](dispatch.md) | The hourly loop, merit order, hydro pacing, storage policy |
| [Storage sizing](storage-sizing.md) | The sizing search and what each outcome means |
| [Economics](economics.md) | Annuitisation, what is charged, and what the levelised figures mean |
| [Transmission](transmission.md) | Directed links, prioritised max flow, losses and costing |
| [Domain model reference](../domain-model.md) | Full ownership boundaries, invariants and unit semantics |
