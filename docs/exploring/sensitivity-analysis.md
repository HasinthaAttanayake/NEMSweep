# Sensitivity analysis

A worked reading of the sweep committed to this repository, `datacentre-nameplate-fy2026`. The
point is not the result but the method: what to look at, in what order, and where the traps are.

Figures below come from `NEM.Web/wwwroot/data/sweeps/datacentre-nameplate-fy2026/index.json` as
published. Rerunning the sweep at a different commit will move them.

## The sweep

One axis: additional always-on load, distributed across the five NEM regions, from 0 MW to
12,000 MW over 25 points. Everything else is held constant: the same generation fleets, the same
interconnectors, the same economics, and the same weather year (FY2026, typical meteorological
year).

Storage sizing runs at every point. That is what makes this a study rather than 25 unrelated runs:
each point answers "what would this system need to hold the reliability standard at this load?"

## Step 1: find where the sizing outcome changes

Before looking at a single cost figure, read the outcome column. It partitions the sweep into
regimes, and a cost comparison across a regime boundary is usually meaningless.

| Points | Added load | Outcome | Meaning |
|---|---|---|---|
| p0 | 0 MW | `notRequired` | Existing storage already holds the standard |
| p1–p9 | 500–4,500 MW | `resized` | Standard held, by building more battery |
| p10–p24 | 5,000–12,000 MW | `storageNoLongerImprovesReliability` | Standard **not** held; more battery stopped helping |

Two boundaries, and the second is the headline. Somewhere between +4,500 MW and +5,000 MW this
system stops being a storage problem.

`storageNoLongerImprovesReliability` means every feasible larger power, energy and combined probe
failed to materially reduce unserved energy. It identifies solver stagnation and does **not**
diagnose the cause: it could be generation timing, or storage policy, or the search itself. Worth
knowing: it is not `energyLimited`, so total available generation energy still exceeded total demand
energy. The energy exists; the system cannot get it to the right hours.

## Step 2: read reliability, and stop reading anything else once it fails

| Point | Added load | USE % of demand | Within target? |
|---|---|---|---|
| p0 | 0 MW | 0 | yes |
| p5 | 2,500 MW | 0.0013% | yes |
| p9 | 4,500 MW | 0.0014% | yes |
| **p10** | **5,000 MW** | **0.1506%** | **no** |
| p15 | 7,500 MW | 1.06% | no |
| p24 | 12,000 MW | 5.93% | no |

The reliability target is 0.002% of demand energy. Through p9 the system holds it with margin. At
p10 unserved energy jumps by two orders of magnitude, from 0.0014% to 0.1506%, and then climbs
steadily to 5.93%.

That is a cliff, not a slope. It is the single most important feature of this sweep, and it is
invisible in any of the cost scalars.

**Everything past p10 is a different kind of result.** Those points describe a system that is
failing, and their cost figures are cost per MWh *served* by a system not serving the energy. Do not
compare them with the compliant points.

## Step 3: look at what storage did

| Point | Added load | Battery MW | Battery MWh |
|---|---|---|---|
| p0 | 0 MW | 5,624 | 14,387 |
| p3 | 1,500 MW | 5,624 | 20,304 |
| p7 | 3,500 MW | 7,784 | 46,023 |
| **p8** | **4,000 MW** | **7,849** | **85,212** |
| p9 | 4,500 MW | 9,325 | 97,641 |
| p10+ | 5,000 MW and above | 12,234 | 108,870 |

Two things to notice.

**The response is strongly non-linear.** The first 1,500 MW of load adds about 6 GWh of storage.
The step from p7 to p8, which is 500 MW of load, adds 39 GWh and nearly doubles the fleet. Storage
requirement is not proportional to load; it is driven by how the new load interacts with the
existing generation shape, and there is a point where the existing shape stops covering it.

**Capacity plateaus once sizing stops succeeding.** From p10 onward, 12,234 MW / 108,870 MWh is
where the search stopped, not what the system needs. Reading it as a requirement would be wrong.

## Step 4: decompose the cost before reading the total

Total levelised cost is the scalar everyone reaches for first, and on its own it is misleading here.

| Point | Added load | System | Generation | Storage | Transmission |
|---|---|---|---|---|---|
| p0 | 0 MW | 154.49 | 137.33 | 5.16 | 12.00 |
| p3 | 1,500 MW | 149.26 | 132.53 | 5.52 | 11.21 |
| p7 | 3,500 MW | 146.40 | 127.64 | 8.46 | 10.31 |
| p9 | 4,500 MW | 149.48 | 125.65 | 13.92 | 9.91 |

