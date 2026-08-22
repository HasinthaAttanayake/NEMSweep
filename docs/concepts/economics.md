# Economics

The figures on this page are the cost of **building and running** the modelled system for one year,
not a retail electricity bill. There is no bidding, no settlement, no market price anywhere in
NemSim, and nothing here should be read as what anyone would pay. See
[Limitations §3](../assumptions/limitations.md#3-this-is-the-cost-of-building-the-system-not-a-power-bill)
before quoting a dollar figure from a NemSim result.

## Annuitisation

A scenario models a single year, but the assets in it last decades: a coal plant, a battery, an
interconnector. `LevelisedCostCalculator` spreads a one-off capital cost across an asset's technical
life using the standard capital recovery factor:

```text
r(1 + r)^n / ((1 + r)^n − 1)
```

for real discount rate `r` and technical life `n` years. Multiplying a capital sum by that factor
gives the equal annual payment that would repay it, at that rate, over that life. That is how a
single modelled year can carry its fair share of an asset that will still be running in year
twenty.

The rate is **real, not nominal**: every cost figure in the model is stated in the scenario's
real-dollar year, so applying a nominal rate on top would double-count inflation. At a zero discount
rate the annuity formula above is undefined (division by zero), so the calculator falls back to
straight-line recovery, `1/n`, as the degenerate case.

## What is charged for generation

For each generating fleet, the annual cost is:

```text
annualised power capex + one year of fixed opex
    + variable opex on gross generated energy + fuel cost on gross generated energy
```

Capital cost is annuitised from the fleet's power capacity and technical life. Fixed opex is one
year's charge against nameplate capacity. Variable opex and fuel cost are both charged on **gross**
generated energy, before curtailment or storage charging are subtracted out, using the same
short-run marginal cost components that set merit order: variable operating cost, and fuel price
multiplied by heat rate.

## What is charged for storage

For each realised storage fleet, the annual cost is:

```text
annualised (power capex + energy capex) over technical life + one year of fixed power opex
```

charged against the fleet's **total final capacity**, including whatever capacity storage sizing
introduced and not just what the scenario originally declared. There is no separate charge for the
energy storage moves while charging. That is deliberate, not an omission: gross-generation variable
operating cost and fuel are already charged on the energy used to charge storage, at the point that
energy was generated, and a battery's round-trip loss is already folded into that gross-generation
figure because more had to be generated than was later delivered. Charging storage again for its
energy throughput would double-count it. For the same reason, the storage figure published here is
**not** a standalone levelised cost of storage (LCoS). It prices only the storage asset itself,
against the same served-energy denominator as everything else, rather than the delivered cost of
energy that passed through storage.

## The denominator

Every levelised figure, for generation, storage, transmission and the total alike, divides by
`DeliveredToLoad`: demand minus unserved energy. It never divides by generation. A regional levelised
cost uses only that region's own delivered energy; the system figure uses the system total. Dividing
by delivered energy rather than generation is what makes the figures comparable to a genuine cost of
supply: energy generated but curtailed, or lost to storage round-trip inefficiency, was never
delivered to anyone and so does not appear in the denominator that spreads the system's cost.

## Why regional costs do not sum to the system total

Transmission is annuitised once, at system level, from each directed interconnector's own capital and
fixed-opex assumptions and its route length. It is never split or attributed back to either endpoint
region. Every way of doing that split is arbitrary, whether by rating, by realised flow or evenly,
so the model does not attempt one. The consequence is genuine rather than an oversight: summing every region's
levelised cost will not reproduce the system levelised cost, because the system figure carries
transmission and the regional figures explicitly do not.

## Decimal versus double

Money and every cost-rate type (`Money`, `EnergyPrice`, `GenerationEnergyCost`, `PowerCapacityCost`,
and so on) use `decimal`, because cost arithmetic is base-10 and accumulating it in binary
floating-point would introduce artefacts a reader could easily mistake for a modelling error.
Measured physical quantities such as power, energy and distance remain `double`. The two only meet
inside typed conversion methods (`PowerCapacityCost.For(Power)`, `EnergyPrice.For(Energy)`, and so on), which
validate that the physical value is finite and non-negative before converting it. This is a
correctness-of-presentation choice, not a precision upgrade: using `decimal` prevents base-10
rounding artefacts from being mistaken for a model defect, but it does not make the underlying cost
assumptions any more accurate.

## Cost-rate types

| Type | Unit | Prices |
|---|---|---|
| `Money` | AUD | A monetary amount: a cost, credit, or net adjustment. |
| `EnergyPrice` | AUD/MWh delivered to load | The output unit of every levelised cost figure (SLCoE and its regional equivalents). |
| `GenerationEnergyCost` | AUD/MWh generated | Variable operating cost and fuel-derived cost on gross generation; also short-run marginal cost. |
| `PowerCapacityCost` | AUD/MW | One-off generation or storage power capital cost. |
| `EnergyCapacityCost` | AUD/MWh of storage capacity (one-off) | Storage energy capital cost. Not AUD/MWh generated or delivered. |
| `AnnualPowerCapacityCost` | AUD/MW/year | Recurring fixed operating cost against installed power capacity. |
| `FuelPrice` | AUD/GJ (thermal) | Fuel price; combined with `HeatRate` to derive the fuel component of `GenerationEnergyCost`. |
| `HeatRate` | GJ/MWh | Thermal energy required per MWh generated. |
| `DistancePowerCost` | AUD/km/MW (one-off) | Interconnector capital cost, scaled by route length and directed capacity. |
| `AnnualDistancePowerCost` | AUD/km/MW/year | Interconnector fixed operating cost, scaled the same way. |

## Next

- [Storage sizing](storage-sizing.md): how the Battery capacity priced here is arrived at.
- [Transmission](transmission.md): the layering that produces the route length `DistancePowerCost`
  and `AnnualDistancePowerCost` are charged against.
- [Limitations](../assumptions/limitations.md): what a NemSim AUD/MWh figure is not.
