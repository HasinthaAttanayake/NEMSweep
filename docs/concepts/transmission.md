# Transmission

Transmission is how a surplus in one region reaches a deficit in another within the same interval.
This page explains the layering that makes that possible, the algorithm underneath it, and the two
approximations in its cost model. For the full invariant detail, see the
[domain model](../domain-model.md).

## Interconnectors are directed and owned by the system

An `Interconnector` holds one directed transfer capacity, from `FromRegionId` to `ToRegionId`,
metered at the sending end. It is owned by `PowerSystem`, not by either endpoint region, because a
link belongs to neither region alone and attributing it to one side or the other would be
arbitrary. A corridor that carries flow both ways is not one bidirectional link; it is **two**
directed interconnectors, each independently declared and each independently costed. That
convention matters for economics too: see [Economics](economics.md) and
[Limitations §6](../assumptions/limitations.md#6-reciprocal-interconnectors-are-each-costed-at-the-corridors-full-length)
for what declaring both directions does to reported cost.

## The layering

`InterRegionalTransfer` is the only place the domain meets the graph, and the split of
responsibility is the interesting part:

- **The transfer layer** (`InterRegionalTransfer`) knows about regions, power, and losses. It maps
  each region's post-generation surplus or deficit onto a node in a capacity graph, delegates to the
  pure algorithms below to find out what can move, and books the outcome back onto the regions as
  imports and exports.
- **The algorithm layer** (`NEM.Model/Algorithms`) knows none of that. `EdmondsKarp` finds maximum
  flow on an abstract capacity graph; `FlowPathDecomposition` turns an edge-flow solution into
  discrete source-to-sink routes; `PrioritisedTransferSolver` sequences sinks and calls both. None of
  it has ever heard of a region, a megawatt, or a transmission loss. Those are the transfer layer's
  concern entirely.

That separation is what keeps the max-flow solver a genuinely standard one: it never has to reason
about losses, priority, or anything domain-specific, and the transfer layer never has to reimplement
graph search.

## Prioritised transfer

Regions in deficit are ranked by size, largest first (ties broken by region identity for
determinism), and served **one at a time**. Each sink gets a full max-flow solve over whatever source
capacity remains after every higher-priority sink has already been served. Once a sink's committed
flow is settled, it is subtracted from edge capacity, and the *next* sink starts from a fresh network
built on what is left, not from the residual graph a max-flow solve would normally leave behind. That
matters: a max-flow solve's residual graph carries reverse edges that a later solve could push flow
back along, effectively clawing back capacity already committed to an earlier sink. Discarding the
residual graph at each stage boundary is exactly what stops that. No reverse edge crosses a stage
boundary, so a lower-priority region can never claw back flow already committed to a higher-priority
one.

That guarantee, that a higher-priority region is never starved by a lower-priority one, is also
exactly why the result is not a global optimum. A different allocation might serve total demand
better in aggregate while starving the top-priority sink a little; the solver will never choose that
allocation.

## Why a sink is solved iteratively

A sink's requirement is stated at the **receiving** end, as how much it still needs delivered, but
max-flow capacity is metered at the **sending** end of every edge, and a route's hop count (and
therefore how much of what is sent actually survives to arrive) is not known until the flow is
decomposed into paths after the solve. Those two units do not reconcile in one shot, so each sink is
solved by successive approximation: send as much as the outstanding requirement suggests, measure
what actually arrived after losses, and solve again for whatever shortfall remains. Because delivered
energy can never exceed sent energy, each round can only reduce the outstanding requirement rather
than overshoot it, so the sequence converges geometrically toward exactly satisfying the sink, or
toward the network's true capacity limit if it cannot be fully satisfied.

## Losses

Every hop of a transfer loses a flat **5%** of what enters that hop, applied *over* the max-flow
result rather than folded into the search itself. Because capacity in the graph is metered at the
sending end of every edge, flow is exactly conserved in the capacity graph, and the max-flow search
never has to reason about decay along a route. That is precisely what keeps it a standard max-flow
problem. A two-hop route delivers `0.95²` of what it sent; a hop count of `n` delivers `0.95ⁿ`.

**This 5% figure is an unsourced placeholder.** AEMO publishes marginal loss factors per
interconnector, and they are neither flat nor equal across links. Any conclusion that depends
materially on inter-regional transfer inherits this uncertainty until a cited value replaces it. See
[Model assumptions](../assumptions/index.md) for the tracking reference (NEM-053), which is the
highest-priority open assumption in the model.

## What can be exported

An export draws first on generation that would otherwise be curtailed, because moving that energy to
an export costs nothing extra and starts no new plant. Only then does it draw on dispatchable
headroom, started specifically to serve the export, in ascending merit order. Pumped hydro is
excluded from what can be exported entirely, because storage is decided *after* transfer runs for
the interval; by the time storage would have something to offer, the export decision has already
been made.

Conventional Hydro is not excluded, but its exportable headroom is capped to exactly the same
per-interval pace `HydroReservationState` already computed for local dispatch (see
[Dispatch](dispatch.md)). Serving an export from Hydro can substitute for local demand this interval,
but it can never dip into budget that pacing set aside for a future local peak. The export sees
precisely the same allowance local dispatch saw, not a separate or larger one. Hydro's 10% reserve
share is never exportable at all: it is reachable only from the local, post-storage fallback that runs
strictly after transfer has already finished for the interval.

## Loss accounting

`SystemDispatchOutcome.TransmissionLosses` is calculated as `exports − imports`, and every interval
that figure is cross-checked against the loss the transfer solver reports directly from its own flow
decomposition. Those are two independent derivations of the same quantity, reconciled rather than
assumed consistent.

A separate **regional** transmission-loss series exists on published artifacts, built by assigning
each directed link's loss to its receiving region. That is an accounting attribution for readability,
not a measurement of where the loss physically occurred along the link, and it is not a transmission
charge against that region. [Economics](economics.md) explains why transmission is costed once, at
system level, and never split across regions. Only a system-level artifact publishes the underlying
directional forward/reverse link series that attribution is built from.

## Costing

Interconnector cost is charged against its declared route length (`routeLengthKm`, a required field
on each scenario interconnector, alongside its directed capacity) using the `DistancePowerCost` and
`AnnualDistancePowerCost` rates described in [Economics](economics.md). Route length is not derived
from anything else, so it does not depend on which weather file a region happens to be assigned.
One approximation remains: because each corridor is declared as two directed interconnectors, the
same kilometre of conductor is paid for twice, once per direction.
[Limitations §6](../assumptions/limitations.md#6-reciprocal-interconnectors-are-each-costed-at-the-corridors-full-length)
carries the roughly 1.8× cost consequence of that, so this page does not repeat it.

## Next

- [Dispatch](dispatch.md): where transfer sits between generation and storage within an interval.
- [Economics](economics.md): how the capacity and route length described here become an annual cost.
- [Limitations §6](../assumptions/limitations.md#6-reciprocal-interconnectors-are-each-costed-at-the-corridors-full-length)
  and
  [Limitations §7](../assumptions/limitations.md#7-interconnector-losses-are-a-flat-unsourced-placeholder):
  the two places transmission figures will mislead you.
