# Dispatch

Dispatch is the framework's hour-by-hour simulation that turns a realised power system into
generation, storage, transfer and reliability figures. It is part of `NEMSweep.Model`, has no
hardcoded region list, and takes region identifiers as free-form strings. The grid model runs on a
fixed one-hour timestep, so sub-hourly demand input is resampled to it before dispatch sees it. The
published example runs a financial year because that is the period of the ingested data.

This page explains the mechanism well enough to predict what a change to a scenario will do to it.
For the full ownership and invariant detail, see the [domain model](../domain-model.md).

## From scenario to power system

A scenario is intent: regions, fleets, cost parameters, interconnectors. `ScenarioDerivation.Derive`
turns that intent, plus one aligned demand series per region and optional aligned weather resources,
into a realised `PowerSystem`. That transformation is where a few decisions are made once and then
fixed for the rest of the run:

- Each `ScenarioGeneratingFleet` becomes a `GeneratingFleet` whose short-run marginal cost is derived
  and stored: variable operating cost plus fuel price multiplied by heat rate. Dispatch never
  recomputes this; it reads the value the derivation produced.
- Storage plans with positive capacity become fleets; a zero-capacity plan is omitted from the
  region's fleet list but its cost and technology assumptions are retained on the region, so storage
  sizing can introduce a fleet later without becoming scenario-aware itself.

After derivation, dispatch is **scenario-blind**. `Dispatcher` and everything it calls only ever see
the realised `PowerSystem`, meaning regions, fleets, demand and interconnectors, never the scenario
that produced it.

## Interval outer, region inner

`SystemDispatchRun.Execute` drives the whole system through the modelled period with the **interval
as the outer loop and the region as the inner loop**:

```text
for each interval:
    for each region: dispatch generation
    run inter-regional transfer for this interval
    for each region: run storage, then Hydro's reserve fallback
```

The reason for the inversion is that every region needs to sit at the same interval at the same time
for transfer to mean anything. A surplus in one region can only serve a deficit in another within
that interval if both regions have already reached it before either one moves on. Running each
region to completion before starting the next, the more obvious loop order, would never put two
regions at the same interval simultaneously, and transfer would have nothing to connect.

A direct consequence: a system with **no interconnectors** produces results identical to dispatching
each region independently. Nothing about the loop structure itself favours a linked system; every
difference a link makes flows entirely through the transfer step.

## Order within an interval

For each interval, the sequence is fixed: **generation, then inter-regional transfer, then storage,
then the Hydro reserve fallback**, which is strictly local and never exportable.

Generation runs first because transfer needs to know each region's post-generation deficit or
surplus before it can move anything. Storage runs after transfer, not before, so that a region's
surplus can beat its own battery to a neighbour's unserved load: exporting first means a region never
locks energy into local storage that another region needed for that hour. The Hydro reserve fallback
runs last of all, after that region's own storage, and it is invisible to transfer entirely. It is
reachable only from inside the region's own completion step, never from generation, exports, or
storage charging.

## Merit order

Within the generation step, fleets dispatch in ascending short-run marginal cost (SRMC), tie-broken
by the declaration order of the `GenerationTechnology` enum (Solar, Wind, Hydro, Coal, Gas) for
determinism:

```text
SRMC = variable operating cost + fuel price × heat rate
```

Solar, Wind and, in cost terms, Hydro all carry zero fuel cost, so they tie at zero SRMC extremely
often. The technology tie-break then decides which of them gets dispatched first, and therefore
which one's surplus gets curtailed first when there is more available than demand needs.
Conventional Hydro is sorted into this order like any other technology; nothing about
`GenerationMeritOrder` singles it out.

## Hydro pacing

Hydro is unlike every other technology in one respect: it carries a monthly *energy* budget instead
of a fuel cost. Sorted purely by cost, that budget would be spent on whichever hours happen to come
first each month, which is not how a reservoir with a season to last is actually operated. An earlier
version tried the intuitive fix of sorting Hydro last in merit order, so that it only ran once
cheaper technologies had already served demand, and that stranded roughly 93% of the budget.
Relocating Hydro in the sort order did not ration it: being sorted last just meant the demand
reaching it was small in most hours, so the reservoir sat mostly unused all month and then had
nothing meaningful left to contribute at genuine peaks.

The fix that shipped is a **causal threshold controller**, `HydroReservationState`, layered
independently of sort position. Each interval, Hydro's dispatch is capped at:

```text
min(nameplate, remainingBudget, max(0, residualDemand − T))
```

