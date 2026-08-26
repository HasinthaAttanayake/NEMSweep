# Limitations

Read this before you quote a number from NEMSweep.

These are not disclaimers. Each one changes how a result should be read, and in most cases we can
say which direction the error runs. They are stated first, ahead of the assumptions register,
because a reader who takes only one page from this site should take this one.

## The four that matter most

If you read nothing else on this page, read these four. Each links to the section that works it
through.

| Limitation | What it does to a result |
|---|---|
| [1. A deterministic run cannot honour an expectation-based standard](#1-a-deterministic-run-cannot-honour-an-expected-unserved-energy-standard) | **Storage is understated.** The standard is an expectation across many weather outcomes; one typical year is a realisation, and it excludes the tail events that drive reliability. Treat a sizing result as a lower bound. |
| [2. The 82% renewable target is measured on a different basis](#2-the-82-renewable-target-is-not-the-same-target-on-a-grid-scale-basis) | **Not directly comparable.** Operational demand nets out rooftop PV before the model sees it, and no rooftop fleet is dispatched. Two shares are published, on grid-scale and native bases; neither is "the" number. |
| [3. This is the cost of building the system, not a power bill](#3-this-is-the-cost-of-building-the-system-not-a-power-bill) | **Not a price.** The figures are annuitised build-and-run cost per MWh served. No distribution, retail, market, environmental scheme or tax costs are modelled. |
| [4. Greedy storage dispatch is wrong in both directions](#4-greedy-storage-dispatch-is-wrong-in-both-directions) | **Biased both ways.** With no foresight, storage undersizes against a multi-day wind drought and oversizes against a system able to arbitrage. Which dominates depends on the scenario. |

Four more follow, on sizing, transmission distance, interconnector losses, and what is not modelled
at all.

## 1. A deterministic run cannot honour an expected-unserved-energy standard

The reliability standard NEMSweep sizes against, 0.002% of demand energy unserved, is an
**expectation across a distribution** of weather and demand outcomes. NEMSweep runs a single weather
year and produces a **realised** figure from it.

These are different quantities. Reliability is driven by tail events: the still, cold week; the
wind drought that coincides with a heat wave. A typical meteorological year is assembled from
representative months and, by construction, contains few of them.

**Direction of the error: storage is understated.** A system sized to meet 0.002% USE against one
typical year will need more storage than that to meet 0.002% USE *in expectation*. Treat a sizing
result as a lower bound on required capacity, not an estimate of it.

Doing this properly means running many weather years, or synthetic years drawn from a fitted
distribution, and taking the expectation across them. NEMSweep does not do that today.

## 2. The 82% renewable target is not the same target on a grid-scale basis

Australia's 82% renewable generation target is commonly quoted against a basis that includes
rooftop solar. NEMSweep dispatches **operational demand**, which is demand as seen by the grid *after*
rooftop PV has already served part of it. Rooftop generation is netted out of the demand series
before the model ever sees it, and no rooftop fleet is dispatched.

So a renewable share measured on what NEMSweep dispatches is not directly comparable to the headline
target. NEMSweep therefore reports **two** shares, and neither is presented as "the" number:

| Measure | Numerator | Denominator |
|---|---|---|
| Grid-scale renewable share | Delivered Solar + Wind + Hydro | Total delivered generation |
| Native renewable share | Delivered Solar + Wind | Base demand energy, excluding additive demand components |

If you are comparing against a published target, check which basis that target uses. Comparing a
grid-scale share against a target that counts rooftop will understate progress; the reverse will
overstate it.

## 3. This is the cost of building the system, not a power bill

The levelised figures NEMSweep publishes are **system build-and-run costs per MWh served**:
annuitised capital plus one year of operating cost for the generation, storage and transmission
assets in the scenario, divided by the energy actually served.

A retail electricity bill is a different thing entirely. It also contains distribution network
charges below the transmission level, retail operating costs and margin, market and settlement
costs, environmental scheme costs, metering, and taxes. None of those are modelled.

Do not read a NEMSweep AUD/MWh figure as a price, a tariff, or a forecast of what anyone will pay.
It is an engineering-economic cost of supply, and it is a modelled estimate rather than an audited
one.

## 4. Greedy storage dispatch is wrong in both directions

NEMSweep's storage policy has **no foresight**. It sees the current interval only: the residual after
generation has met demand, and each fleet's headroom. It cannot pre-charge in anticipation of
tomorrow's shortfall because no forward view of residual demand is provided to it.

That cuts two ways, and which way dominates depends on the scenario:

- **Against a multi-day wind drought, it undersizes.** A policy that discharges whenever there is a
  deficit will spend its stored energy on the first day of a lull rather than rationing it across
  three. Real operators, seeing a forecast, would hold back. Storage sized against greedy dispatch
  is therefore sized against a worse operator than the real one.
- **Against a system able to arbitrage, it oversizes.** A greedy policy charges only from surplus
  or from incremental thermal generation in the current interval. It never charges cheaply now to
  displace something expensive later. A system that could arbitrage would extract more value from
  the same megawatt-hours, and would need fewer of them.

The same absence of foresight is what makes the model deterministic and cheap to run, which is what
makes sweeps practical. It is a deliberate trade, but it is a real limitation.

## 5. Sizing finds a near-frontier point, not an optimum

The storage sizing search grows Battery capacity until every region is inside the reliability
target, then refines each changed region's power and energy coordinate by coordinate. The result is
a **deterministic coordinate-wise near-frontier point**.

It is not a global minimum, and it is emphatically not cost-optimal: nothing in the search prices
the capacity it adds. A different search order could land on a different but equally compliant
point. Two sizing results are comparable to each other because the procedure is deterministic;
neither is "the answer".

A sizing run that does not meet the target reports which bound stopped it. Only the
`energyLimited` outcome, where total available generation energy across the system is below total
demand energy, proves that no Battery could have met the target. A capacity ceiling, a pass budget,
or probes that stopped helping each report a limit of the search rather than a limit of the
system.

The same applies to inter-regional transfer, which serves regions in priority order by successive
max-flow solves. That guarantees a higher-priority region is never starved by a lower-priority one,
and for the same reason is deliberately not a global optimum.

## 6. Reciprocal interconnectors are each costed at the corridor's full length

Every corridor that carries flow both ways is declared as two directed interconnectors (see
[Scenario configuration](../guide/scenarios.md#interconnectors)), and each is costed independently
over the corridor's full route length, so the same kilometre of conductor is paid for twice.
Charging each of the five physical corridors once at its larger directed rating gives
1,955,280 km·MW against the 3,461,890 km·MW actually charged across all ten directed links. This
convention alone accounts for roughly **1.8×** of reported transmission cost. It is kept
deliberately: attributing a shared asset's cost to one direction rather than the other would need a
convention of its own, and this one is at least uniform.

Route length itself is a declared field on each scenario interconnector (`routeLengthKm`), not
derived from anything else. It used to be the great-circle distance between the endpoint regions'
solar weather sites, which ran long against the real NEM corridors and meant swapping a region's
solar weather file silently changed transmission cost. The committed scenario now carries
approximate real corridor distances instead (Heywood ≈275 km, VNI ≈300 km, the QNI corridor
≈500 km, Basslink ≈370 km, EnergyConnect ≈900 km); a more precise per-route figure can replace any
of them without a code change.

## 7. Interconnector losses are a flat unsourced placeholder

Every hop of an inter-regional transfer loses a flat 5%, so a two-hop route delivers 0.95². That
figure is a placeholder. AEMO publishes marginal loss factors per interconnector and they are
neither flat nor equal across links; a sourced value should replace this one.

Any result that depends materially on inter-regional transfer inherits that uncertainty.

## 8. What is not modelled at all

Stated so their absence is not mistaken for a zero:

- **No market.** No bidding, no offers, no spot price, no settlement. Generation is dispatched in
  merit order by short-run marginal cost.
- **No unit commitment.** No minimum stable generation, no start-up cost, no ramp rate, no minimum
  up or down time. A fleet can go from zero to full output in one hour.
- **No forced outages or maintenance.** Every fleet is available at nameplate whenever its resource
  allows.
- **No intra-regional network.** A region is a single node. Distribution and sub-transmission
  constraints do not exist.
- **No frequency control, inertia, or system strength.** Reliability here is an energy measure only.
- **One year, one weather year, hourly.** No multi-year build path, no retirement schedule, no
  capacity expansion over time.
- **No demand response or price elasticity.** Demand is exogenous and inelastic.

## Reading a result honestly

The safest use of NEMSweep is **comparative**. Because the model is deterministic and every
assumption is either a documented constant or a scenario input, the *difference* between two runs
is trustworthy even where the absolute level is not: the same biases sit on both sides and largely
cancel.

That is what makes sweeps the natural unit of work here, as
[Sensitivity analysis](../exploring/sensitivity-analysis.md) works through.

## Next

- [Model assumptions](index.md): the constants baked into the model, with values and sources.
- [Scenario parameters](scenario-parameters.md): the values you supply, which NEMSweep makes no
  claim about.
