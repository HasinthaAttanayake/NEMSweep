# NEMSweep documentation

This documentation is for analysts and engineers running NEMSweep, or reading results someone else
produced with it. It covers how to run the model, what it assumes, how to design a study, and the
shape of every artifact it writes.

NEMSweep is a deterministic engine for grid dispatch, reliability assessment, storage sizing and
system cost. You describe a set of regions, each with its demand, generation and storage, and a
reliability standard. The engine dispatches them in merit order for every hour of the modelled
period, grows battery storage in the regions that miss the standard, and reports the technical and
economic result: what was generated, what was curtailed, what was left unserved, how much battery
capacity the system needed, and the system levelised cost of electricity (SLCoE) in AUD per MWh.

## The three layers

Read every page on this site against one distinction. A statement about NEMSweep is about exactly
one of these three layers, and mistaking one for another is the most common way to misread a result.

| Layer | What it is | Where it lives |
|---|---|---|
| Framework | The dispatch, reliability, storage-sizing and cost engine. No hardcoded region list and no AEMO coupling: region identifiers are free-form strings. Fixed one-hour timestep. The market-time offset is a run parameter, read from the scenario period and defaulting to UTC+10. Deterministic, no package dependencies, embeddable. | `NEMSweep.Model`, `NEMSweep.Contracts` |
| NEM scoping | The command-line tool that binds the framework to Australia. It ingests AEMO operational demand, EnergyPlus Weather data and AEMO generation data, and validates scenarios against the five National Electricity Market regions (NSW1, QLD1, SA1, TAS1, VIC1). | `NEMSweep.CLI` |
| Published example | One scenario: the National Electricity Market configured for the 2026 financial year, plus a sweep that adds data-centre load across the regions. Everything on the [results site](https://www.nemsweep.com/) is this one example. | the repository artifacts |

The five NEM regions are the CLI's constraint, enforced because the data it ingests is National
Electricity Market data. The one-hour timestep is the framework's, fixed in `NEMSweep.Model`; the
market-time offset is a run parameter it reads from the scenario period, and the CLI takes that from
the ingested data, so a NEM run works in UTC+10. Sub-hourly demand input is resampled to the hourly
timestep during ingestion.

## Where to start

| If you want to | Go to |
|---|---|
| Install NEMSweep and run your first scenario | [Guide](guide/index.md) |
| Understand what the model does | [Concepts](concepts/index.md) |
| Know what the model assumes, and where it will mislead you | [Assumptions](assumptions/index.md) |
| Design a study, or drive NEMSweep with an LLM | [Exploring](exploring/index.md) |
| Look up a type, a field, or a published JSON schema | [API reference](api/index.md) |

## What the framework does

Given a realised system and a reliability standard, the framework dispatches generation in merit
order by short-run marginal cost for every hour of the period, meters flow and loss on each directed
interconnector, grows battery storage in the regions that miss the standard, and prices the build
and operation of the resulting system. [Concepts](concepts/index.md) covers each stage.

The dispatch method is fixed. The flexibility is in the system you describe, not in the modelling
approach. Because the engine is deterministic and every assumption is either a scenario input or a
constant listed in the [assumptions register](assumptions/index.md), one method answers many
questions:

- **Generation mix.** Replace coal with wind, or wind with solar, and read the storage and
  curtailment consequences.
- **Economics.** Vary the discount rate, capital costs, fuel prices or technical lives and see which
  conclusions survive and which were artefacts of one cost assumption.
- **Reliability.** Set a stricter or looser standard and read its cost, in battery capacity and in
  dollars.
- **A sweep.** Run a baseline scenario against a set of override patches lined up along one labelled
  axis, and publish every run with its provenance, so the shape of the response is inspectable
  rather than asserted.

[Exploring the scenario space](exploring/index.md) covers how to design those studies, including how
to hand the machine-readable schemas to an LLM and have it generate sweeps for you.

## What the framework does not do

NEMSweep is a system-planning model. It is not a market simulator, not a power-flow model, and not a
forecast.

- It does not model bidding, prices, settlement, or any market behaviour. Generation is dispatched
  in merit order by short-run marginal cost, with no unit commitment and no
  security-constrained economic dispatch.
- The cost it reports covers building and running the system. It is not a retail bill, and SLCoE is
  not a price anyone pays.
- A run has no stochastic draws, so it reports a realised outcome rather than an expectation over a
  distribution. The trustworthy output is the gap between two scenarios. Any single level carries
  the model's assumptions with it.

These are load-bearing limitations, not disclaimers. Read [Limitations](assumptions/limitations.md)
before you quote a figure from NEMSweep.

## Determinism and provenance

The same inputs at the same commit reproduce every modelled value. Every result records the SHA-256
digest of the exact input bytes it was built from, and that digest, not the file path, is the
reproducibility boundary. The model constants a scenario cannot override are listed in the
[assumptions register](assumptions/index.md), which a test suite checks against the code on every
change.

Determinism is what makes a comparison attributable: if a number moves between two runs, an input
you changed moved it, because there is no run-to-run noise for the effect to hide in.

Dispatch artifacts are not byte-identical between reruns, because each run stamps a fresh `runId`
that identifies the run rather than describing its contents. [Outputs and
provenance](guide/outputs.md) sets out exactly what differs.

## The published example

The [results site](https://www.nemsweep.com/) publishes one worked example, not a dataset and not a
forecast:

- a baseline scenario, `scenarios/nem-fy2026-all-regions.json`, dispatching all five National
  Electricity Market regions together over directed interconnectors, built from AEMO operational
  demand for the 2026 financial year, a typical-meteorological-year weather profile, and a declared
  generation and storage fleet;
- a sweep, `sweeps/datacentre-nameplate-fy2026.json`, that holds that baseline fixed and adds
  data-centre nameplate load across the regions, one run per step.

The example demonstrates what the framework does. It is not the limit of what the framework does.
Its artifacts derive from AEMO and EnergyPlus Weather sources under their own terms. Run your own
scenario before quoting a figure from it.
