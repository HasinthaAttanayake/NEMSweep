# Scenario parameters

Everything on this page is a value **you** supply in a scenario configuration. NEMSweep does not
assert any of it. It takes what you give it, dispatches accordingly, and reports the consequence.

That distinction matters. The constants in [Model assumptions](index.md) are the model's claims and
we defend them there. The values below are the *user's* claims, and the committed
`scenarios/nem-fy2026-all-regions.json` is one person's set of them: a starting point to argue
with, not a reference case to cite.

For the exact field names, types and validation rules, see
[Scenario configuration](../guide/scenarios.md). This page is about what the values *mean* and how
sensitive results are to each.

## Why we are not publishing a sourced cost deck

A costing deck that claims authority invites a reader to accept the output without examining the
input. NEMSweep's whole proposition is the opposite: change a number, rerun, and see what moves.

So the committed scenario carries no citations, and none of its economic values should be treated
as ours. If you need defensible numbers, bring your own. CSIRO's GenCost and AEMO's Integrated
System Plan inputs are the usual Australian starting points. Then sweep them, because the point is
not to find the right value but to learn which conclusions depend on it.

## Cost basis

| Parameter | Unit | What it means |
|---|---|---|
| `costBasis.year` | year | The real-dollar year every cost in the scenario is stated in. Purely a label: no deflator is applied, so all your inputs must already be in this year's dollars. |
| `costBasis.realDiscountRate` | fraction | The **real** discount rate used to annuitise capital. 0.07 is 7%. Real, not nominal: applying an inflation-inclusive rate to real-dollar costs double-counts inflation. |

**Sensitivity: high, and asymmetric.** The discount rate is applied to every capital cost in the
system, and capital-heavy technologies feel it hardest. Raising it favours low-capital,
high-fuel-cost plant; lowering it favours renewables and storage. If you sweep one economic
parameter, sweep this one. A conclusion that flips between 5% and 9% was never a conclusion about
the technology.

## Generation fleets

| Parameter | Unit | What it means |
|---|---|---|
| `nameplateCapacityMw` | MW | Installed capacity. For Solar and Wind this scales the resource trace; for dispatchable plant it is the per-interval ceiling. |
| `costParameters.capitalCostAudPerMw` | AUD/MW | One-off build cost, annuitised over the technical life at the real discount rate. |
| `costParameters.fixedOperatingCostAudPerMwYear` | AUD/MW/year | Charged on installed capacity whether or not the plant runs. |
| `costParameters.variableOperatingCostAudPerMwh` | AUD/MWh generated | Charged on **gross** generation, including generation used to charge storage. |
| `costParameters.fuelPriceAudPerGj` | AUD/GJ | Multiplied by heat rate to give a fuel cost per MWh generated. |
| `technologyProfile.heatRateGjPerMwh` | GJ/MWh | Thermal energy consumed per MWh **generated**, on the same gross basis as variable operating cost. With fuel price this sets the fleet's short-run marginal cost, which is what decides merit order. |
| `technologyProfile.technicalLifeYears` | years | Annuitisation period for capital cost. |
| `technologyProfile.emissionsIntensityTonnesPerMwh` | t CO2-e/MWh | Operational carbon dioxide equivalent released per MWh **generated**, on the same gross basis as fuel. Combustion only: it excludes fuel extraction, construction and decommissioning, so it is not a life-cycle figure. Zero for a fleet that emits nothing when it runs; there is no technology-name default, so a non-emitting fleet still states it. The shipped NEM scenario derives each value as heat rate multiplied by a fuel combustion factor (about 90.2 kg CO2-e/GJ for black coal, 93.0 for Victorian brown coal, 51.4 for natural gas), giving 0.771 for black coal, 1.054 for brown coal and 0.364 for gas. Substitute your own sourced figures rather than treating these as authoritative. |
| `monthlyCapacityFactors` | fraction per month | An energy budget rather than a capacity limit. Used by Hydro, whose output is limited by inflow. |

**Sensitivity: fuel price and heat rate change dispatch, not just cost.** Together they set
short-run marginal cost, which sets merit order. Change them enough and plant swaps position in the
stack, which changes what is generated, what is curtailed and how much storage is needed, not
merely what the same dispatch costs. Capital cost and fixed operating cost, by contrast, change the
bill without changing a single MWh.

## Storage fleets

