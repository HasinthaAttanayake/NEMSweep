# NEMSweep

NEMSweep is an hourly grid dispatch model for Australia's National Electricity Market.

You describe a set of regions, each with its demand, its generation assets and its storage, and
NEMSweep dispatches them hour by hour across a modelled year. Where the reliability standard you
declared is not met, it grows storage until either the standard is met or the search reports what
stopped it. The result is a set of technical and economic figures: what was generated, what was
curtailed, what was left unserved, how much storage the system needed, and what the whole thing
costs per MWh served.

The model is **deterministic**. The same inputs at the same commit produce identical results, every
modelled value to the last decimal place, and every published result carries the SHA-256 of the
exact input bytes it was built from. Dispatch artifacts are not quite byte-identical across reruns,
because each run stamps a fresh `runId`, which identifies the run rather than describing its
contents.

That is the property the rest of this site is built around: if a number moves, something you
changed moved it.

## Where to start

| If you want to… | Go to |
|---|---|
| Install it and run your first scenario | [Guide](guide/index.md) |
| Understand what the model actually does | [Concepts](concepts/index.md) |
| Know what it assumes, and where it will mislead you | [Assumptions](assumptions/index.md) |
| Design a study, or drive it with an LLM | [Exploring](exploring/index.md) |
| Look up a type, a field, or a published JSON schema | [API reference](api/index.md) |

## What it is for

The published work so far uses NEMSweep for one question: what happens to system cost and reliability
as large new loads are added to the grid, and at what point does new storage become necessary. The
published study frames those loads as data centres, but any load increase behaves the same way. You
can see the results on the [results site](https://nemsweep.pages.dev/).

That is one slice of a much larger space. Because the model is deterministic and every assumption
is either a scenario input or a documented constant, the same framework answers questions it was
never specifically built for:

- **Generation mix.** Replace coal with wind, or wind with solar, and read the storage and
  curtailment consequences rather than guessing at them.
- **Economics.** Vary the discount rate, capital costs, fuel prices or technical lives and watch
  which conclusions survive and which were artefacts of one cost assumption.
- **Reliability.** Ask what a stricter or looser standard costs, in storage and in dollars.
- **Sensitivity analysis.** Sweep any single input across a range and publish every run with its
  provenance, so the shape of the response is inspectable rather than asserted.

[Exploring the scenario space](exploring/index.md) covers how to design those studies, including
how to hand the machine-readable schemas to an LLM and have it generate sweeps for you.

## What it is not

NEMSweep is a system-planning model, not a market simulator and not a forecast.

- It does not model bidding, prices, settlement, or any market behaviour. Generation is dispatched
  in merit order by short-run marginal cost.
- The cost it reports is the cost of **building and running the system**, not a retail bill.
- It models a single year against a single weather year, with no stochastic draws, so it reports a
  realised outcome rather than an expectation over a distribution.

Those are load-bearing limitations, not disclaimers. Read
[Limitations](assumptions/limitations.md) before you quote a number from it.

## Project status

NEMSweep is being built in public, one validated layer at a time. The
[results site](https://nemsweep.pages.dev/) shows the current state of that work, and the
[repository](https://github.com/HasinthaAttanayake/NEMSweep) carries the model, the CLI that produces
the artifacts, and the committed inputs those artifacts were built from.
