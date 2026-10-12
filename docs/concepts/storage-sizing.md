# Storage sizing

Storage sizing is the framework step that, given a realised system that misses its reliability
target, searches for the Battery capacity that meets it, or reports why the bounded search could
not. It is `StorageSizingService` in `NEMSweep.Model` and, like dispatch, is region-agnostic.

This page explains that search. For the full invariant detail, see
[Storage sizing](../domain-model.md#storage-sizing) and [Storage](../domain-model.md#storage) in the
domain model.

## The loop

`StorageSizingService.Size` is pure and whole-system scoped. It never mutates the system it is
handed. Instead it builds an immutable `PowerSystem` candidate, re-dispatches the **entire linked
system**, meaning every region and not just the one being grown, and checks every region's dispatch
outcome against the configured reliability target. Only regions that fail are grown; a compliant
region is left untouched. The loop is:

```text
build a candidate PowerSystem
dispatch the whole linked system
check every region against the USE target
grow one failing region, then re-dispatch and re-rank
repeat
```

Re-dispatching the whole system on every candidate, rather than just the region being resized, is
what keeps the search honest about inter-regional transfer: growing one region's Battery can change
what it needs to import or can afford to export, which can move another region's outcome too.

Only **Battery** is sizeable. Pumped hydro is fixed at whatever the scenario declared, because a new
pumped-hydro scheme is a specific site and reservoir, not a quantity that scales in arbitrary
increments the way a battery fleet does.

## The floor

A region with no Battery, or one below the floor, is raised to it before growth begins: **30 MW and
120 MWh**, which together fix a four-hour minimum duration. Every candidate the search produces after
that, at every stage of growth and refinement, preserves that four-hour floor. The floor exists
because probing upward from a fleet too small to change the outcome at all would waste dispatch
passes on candidates that could never matter.

## Growth

Once a region is at or above the floor, each growth iteration doubles its current Battery capacity.
It probes larger energy, larger power (bounded by the four-hour duration floor at the new power
level), and a combined larger-energy-and-power candidate, each capped at the configured maximum
power and maximum energy for that region. Every candidate is dispatched against the whole linked
system. Within that region, the search accepts its best candidate only when that candidate reduces
the region's own total unserved energy by **more than 0.001 MWh**. Peak unserved power does not
override this acceptance threshold.

When more than one region fails, growth priority is the region with the greatest total unserved
energy, with equal totals ordered by ordinal region ID. The search grows only that region, then
re-dispatches and re-ranks all regions after an accepted change. If a region has no larger candidate,
or its best candidate does not clear the material-improvement threshold, it is blocked for the
current system state; the search skips it and tries the other failing regions. After any accepted
change, all still-failing regions are reconsidered and ranked again. Thus one blocked region does
not prevent another from improving or reaching its target.

Growth stops when every currently failing region is blocked, the dispatch-pass budget is exhausted,
or the whole-system energy assessment applies. Per-region results identify the blocker that applies
to that region. If growth makes no progress while regions still fail, the whole-system status is
`BatteryCapacityLimitReached` when **any** failing region has no larger candidate; otherwise it is
`StorageNoLongerImprovesReliability`. A cap on only power or only energy is not exhaustion when a
larger candidate remains on another dimension. Termination evidence lists all currently failing
regions and their blockers. These outcomes describe the bounded search, not global infeasibility or
optimality; only `EnergyLimited` proves that additional Battery storage cannot meet the target under
the assessed total-energy deficit.

The descending-unserved-energy priority is intentional and can change successful sizing results
compared with growing regions in ordinal order. It does not change dispatch behavior or reserve
stored energy for a forecast event.

## Refinement

Once every region is compliant, the search is not finished: doubling can overshoot by a wide margin,
so each region the search actually changed is refined by bisection in ordinal region order. It
refines power first, then energy, to **1 MW and 1 MWh precision**, using a full-system dispatch at
each probe and keeping only candidates that remain compliant across the whole system. A region the
growth phase never touched is left exactly as installed; refinement only revisits changed regions.

## The six outcomes

A run terminates in one of six `StorageSizingOutcome` values. The JSON name is what a published
artifact carries; the .NET name is what you see in the API reference and in source. Whole-system
results and sweep points report the search's aggregate outcome. Regional results and regional
summaries report that region's outcome, which can differ from the aggregate.

| JSON name | .NET name | Whole-system meaning | Regional meaning | What to do next |
|---|---|---|---|---|
| `notRequired` | `NotRequired` | Every region met the target with unchanged installed Battery capacity. | This region meets its target and its Battery capacity was unchanged, even if another region failed. | No additional Battery was required for the reported scope. |
| `resized` | `Resized` | Every region meets its target after growth and completed refinement. | This region meets its target and its Battery capacity changed. Other regions may still fail, so refinement may never have run. | A successfully refined whole-system result is near-frontier, not cost-optimal. Do not interpret a compliant region's capacity in a failed run as a refined requirement. |
| `energyLimited` | `EnergyLimited` | Total available generation energy across the whole system is under total demand energy. | This region still fails when the system-wide energy assessment terminates the search; the evidence is not a region-specific energy deficit. | Add generation, not storage. See [when storage cannot be the answer](#when-storage-cannot-be-the-answer). |
| `storageNoLongerImprovesReliability` | `StorageNoLongerImprovesReliability` | Growth made no progress: every still-failing region had larger candidates, but none materially reduced its own unserved energy. | This region's tested larger candidates did not reduce its own unserved energy by more than 0.001 MWh. Other regions may be compliant or capacity-blocked. | Investigate timing or storage-policy limitations (see [Limitations §4](../assumptions/limitations.md#4-greedy-storage-dispatch-is-wrong-in-both-directions)); stagnation is not infeasibility. |
| `batteryCapacityLimitReached` | `BatteryCapacityLimitReached` | Growth made no progress and at least one still-failing region had no larger allowed candidate. Other failing regions may have stagnated. | This region still fails and has no larger candidate under its configured Battery power and energy limits. | Raise a limiting ceiling or accept the residual shortfall. A single dimension at its ceiling is not exhaustion if another larger candidate remains. |
| `passLimitReached` | `PassLimitReached` | The dispatch-pass budget was exhausted during growth or refinement. If refinement was interrupted, every region can already meet its target. | This region still fails when the whole-run pass budget expires; it has not necessarily exhausted its own candidates. A compliant region instead reports `notRequired` or `resized`. | Raise `maximumPasses`; interrupted growth is inconclusive on feasibility, and interrupted refinement is not a completed near-frontier result. |

Only `energyLimited` is a proof that no Battery could have met the target. Capacity-limit,
stagnation, and pass-limit outcomes are evidence about this bounded search, not proof of global
infeasibility or optimality.

## When storage cannot be the answer

Whenever a dispatch fails its target, the model separately checks whether the failure could ever be
fixed by storage at all. `EnergyLimitedAssessment` sums generator availability and demand across
*every* aligned region in the whole `PowerSystem`, applying the same renewable-resource and monthly
generation-budget rules dispatch itself uses. **Storage is excluded from that sum**, deliberately: a
battery shifts energy between intervals but cannot add to the whole-period total, so including it
would let a system smuggle in a false pass. If total available generation energy across the system is
below total demand energy over the dispatch period, the run reports `EnergyLimited` with the
available energy, demand energy, the shortfall, and the intervals where available power fell short of
demand power.

This assessment is a **system-level proof**, and it is deliberately not attributed to any one
region: it says something about the system's aggregate energy balance, not about which region's
fleet is short. Two things follow from what it actually proves. A total-energy shortfall proves
infeasibility outright, even under an idealised assumption of unrestricted future transfer between
regions. The converse does not hold, though: adequate total system energy does **not** prove the
network can actually deliver it. A region can still fail its target for reasons that have nothing
to do with total energy, such as transfer capacity, timing, or local Hydro pacing.

## Seeding

The opening state of charge for every storage fleet is 80% of installed capacity for PumpedHydro
and 50% for everything else. It is computed once, from the **scenario-declared** installed
capacity, and never from capacity storage sizing has since grown. If growing a fleet also grew its
opening balance, the search would be handing itself free energy at the start of every candidate it
tested, and would stop measuring what installed capacity alone achieves, which is the thing
actually being searched over. See [Model assumptions](../assumptions/index.md) for the seed
fractions themselves.

## What a sizing result is, and is not

A sizing result is a **deterministic coordinate-wise near-frontier point**. It is not a global
minimum, and it is not cost-optimal: nothing in the search prices the capacity it adds, so the
search cannot trade off, say, more Battery energy against less Battery power, or against a different
technology entirely. A different search order, meaning a different growth or refinement strategy,
could land on a different but equally compliant point. Two sizing results from the same procedure
are comparable to each other because the procedure is deterministic; neither one is "the answer" in
any stronger sense. See
[Limitations §5](../assumptions/limitations.md#5-sizing-finds-a-near-frontier-point-not-an-optimum)
for what that means for how you should read a sizing result.

## Next

- [Dispatch](dispatch.md): the mechanism each sizing candidate is re-run against.
- [Economics](economics.md): how the final Battery capacity a sizing run settles on gets costed.
- [Limitations](../assumptions/limitations.md): where sizing results will mislead you if read as
  more than they are.
