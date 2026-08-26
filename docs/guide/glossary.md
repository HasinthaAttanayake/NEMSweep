# Glossary

Terms NEMSweep uses without explaining them elsewhere. Where a word has a general meaning and a
narrower one here, the narrower one is what the model means. Terms are marked as framework, CLI or
published-example concepts where the distinction matters.

## Dispatch

**Dispatch.** Deciding, for each interval, which generators run and how much they produce. A
framework concept. NEMSweep dispatches by merit order and does not model bidding, prices or
settlement.

**Merit order.** The order generators are called on in, lowest short-run marginal cost first, ties
broken by technology. Wind, solar and hydro all have zero short-run marginal cost and so tie at the
front; hydro is additionally rationed against a monthly energy budget by a causal pacer, so it does
not spend that budget on whichever intervals come first. Thermal plant follows, ordered by fuel
price times heat rate.

**Curtailment.** Generation that was available but neither delivered to demand nor stored, so it was
spilled. High curtailment usually means the system has more variable generation than it can use in
that interval.

**Unserved energy.** Demand not met by generation, storage discharge or imports. Reported both as
MWh and as a percentage of demand energy, which is what the reliability standard is expressed
against.

**Interval.** One hour. The framework's grid model runs on a fixed one-hour timestep. Every series
in a result is one value per interval, as average MW rather than instantaneous.

## Storage

**Storage sizing.** The search that grows storage until the declared reliability standard is met, or
until it can report why it could not. It grows batteries only; pumped hydro is held at its scenario
capacity.

**State of charge.** Energy held in storage at the beginning of an interval, in MWh.

**Round-trip efficiency.** The fraction of energy put into storage that comes back out. Losses are
taken on charging.

**Sizing outcome.** How the search finished. Every value the artifact can carry:

| Outcome | Meaning |
|---|---|
| `notRequired` | The standard was met without adding any storage. |
| `resized` | Storage was grown and the standard was then met. |
| `energyLimited` | Total generation energy is below demand energy, so no amount of storage would help. |
| `storageNoLongerImprovesReliability` | Every larger battery the search probed left unserved energy essentially unchanged. |
| `batteryCapacityLimitReached` | The search hit the scenario's `maximumPowerMw` or `maximumEnergyMwh` ceiling. |
| `passLimitReached` | The search hit the configured dispatch-pass limit before converging. |

Read this rather than the reliability flag alone. A result can be within target because it never
needed storage, or outside it because the search was capped, and those are different findings.

## Economics

**SLCoE.** System levelised cost of electricity, in AUD per MWh served. Annuitised capital plus
fixed and variable operating cost, divided by energy served. It is the cost of building and running
the system, not a retail bill and not a market price.

**SLCoT.** The transmission equivalent, levelised cost of transmission per MWh served.

**Energy served.** Demand minus unserved energy: the energy the system actually met. The
denominator of every levelised cost, published as `energyServedMwh`. Not the same as **delivered
generation** (`deliveredGenerationMwh`), which is the generation that reached load, after
curtailment and storage charging.

**Cost basis.** The year costs are expressed in, and the real discount rate used to annuitise
capital. Both are scenario inputs, so two runs are only comparable if they share them.

**Annuitisation.** Spreading a capital cost over an asset's technical life at the real discount
rate, so a one-off build cost can be compared against annual operating cost.

## Studies

**Scenario.** One system described in one CLI config file: regions, their demand and weather
artifacts, their generating and storage fleets, and the interconnectors between them. One scenario
produces one result.

**Sweep.** A baseline scenario config plus a set of points, run and published together. Each point
is a free-form override patch on the baseline, and the points sit along one labelled axis. A point
may change any number of inputs.

**Point.** One run within a sweep, identified by a `pointId` and positioned on the sweep's axis by
its `axisValue`.

**Axis value.** The x-axis label for a point. Nothing in the model reads it: it is a display value,
and it is your responsibility to keep it agreeing with what the point's overrides actually change.

**Merge patch.** How a point's overrides are applied to the baseline. Mostly RFC 7386, with two
extensions: some arrays merge by key rather than being replaced, and `$remove` deletes a keyed
element. See [Sweeps](sweeps.md).

## Data

**Input bundle.** A directory of raw upstream sources (AEMO demand archives, an AEMO generation
workbook, EPW weather files) plus a manifest, validated and ingested as a unit.

**Artifact.** A file the CLI writes. Input artifacts are the ingested JSON a scenario reads; output
artifacts are results and sweep files.

**Data root, output root.** The directories a run reads inputs from and writes results to. Neither
is discovered; you supply both. See [the workspace](cli.md#the-workspace).

**Provenance.** What a result records about how it was produced: the SHA-256 of every input byte it
read, and the commit the model was built from. The digest, not the path, is what makes a result
reproducible.

**EPW.** EnergyPlus Weather, the hourly weather file format the model reads solar radiation, wind
speed and temperature from. One representative site per region per role.
