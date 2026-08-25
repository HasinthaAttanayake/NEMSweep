# Glossary

Terms NEMSweep uses without explaining them elsewhere. Where a word has a general meaning and a
narrower one here, the narrower one is what the model means.

## Dispatch

**Dispatch.** Deciding, for each hour, which generators run and how much they produce. NEMSweep
dispatches by merit order and does not model bidding, prices or settlement.

**Merit order.** The order generators are called on in, cheapest short-run marginal cost first.
Wind and solar come first because their marginal cost is zero, then hydro against its energy budget,
then thermal plant by fuel cost times heat rate.

**Curtailment.** Generation that was available but neither delivered to demand nor stored, so it was
spilled. High curtailment usually means the system has more variable generation than it can use at
that hour.

**Unserved energy.** Demand not met by generation, storage discharge or imports. Reported both as
MWh and as a percentage of demand, which is what the reliability standard is expressed against.

**Interval.** One hour. Every series in a result is one value per interval, and interval values are
average MW rather than instantaneous.

## Storage

**Storage sizing.** The search that grows storage until the declared reliability standard is met, or
until it can report why it could not. It grows batteries only; pumped hydro is held at its scenario
capacity.

**State of charge.** Energy held in storage at the beginning of an interval, in MWh.

**Round-trip efficiency.** The fraction of energy put into storage that comes back out. Losses are
taken on charging.

**Sizing outcome.** How the search finished. The values that matter when reading a result:

| Outcome | Meaning |
|---|---|
| `notRequired` | The standard was met without adding any storage. |
| `resized` | Storage was grown and the standard was then met. |
| `batteryCapacityLimitReached` | The search hit the scenario's `maximumPowerMw` or `maximumEnergyMwh` ceiling. |
| `energyLimited` | Total generation energy is below demand energy, so no amount of storage would help. |
| `passBudgetExhausted` | The search ran out of passes before converging. |

Read this rather than the reliability flag alone. A result can be within target because it never
needed storage, or outside it because the search was capped, and those are different findings.

## Economics

**SLCOE.** System levelised cost of energy, in AUD per MWh **served**. Annuitised capital plus fixed
and variable operating cost, divided by energy served. It is the cost of building and running the
system, not a retail bill and not a market price.

**SLCOT.** The transmission equivalent, levelised cost of transmission per MWh served.

**Cost basis.** The year costs are expressed in, and the real discount rate used to annuitise
capital. Both are scenario inputs, so two runs are only comparable if they share them.

**Annuitisation.** Spreading a capital cost over an asset's technical life at the real discount
rate, so a one-off build cost can be compared against annual operating cost.

## Studies

**Scenario.** One system described in one file: regions, their demand and weather artifacts, their
generating and storage fleets, and the interconnectors between them. One scenario produces one
result.

**Sweep.** A series of runs that differ in one controlled way. Each point is the baseline scenario
with a small set of overrides applied, so everything except the varied input is held constant.

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