All AUD/MWh served.

System levelised cost **falls** as load is added, from 154.49 to 146.40 by p7. That is real, and
it is not a modelling artefact: a flat always-on load raises the system load factor, and the fixed
costs of generation and transmission are then spread over more energy served. Generation levelised
cost falls monotonically from 137.33 to 125.65; transmission from 12.00 to 9.91.

But the storage component moves the other way, from 5.16 to 13.92, and by p8 it is rising faster
than generation cost is falling. The system total turns around between p7 and p8, the same place
storage capacity nearly doubled.

**That turning point is the finding.** "Adding load reduces levelised cost" is true up to about
+3,500 MW on this system and false beyond it, and the total alone would not have told you why.

## Step 5: check the second-order series

Two more scalars carry information the headline figures do not.

**Curtailment falls monotonically**, from 8.89 TWh at p0 to 0.94 TWh at p24. The added load is
partly absorbing renewable generation that was previously spilled. That is most of the mechanism
behind the falling generation cost, and it is why the early points look favourable: part of the
added load is served from energy that was already being generated and then spilled, so total annual
cost rises proportionally less than served energy does.

Be careful what that supports. Falling AUD/MWh is an **average**, and an average falls whenever
fixed costs are spread over more energy, whether or not the added load is cheap to serve. Nothing
in this section computes a marginal cost, which would be the change in total annual cost divided by
the change in energy served between two adjacent points. Read these scalars as "average cost falls
through p7", not as "the added load is nearly free".

**The two renewable shares move in opposite directions**, which is worth dwelling on:

| Point | Added load | Grid-scale share | Native share |
|---|---|---|---|
| p0 | 0 MW | 40.6% | 34.8% |
| p12 | 6,000 MW | 34.6% | 37.8% |
| p24 | 12,000 MW | 31.3% | 39.1% |

Grid-scale renewable share is delivered Solar + Wind + Hydro over *total delivered generation*: as
thermal plant ramps up to serve the new load, the renewable fraction of generation falls. Native
renewable share is delivered Solar + Wind over *base demand energy*, which excludes the added load:
as curtailment falls, more of the existing renewable fleet reaches load, so it rises.

Both are correct. They answer different questions. Quoting one without saying which basis it uses
is exactly the error described in
[Limitations §2](../assumptions/limitations.md#2-the-82-renewable-target-is-not-the-same-target-on-a-grid-scale-basis).

## What this sweep supports, and what it does not

**Supported:**

- On this system, with these assumptions, added always-on load first *lowers* system levelised cost
  by improving load factor and absorbing curtailment.
- That effect reverses at roughly +3,500 to +4,000 MW, when storage cost growth overtakes it.
- Somewhere between +4,500 MW and +5,000 MW the system stops being able to hold the reliability
  standard with any battery the search could find.
- The storage response is sharply non-linear and concentrated near that boundary.

**Not supported:**

- Any absolute AUD/MWh figure as a cost of electricity. See
  [Limitations §3](../assumptions/limitations.md#3-this-is-the-cost-of-building-the-system-not-a-power-bill).
- The marginal cost of the added load. That would need the change in total annual cost divided by
  the change in energy served, which this sweep publishes the ingredients for but does not
  calculate.
- The storage capacities as requirements. A single weather year understates them, per
  [Limitations §1](../assumptions/limitations.md#1-a-deterministic-run-cannot-honour-an-expected-unserved-energy-standard),
  and greedy dispatch biases them in a second, separate direction, per
  [Limitations §4](../assumptions/limitations.md#4-greedy-storage-dispatch-is-wrong-in-both-directions).
- The exact breakpoint. It sits between two sample points 500 MW apart, and it will move with
  weather year, generation mix and storage economics.
- Anything beyond +12,000 MW. Run the point.

## The habits worth taking away

1. **Read the sizing outcome before the numbers.** It tells you which points are comparable.
2. **Read reliability second.** Once the standard fails, the cost figures describe a different
   system.
3. **Decompose before totalling.** A flat total often hides two components moving in opposite
   directions, and the crossover is usually the finding.
4. **Look for the bend, not the level.** Where the response changes shape is where something became
   binding.
5. **Sweep past the failure.** The value at which an outcome code first appears is frequently the
   most useful number in the study.
6. **Say which basis.** Especially for renewable share, where two defensible measures move in
   opposite directions on the same run.

## Next

- [Designing a study](index.md): what else is worth varying.
- [Driving NemSim with an LLM](llm-workflow.md): generating sweeps and reading them back.
- [Limitations](../assumptions/limitations.md): required before quoting any of this.