| Parameter | Unit | What it means |
|---|---|---|
| `initialEnergyCapacityMwh` | MWh | Installed storage energy. Zero means no fleet is built, but the cost and technology assumptions on the plan still govern any capacity storage sizing adds. |
| `initialPowerCapacityMw` | MW | Installed charge and discharge power. Must be zero exactly when energy is zero. |
| `costParameters.powerCapitalCostAudPerMw` | AUD/MW | Build cost scaling with power rating: inverters, connection. |
| `costParameters.energyCapitalCostAudPerMwh` | AUD/MWh of capacity | Build cost scaling with storage size: cells, reservoir. One-off, not annual. |
| `costParameters.fixedOperatingCostAudPerMwYear` | AUD/MW/year | Charged on installed power. |
| `technologyProfile.roundTripEfficiency` | fraction, 0–1 | Applied once, on charging. A full cycle loses `1 − efficiency` of the grid energy used to charge. |
| `technologyProfile.technicalLifeYears` | years | Annuitisation period. Batteries and pumped hydro differ by decades here, which is most of why their levelised costs differ. |

**Sensitivity: these costs price the answer, they do not choose it.** The sizing search never
reads a cost parameter. It probes power and energy separately and ranks the probes purely on
unserved energy and peak shortfall, so the MW and MWh it settles on are the same whatever these two
costs are; what changes is the bill attached to them. That is the same point
[Limitations §5](limitations.md#5-sizing-finds-a-near-frontier-point-not-an-optimum) makes: the
result is a near-frontier point, not a cost-optimal one, so the power and energy costs are worth
sweeping to price the shape the search chose, not to steer it.

A scenario with a zero-capacity Battery plan is how you tell NEMSweep "build whatever storage this
needs, and price it at these economics". That is the normal way to use the sizing loop.

## Storage sizing block

| Parameter | Unit | What it means |
|---|---|---|
| `targetUsePercentage` | % of demand energy | The reliability standard this scenario is sized against. Defaults to the NER standard if omitted. |
| `maximumPowerMw` | MW | Largest Battery power the search may consider, **per region**. |
| `maximumEnergyMwh` | MWh | Largest Battery energy the search may consider, **per region**. Must support four hours at the power maximum. |
| `maximumPasses` | count | Cap on whole-system dispatch passes. Each pass re-dispatches every region for the full period, so this bounds run time. |
| `reliabilityStandardName` | text | A label carried through to published results. |

The maxima are commercial limits you assert, not physical ones. Reaching one is a reportable
outcome, not a failure: "no battery within these bounds meets the standard" is a result. It is not
a proof that no battery could, which only the `energyLimited` outcome gives.

## Interconnectors

| Parameter | Unit | What it means |
|---|---|---|
| `fromRegionId`, `toRegionId` | region | A **directed** path. A reciprocal corridor is two entries. |
| `capacityMw` | MW | Directed transfer capacity, metered at the sending end. |
| `routeLengthKm` | km | The line's route length, declared directly rather than derived from anything else. A reciprocal corridor declares the same value on both of its entries. |
| `capitalCostAudPerKmPerMw` | AUD/km/MW | Build cost, scaling with both route length and capacity. |
| `fixedOperatingCostAudPerKmPerMwYear` | AUD/km/MW/year | Fixed operating cost on the same basis. There is no variable term. |
| `technicalLifeYears` | years | Annuitisation period. |

**Declaring a corridor in both directions charges its full route length twice**, covered in
[Limitations §6](limitations.md#6-reciprocal-interconnectors-are-each-costed-at-the-corridors-full-length).
Route length used to be derived from the endpoint regions' weather-site coordinates and ran long
against the real NEM corridors; it is now a value you supply, so a more accurate figure is a data
change, not a code change.

## Demand

| Parameter | Unit | What it means |
|---|---|---|
| `demandFile` | path | The operational-demand artifact for this region. Per region, not top-level. |
| `weatherFile` | path | The weather artifact for this region. Its solar site also supplies the region's published coordinates and drives the solar-geometry calculation. |
| `dataCentreNameplateMw` | MW | An additive demand component at full load factor, on top of base demand. |

`dataCentreNameplateMw` is named for the study it was built for, but nothing about it is
data-centre-specific: it is a flat, always-on load increase. Use it for any load you want to add
without a shape.

## Where the committed scenario's values came from

`scenarios/nem-fy2026-all-regions.json` uses a 7% real discount rate, Battery round-trip efficiency
0.87 over a 20-year life, pumped hydro 0.80 over 80 years, and 3,860 AUD/km/MW transmission capital
cost over 50 years, among others.

**None of these carry a citation, and the scenario schema has no field for one.** They are
plausible planning figures chosen to make a working example, and they are exactly the values you
should be replacing with your own and sweeping.

## Next

- [Scenario configuration](../guide/scenarios.md): the schema and validation rules.
- [Sweeps](../guide/sweeps.md): applying override patches to these values across a series of runs.
- [Sensitivity analysis](../exploring/sensitivity-analysis.md): reading what the variation tells
  you.