where `residualDemand` is demand net of intermittent-renewable output for that interval, and `T` is a
threshold solved by bisection so that, applied retrospectively over a **trailing 336-interval window
of past residual-demand observations**, it would have spent exactly the budget affordable per
interval over the intervals left in the month (`remainingBudget / intervalsLeft`). Raising `T` runs
Hydro only on the highest-residual hours in that window; lowering it lets Hydro run more broadly. The
controller adjusts `T` every interval so the fleet self-calibrates to whatever the demand
distribution actually looks like, without per-region tuning. During the first 48 intervals of a run
there is no usable history, so the fleet instead runs flat at the affordable average.

The monthly budget is split 90/10: 90% is the pool this controller paces (the "paced" pool); the
remaining 10% is held out of merit order entirely (the "reserve" pool) and spent only as a
last-resort local backstop, after that region's own storage has already run, against whatever
deficit remains. Neither pool carries into the next month, so with fewer than three days left the
unspent reserve is released into the paced pool rather than being wasted at the month boundary.

One point runs through the whole mechanism: **there is no foresight anywhere in it.** Every input
to the threshold and the cap is either the scenario-declared budget (known up front, not forecast),
calendar arithmetic, the current interval's own residual demand, or a window of strictly *past*
observations. Nothing reads ahead of the interval it is pricing.

## Storage

Storage dispatch is deliberately narrow in what it is trusted with. `RegionalDispatchRun` builds a
`DispatchContext`, an immutable snapshot of the *current interval only*: signed residual power
(positive is unmet demand, negative is would-be-curtailed surplus), and one scalar snapshot per
storage and generation fleet describing its headroom and cost. An `IStoragePolicy` implementation
receives that context and returns a `StorageDecision`: zero or more `StorageIntent` values, each
naming a fleet and a requested MW flow.

The policy owns intent and fleet ordering only. It does not own state of charge, execute storage
physics, or book unserved demand and curtailment. The dispatch run clamps every intent against real
headroom, and each `StorageFleet` remains the final authority on power limits, energy limits and
round-trip loss. A policy can therefore ask for something physically impossible, such as more
discharge than the fleet has energy for or more charge than its power rating allows, but it cannot
cause it: the fleet's `Operate` transition reconciles the request down to what is actually
deliverable.
That boundary is what makes `IStoragePolicy` a safe extension point: swapping in a different policy,
including one with foresight, requires no change to dispatch itself, because dispatch was never
trusting the policy with anything it could get wrong.

The shipped default, `GreedySurplusAndIncrementalGenerationChargingPolicy`, discharges Battery before
PumpedHydro on a deficit; on a surplus it charges from curtailed generation first, then may start
incremental Coal or Gas generation (ascending SRMC) to fill remaining charge headroom.

## The dispatch identity

Every region's flows close exactly, each interval, against one identity:

```text
generation + discharge + imports + unserved = demand + charge + exports + curtailment
```

Summed across the whole system, imports and exports do not cancel exactly, because every export
arrives at its destination minus whatever transmission losses consumed along the way. The
system-level form of the identity therefore replaces the import and export terms with a single
losses term:

```text
generation + discharge + unserved = demand + charge + curtailment + transmission losses
```

Nothing enters or leaves the system as a whole: every unit exported by one region is either another
region's import or lost in transit, and the system identity is exactly what is left after imports and
exports cancel and the loss they generated is accounted separately.

## Unserved energy and curtailment

Generation and demand rarely balance exactly in any given interval, and the model resolves the gap
into exactly two residuals. **Unserved energy** is demand that nothing could reach in that interval,
whether local generation, storage or import. **Curtailment** is generation, almost always
intermittent renewable output, that nothing needed and nothing could absorb.

Reliability is measured on unserved **energy** as a percentage of demand energy, not on hours served
or peak unserved power. Those two are useful diagnostics, but they are not the binding measure: a
system can serve 99.99% of hours and still fail the standard, because the hours it misses can be the
largest ones. See [Storage sizing](storage-sizing.md) for how that percentage drives the search for
compliant Battery capacity.

## Next

- [Storage sizing](storage-sizing.md): how Battery capacity is grown until dispatch meets its
  reliability target.
- [Transmission](transmission.md): how inter-regional transfer decides what moves between regions
  before storage runs.
- [Limitations §4](../assumptions/limitations.md#4-greedy-storage-dispatch-is-wrong-in-both-directions):
  what the absence of foresight in storage dispatch does to a result.
