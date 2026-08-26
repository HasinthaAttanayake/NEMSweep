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
grow the failing regions
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
power and maximum energy for that region. Each of those candidates is dispatched against the whole
linked system, and
the search keeps the one that most reduces unserved energy for the failing region, provided the
reduction is material (more than a small fixed tolerance, so a candidate is never adopted for noise).
If none of the probes materially improves unserved energy, the search stops advancing that region and
reports `StorageNoLongerImprovesReliability`.

Growth advances one failing region at a time, in deterministic ordinal region order, and returns to
re-dispatch the whole system after each single change. It does not grow every failing region in one
pass before re-checking.

## Refinement

Once every region is compliant, the search is not finished: doubling can overshoot by a wide margin,
so each region the search actually changed is refined by bisection. A full-system probe at each
refinement step narrows that region's power, then its energy, to **1 MW and 1 MWh precision**,
keeping only candidates that remain compliant across the whole system. A region the growth phase never
touched is left exactly as installed; refinement only revisits changed regions.

## The six outcomes

A run terminates in one of six `StorageSizingOutcome` values. The JSON name is what a published
artifact carries; the .NET name is what you see in the API reference and in source.

| JSON name | .NET name | What it means | What to do next |
|---|---|---|---|
| `notRequired` | `NotRequired` | The installed fleet already met the target, unchanged. | Nothing. The scenario as declared is already reliable. |
| `resized` | `Resized` | The search grew Battery capacity and the target was met. | Read the final MW/MWh as the near-frontier point, not a cost-optimal answer. See [what a sizing result is, and is not](#what-a-sizing-result-is-and-is-not). |
| `energyLimited` | `EnergyLimited` | Total available generation energy across the whole system is under total demand energy. | Add generation, not storage. No Battery size will fix this. See [when storage cannot be the answer](#when-storage-cannot-be-the-answer). |
| `storageNoLongerImprovesReliability` | `StorageNoLongerImprovesReliability` | Every feasible larger candidate failed to materially reduce unserved energy before hitting the configured capacity limits. | Investigate whether the shortfall is a generation-timing problem or a storage-policy limitation (see [Limitations §4](../assumptions/limitations.md#4-greedy-storage-dispatch-is-wrong-in-both-directions)) rather than assuming more Battery would help. |
| `batteryCapacityLimitReached` | `BatteryCapacityLimitReached` | The configured per-region MW or MWh ceiling was hit before the target was met. | Raise the ceiling if it was set conservatively, or accept the residual shortfall. |
| `passLimitReached` | `PassLimitReached` | The dispatch-pass budget was exhausted before the target was met. | Raise `maximumPasses`, or treat the run as inconclusive rather than a verdict on feasibility. |

Only `energyLimited` is a proof that no Battery could have met the target. The other three failure
outcomes each report a bound the search ran into, so none of them establishes infeasibility.

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
