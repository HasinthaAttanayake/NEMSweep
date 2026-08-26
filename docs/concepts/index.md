# What the model does

This section is for anyone who needs to know how a NEMSweep figure was produced before relying on
it. It describes the framework: the dispatch, reliability, storage-sizing and cost engine in
`NEMSweep.Model`. The framework has no hardcoded region list and no AEMO coupling; region
identifiers are free-form strings, and the five-region, financial-year shape of the published
results comes from the data the CLI feeds it. The grid model runs on a fixed one-hour timestep. The
market-time offset, the single fixed UTC offset every series in a run shares, is a run parameter
taken from the scenario period and defaulting to the NEM's UTC+10.

Given a set of regions, each with a demand series, generation and storage, a set of directed
interconnectors, and a reliability standard, the framework dispatches the system hour by hour, grows
battery storage where a region misses the standard, and prices the result. The rest of this section
is how each of those steps works.

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

Four stages, each with a clean boundary.

**Intent.** A `Scenario` is what you asked for. It owns the period, one fleet plan per region, the
cost basis and any interconnectors. It knows nothing about files or data.

**Realisation.** `ScenarioDerivation` combines that intent with parsed demand and weather to produce
a `PowerSystem`, the actual grid to be dispatched. This is a pure transformation. After it, nothing
downstream is scenario-aware.

**Dispatch.** The `Dispatcher` walks the modelled period one hour at a time. Within each hour it
dispatches generation in merit order, moves surplus between regions over the interconnectors, then
operates storage. Demand it cannot meet becomes unserved energy; generation it cannot use becomes
curtailment. See [Dispatch](dispatch.md).

**Assessment.** Reliability is measured as unserved energy against demand energy. If a region misses
its target, `StorageSizingService` grows battery capacity and re-dispatches the entire system, until
either every region complies or the search reports what stopped it. See
[Storage sizing](storage-sizing.md). The final system is then priced, which
[Economics](economics.md) covers.

## Why the two properties matter

**The model is deterministic.** No random draws, anywhere. The same inputs at the same commit
produce identical results, value for value. There are no confidence intervals because there is no
distribution, only the single realisation you asked for.

Determinism makes a difference between two runs attributable: if you changed one input and a number
moved, that input moved it. That is what makes a sweep of dozens of runs a sensible unit of work
rather than a statistical exercise.

**The model is inspectable.** Every assumption is either a value you supplied in the scenario or a
constant in the [assumptions register](../assumptions/index.md), and the register is checked against
the code by a test. Every published result records the SHA-256 digest of the exact input bytes it
was built from. Nothing about a result asks you to take the model's word for it.

## The vocabulary

A small set of terms carries most of the meaning across this section, and each is used in exactly
one sense.

| Term | Means |
|---|---|
| Region | A node in the model, identified by a string. It has one demand series, its own fleets and storage, and imports and exports over interconnectors. The published example uses the five National Electricity Market regions (NSW1, QLD1, SA1, TAS1, VIC1); the framework places no constraint on the identifier or the count. |
| Fleet | All capacity of one technology in one region, treated as a single unit. |
| Merit order | Dispatch order: ascending short-run marginal cost, tie-broken by the `GenerationTechnology` enum order (Solar, Wind, Hydro, Coal, Gas). |
| Residual | Demand less generation, within an interval. Positive is a deficit; negative is a surplus. |
| Unserved energy | Demand that could not be met, from any source. The binding reliability measure, as a percentage of demand energy. |
| Curtailment | Available generation that could not be used and was not stored. |
| Energy served | Demand minus unserved energy. The denominator of every levelised cost. Distinct from delivered generation, which is generation that reached load. |
| Storage sizing | Growing battery capacity until dispatch meets the reliability target, or reporting that the bounded search could not. |
| Sweep | A baseline scenario plus a set of points, each point a free-form override patch on the baseline, lined up along one labelled axis. A point may change any number of inputs. |

## What the framework deliberately does not do

The framework is a system-planning model. It is not a market simulator, not a power-flow model, and
not a forecast.

No bidding, no prices, no settlement. No unit commitment, so no minimum stable generation, no
start-up costs and no ramp rates. No forced outages. No intra-regional network. No frequency control
or system strength. No demand response. No security-constrained economic dispatch.

Those absences are what make the model small enough to run the published 25-point sweep, over five
regions and 8,760 hourly intervals, in under ten minutes on ordinary hardware. That is the trade it
exists to make. They are also why [Limitations](../assumptions/limitations.md) is required reading
before you quote anything from it.

## Where to go next

| Page | Covers |
|---|---|
| [Dispatch](dispatch.md) | The interval loop, merit order, hydro pacing, storage policy |
| [Storage sizing](storage-sizing.md) | The sizing search and what each outcome means |
| [Economics](economics.md) | Annuitisation, what is charged, and what the levelised figures mean |
| [Transmission](transmission.md) | Directed links, prioritised max flow, losses and costing |
| [Domain model reference](../domain-model.md) | Full ownership boundaries, invariants and unit semantics |
