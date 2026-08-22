# Designing a study

NemSim's published work so far asks one question: what does adding a large new load to the grid do
to storage requirements, reliability and system cost. That is a narrow use of a general instrument.

The model is deterministic, every assumption is either a documented constant or a scenario input,
and a run takes minutes rather than hours. Those three properties together mean the natural unit of
work is not a run — it is a **study**: a set of runs that differ in one controlled way, published
together with their provenance.

This section is about designing those.

## The determinism contract

Everything here rests on one guarantee: **the same inputs at the same commit produce identical
results.** There is no random number generator in the model. Rerunning a scenario reproduces every
modelled value exactly; the only field that moves is the `runId` stamped on the artifact, which
identifies the run rather than describing it.

That is why a sweep is meaningful. When you vary one input across twenty points and the storage
requirement moves, the input moved it — there is no run-to-run noise for the effect to hide in and
no need to average anything.

It also means a result is checkable. A sweep records the git commit SHA, whether the working tree
was dirty when it ran, and the SHA-256 of every input file it consumed. Anyone with the repository
can reproduce your numbers exactly, or show that they cannot.

The corollary is that the model reports a **realisation**, not an expectation. It has no
distribution to sample and gives no confidence intervals. See
[Limitations §1](../assumptions/limitations.md#1-a-deterministic-run-cannot-honour-an-expected-unserved-energy-standard).

## Comparative, not absolute

The single most useful habit when working with NemSim: **read differences, not levels**.

The model's biases are systematic. Transmission route length runs long. Greedy storage dispatch is
wrong in a known direction. A typical meteorological year understates tail events. Every one of
those sits on *both* runs when you compare two, and largely cancels.

So a claim of the form "storage requirement roughly triples between +2 GW and +6 GW of new load" is
far better supported than "the system needs 14 GWh of storage". Frame findings the first way.

## What is worth varying

Anything in a scenario config can be swept. These are the axes that repay the effort.

### Demand

The published study. Add load and watch where the system stops absorbing it.

Vary `dataCentreNameplateMw` per region. Despite its name it is just a flat, always-on load
increase — use it for electrification, industrial load, anything without a distinct shape. Watch for
the point at which sizing switches from `NotRequired` to `Resized`, and later to
`BatteryCapacityLimitReached` or `EnergyLimited`. Those transitions are the interesting part; the
smooth stretches between them are not.

### Generation mix

Vary `nameplateCapacityMw` across fleets — more wind against less solar, retiring coal, adding gas.

The revealing outputs are curtailment and storage, not cost. A mix that looks cheap on capacity
alone often turns out to need storage that the capacity comparison never showed, because solar and
wind fail at different times of day and year.

Note that removing a fleet entirely and setting its capacity to zero are not the same thing: a
zero-capacity *storage* plan is how you tell the sizing loop "build whatever this needs, at these
economics".

### Economics

Vary `costBasis.realDiscountRate`, capital costs, fuel prices, or technical lives.

Discount rate first, always. It applies to every capital cost in the system and its effect is
asymmetric — raising it favours low-capital, high-fuel plant; lowering it favours renewables and
storage. A conclusion that flips between 5% and 9% was never a conclusion about technology.

Fuel price and heat rate are different in kind from capital cost: together they set short-run
marginal cost, so changing them changes **merit order** and therefore what is actually generated,
not merely what the same dispatch costs.

### Reliability standard

Vary `storageSizing.targetUsePercentage` to price the standard itself.

Expect a sharply non-linear curve. The last increment of reliability is bought against the rarest
events, so it is bought with the most storage. This is one of the more policy-relevant sweeps the
model supports, and one of the least often run.

### Transmission

Vary interconnector `capacityMw`, or add and remove links entirely.

Interconnection and storage are substitutes: both move energy from where and when it is abundant to
where and when it is not. Sweeping one while the sizing loop solves for the other shows the
trade-off directly. Read [Limitations §6](../assumptions/limitations.md#6-transmission-route-length-is-a-proxy-and-it-runs-long)
before drawing a cost conclusion from it.

## Designing a good sweep

**One axis.** If two things change between points you cannot attribute the difference. That is what
a sweep is *for*.

**Points where the interesting thing happens.** A linear ladder wastes runs on the flat stretches.
Run a coarse sweep first, find the region where behaviour changes, then sweep finely through it.

**Include a baseline.** A zero-change point makes every other point readable as a delta and catches
the case where your overrides did nothing at all.

**Push until it breaks.** Extend the axis past the point where sizing stops succeeding. The outcome
codes — `EnergyLimited`, `StorageNoLongerImprovesReliability`, `BatteryCapacityLimitReached` — are
findings, not failures, and the value at which one first appears is often the headline.

**Make `axisValue` honest.** It is a display value only; nothing in the model reads it. A sweep
whose `axisValue` disagrees with its `overrides` runs happily and produces a chart that lies. See
[Sweeps](../guide/sweeps.md).

## Reading the result

A sweep publishes 19 scalars per point per region — levelised costs, energy served, renewable
shares, storage capacity, unserved energy, curtailment, net imports. They are listed in
[Outputs and provenance](../guide/outputs.md).

Look for three things:

1. **Where the curve bends.** Smooth response means the system is absorbing the change with what it
   already has. A bend means something became binding.
2. **Which component moved.** Total levelised cost rising tells you little; generation cost rising
   while storage cost is flat tells you a great deal.
3. **What the sizing outcome says.** A point that met its target with no new storage and a point
   that hit the capacity limit are qualitatively different results, even if their costs look
   similar.

[Sensitivity analysis](sensitivity-analysis.md) works a real example through.

## Next

- [Driving NemSim with an LLM](llm-workflow.md) — hand the machine-readable schemas to a model and
  have it generate sweeps for you.
- [Sensitivity analysis](sensitivity-analysis.md) — a worked example on the committed sweep.
- [Sweeps](../guide/sweeps.md) — the mechanics of defining and running one.
