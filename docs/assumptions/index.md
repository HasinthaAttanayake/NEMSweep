# Model assumptions

There are two kinds of assumption in a NEMSweep result, and confusing them is the most common way to
misread one.

**Model assumptions** are baked into the code. You cannot change them by editing a scenario, and
every run carries them. They are the subject of this page.

**Scenario parameters** are values *you* supply: discount rate, capital costs, round-trip
efficiencies, technical lives, fuel prices, installed capacities. NEMSweep makes no claim about them;
it takes what you give it. They are covered in [Scenario parameters](scenario-parameters.md).

Read [Limitations](limitations.md) first if you have not. This page says what the model assumes;
that page says where those assumptions will mislead you.

## How to read the register

Each assumption gives its value, where it lives in the code, why it is what it is, what it does to
results, and the trigger that should make you revisit it.

The numeric values in the register below are **checked against the code by a test**. If someone
changes a constant without updating this page, `ModelAssumptionsTests` fails. That is deliberate:
an assumptions page that can silently go stale is worse than none.

<!-- assumption-values:begin -->

| Key | Value |
|---|---|
| `reliability.defaultTargetUsePercentage` | 0.002 |
| `sizing.minimumPowerMw` | 30 |
| `sizing.minimumEnergyMwh` | 120 |
| `sizing.defaultMaximumPasses` | 256 |
| `transfer.lossFactorPerHop` | 0.05 |
| `storage.pumpedHydroSeedFraction` | 0.8 |
| `storage.defaultSeedFraction` | 0.5 |
| `hydro.reserveFraction` | 0.1 |
| `solar.groundAlbedo` | 0.2 |
| `solar.systemFactor` | 0.95 |
| `solar.standardTestIrradianceWattsPerSquareMetre` | 1000 |
| `solar.cellTemperatureRiseAboveDryBulbCelsius` | 25 |
| `solar.referenceCellTemperatureCelsius` | 25 |
| `solar.temperatureCoefficientPerCelsius` | -0.0027 |
| `wind.referenceTurbineCapacityMw` | 3.4 |
| `wind.referenceAirDensityKilogramsPerCubicMetre` | 1.225 |
| `wind.hubHeightMetres` | 120 |
| `wind.shearExponent` | 0.2 |
| `wind.cutInSpeedMetresPerSecond` | 2.5 |
| `wind.ratedSpeedMetresPerSecond` | 11 |
| `wind.cutOutSpeedMetresPerSecond` | 20 |

<!-- assumption-values:end -->

## Reliability

### Default reliability target: 0.002% of demand energy unserved

**Where:** `StorageSizingOptions.DefaultTargetUsePercentage`
**Source:** the National Electricity Rules reliability standard.
**Impact:** this is what storage is sized against. A stricter target grows storage; a looser one
shrinks it, superlinearly in both directions, because the last increment of reliability is bought
against the rarest events.
**Revisit when:** the NER standard changes, or you are deliberately pricing a different standard.
In that second case, set `storageSizing.targetUsePercentage` on the scenario rather than changing
the code.

Note the mismatch described in [Limitations §1](limitations.md#1-a-deterministic-run-cannot-honour-an-expected-unserved-energy-standard): the standard is
an expectation, and a single-weather-year run produces a realisation.

### Unserved energy percentage is the binding measure

**Where:** `ReliabilityMetrics`, `SystemReliabilityAssessment`
**Why:** an energy-based measure is what the standard is written in.
**Impact:** hours served and peak unserved power are also published, but they are **diagnostics**.
A system can serve 99.99% of hours and still fail on energy, because the hours it misses are the
big ones. Never compare an hours-based figure against an energy-based target.
**Revisit when:** never, without also changing what "meets the standard" means.

### The system and every region must pass

**Where:** `SystemReliabilityAssessment.Create`
**Why:** a system-average that hides one failing region is not a reliable system.
**Impact:** system USE is calculated from aggregate demand and aggregate unserved energy, never by
averaging regional percentages, which would weight a small region equally with a large one.

## Dispatch

### Hourly intervals, one modelled year

**Where:** `SystemDispatchOutcome.CreateCore` requires hourly resolution; the scenario declares a
period.
**Why:** demand data arrives half-hourly and weather hourly; hourly is the coarsest resolution both
support and the finest the weather justifies.
**Impact:** sub-hourly ramping and within-hour variability are invisible. Storage requirements
driven by minute-scale events are not captured at all.
**Revisit when:** you need to model frequency support or fast response, which this model cannot do.

### Interval outer loop, region inner loop

**Where:** `SystemDispatchRun.Execute`
**Why:** so every region sits at the same hour at the same time and a surplus in one can serve a
deficit in another within that hour.
**Impact:** a system with no interconnectors produces results identical to dispatching each region
alone. Adding a link changes results only through transfer, never through the loop structure.

### Order within an interval: generation, then transfer, then storage

**Where:** `SystemDispatchRun.Execute`
**Why:** exports must be able to beat local battery charging, or a region would store energy its
neighbour needed.
**Impact:** this ordering biases toward inter-regional sharing over local storage. A different
order would size storage differently.

### Merit order is short-run marginal cost, then technology

**Where:** `GenerationMeritOrder.Sort`
**Why:** the technology tie-break makes a run deterministic when two fleets have equal cost, which
they frequently do at zero fuel cost.
**Impact:** with several zero-cost fleets tied, dispatch order follows the declaration order of the
`GenerationTechnology` enum (Solar, Wind, Hydro, Coal, Gas). That decides which fleet's output gets
curtailed first.

### Hydro is paced against a monthly budget, 90% paced and 10% reserved

**Where:** `HydroReservationState`, `GenerationBudgetState`, `RegionalDispatchRun.DispatchHydroFallback`
**Why:** Hydro is the only technology limited by an energy allowance rather than fuel cost. Sorted
purely by cost it would be spent on whichever hours came first each month. An earlier
"dispatch Hydro last" rule was tried and stranded roughly 93% of the budget instead of rationing it.
**How it works:** 90% of the monthly budget is metered by a causal threshold controller. Each
interval it caps Hydro's request at `max(0, residualDemand − T)`, where T is solved by bisection so
that, applied over a trailing 336-interval window of *past* residual-demand observations, it would
have spent exactly the budget affordable per interval over the intervals remaining in the month.
The remaining 10% is held out of merit order entirely and spent only as a last-resort local backstop
after that region's own storage has run. Neither pool carries into the next month, so with fewer
than three days left the unspent reserve is released into the paced pool.
**No foresight:** every input is the scenario-declared budget, calendar arithmetic, the current
interval's own residual demand, or strictly past observations.
**Impact:** the controller self-calibrates to a region's demand distribution without per-region
tuning, but it is a heuristic. A real operator with an inflow forecast and a reservoir model would
do better.
**Revisit when:** you have inflow time series rather than monthly capacity factors.

### Storage policy is greedy and charges incrementally only from Coal and Gas

**Where:** `GreedySurplusAndIncrementalGenerationChargingPolicy`
**Why:** the policy sees one interval. See [Limitations §4](limitations.md#4-greedy-storage-dispatch-is-wrong-in-both-directions).
**How it works:** on a deficit it discharges; on a surplus it charges from that surplus first, then
may start incremental Coal or Gas to fill remaining storage headroom, in ascending short-run
marginal cost order. Battery is served before PumpedHydro.
**Impact:** storage never pre-charges against a forecast shortfall, and never arbitrages.
**Revisit when:** you implement a policy with foresight. `IStoragePolicy` is the extension point,
and swapping it requires no change to dispatch.

### Round-trip efficiency is applied once, on charging

**Where:** `StorageFleet.Operate`
**Impact:** input MWh × efficiency becomes stored MWh; discharge delivers stored MWh one-for-one.
A full cycle therefore loses `1 − efficiency` of the grid energy used to charge. Charging losses are
priced through gross-generation variable and fuel cost, not as a separate storage cost.

## Storage

### Opening state of charge: 80% for pumped hydro, 50% for everything else

**Where:** `StorageSeedPolicy`
**Why:** dispatch used to open every fleet at zero MWh, which is not how real plant starts a year.
Large reservoir schemes are typically operated near full as an operating reserve; 50% is a mid-point
absent fleet-specific cycling data.
**Source:** neither fraction is sourced from operational data. They are modelling assumptions.
**Impact:** the seed is free energy at the start of the run. A shorter modelled period would be more
sensitive to it; over a full year it matters mainly for a January shortfall.
**Important:** the seed is always computed from the **scenario-declared** installed capacity, never
from capacity storage sizing has since grown. If growing a fleet also grew its opening balance,
sizing would be handing itself free energy and would stop measuring what installed capacity alone
achieves. A region with no installed fleet of a technology gets no seed even if sizing later
introduces one.
**Revisit when:** real operational state-of-charge data is available.

### Only Battery is sizeable; pumped hydro is fixed

**Where:** `StorageSizingService`, `StorageSizingSearch`
**Why:** new pumped hydro is a site-specific project with a fixed reservoir, not a quantity that can
be scaled freely. Batteries are, to a first approximation, purchasable in arbitrary quantity.
**Impact:** a scenario that would be better served by more pumped hydro will instead be given
batteries, at battery economics.

### Sizing floor: 30 MW and 120 MWh, with a four-hour minimum duration

**Where:** `StorageSizingOptions.MinimumPowerMw`, `MinimumEnergyMwh`; enforced in
`StorageSizingSearch`
**Why:** probing upward from a fleet too small to change the outcome wastes dispatch passes. The
four-hour floor is preserved by every sized candidate.
**Impact:** a system needing only a token amount of storage will still be given 30 MW / 120 MWh.
Reported capacity is a floor, not a fitted minimum, at the small end.
**Revisit when:** modelling systems where sub-30 MW storage is a meaningful answer.

### Capacity limits are per region, not per system

**Where:** `StorageSizingOptions`
**Impact:** `maximumPowerMw` and `maximumEnergyMwh` bound each region independently. A
whole-system artifact sums the regional figures, limits included, so a system total is compared
against the summed ceiling rather than the per-region one.

### Sizing returns a near-frontier point

**Where:** `StorageSizingSearch`
See [Limitations §5](limitations.md#5-sizing-finds-a-near-frontier-point-not-an-optimum). Never
describe a sizing result as optimal or minimal.

## Transmission

### Flat 5% loss per hop

**Where:** `InterRegionalTransfer.LossFactorPerHop`
**Source:** **none. This is an unsourced placeholder** (tracked as NEM-053). AEMO publishes
marginal loss factors per interconnector; they are neither flat nor equal across links.
**How it works:** the loss factor is applied *over* the max-flow result rather than inside it.
Capacity is metered at the sending end of every edge, so flow is conserved in the capacity graph
and the search remains a standard max-flow problem. A two-hop route therefore delivers 0.95².
**Impact:** any conclusion that depends materially on inter-regional transfer inherits this
uncertainty.
**Revisit when:** sourcing a cited per-link value. This is the highest-priority open assumption in
the model.

### Reciprocal links are costed twice

**Where:** the scenario convention of declaring each corridor as two directed interconnectors.
See [Limitations §6](limitations.md#6-reciprocal-interconnectors-are-each-costed-at-the-corridors-full-length).
Roughly 1.8× of reported transmission cost is this convention.

### Transfer serves regions in priority order

**Where:** `PrioritisedTransferSolver`
**How it works:** each sink is served in turn by a full max-flow solve over the sources' remaining
capacity. Committed flow is subtracted from edge capacity before the next sink starts, from a fresh
network, so no residual reverse edge crosses a stage boundary and a lower-priority region can never
claw back flow already committed to a higher-priority one.
**Impact:** that priority guarantee is exactly why the outcome is deliberately not a global optimum.

### Exports draw curtailment first, and pumped hydro is never exportable

**Where:** `InterRegionalTransfer`, `RegionalDispatchRun`
**Why:** storage is decided after transfer, so storage cannot be a transfer source. Hydro's paced
share *can* be exported, capped to the same per-interval pace as local dispatch, so an export can
substitute for local demand this interval but never draws on budget paced for a future local peak.
Hydro's 10% reserve is never exportable at all.

## Weather and demand

### One typical meteorological year

**Where:** the EPW inputs; declared on every artifact as `WeatherBasisKind.TypicalMeteorologicalYear`
**Why:** it is the standard, freely available representation of a site's climate.
**Impact:** a TMY is assembled from representative months across several observed years. It
excludes the tail events that drive storage and reliability. See
[Limitations §1](limitations.md#1-a-deterministic-run-cannot-honour-an-expected-unserved-energy-standard).
**Revisit when:** you can run against multiple actual meteorological years.

### Each region has a separate solar site and wind site

**Where:** the input bundle's `weather/{REGION}/solar` and `weather/{REGION}/wind`
**Why:** the best solar resource and the best wind resource in a region are rarely co-located.
**Impact:** the solar site doubles as the region's location for transmission costing, which is the
approximation described above. There is no single weather basis for a whole-system result: each
region is simulated against its own typical year.

### Demand is operational demand, exogenous and inelastic

**Where:** AEMO actual operational demand archives; `DemandProfile`
**Impact:** rooftop PV is already netted out. See
[Limitations §2](limitations.md#2-the-82-renewable-target-is-not-the-same-target-on-a-grid-scale-basis).
Demand does not respond to price or to scarcity. Additive components, such as a data-centre load,
are added on top as flat full-load-factor flows unless a shape is supplied.

### The model is deterministic

**Where:** everywhere. There is no random number generator in the model.
**Impact:** same inputs at the same commit reproduce every modelled value exactly. There are no
confidence intervals, because there is no distribution, only the single realisation you asked for.
In a dispatch artifact, the one field that changes between otherwise identical reruns is `runId`.
Imported input artifacts also carry a `generatedAt` timestamp, and a sweep index records measured
run durations, so those files differ in more than `runId`; see
[Outputs and provenance](../guide/outputs.md). This is what makes sweeps and sensitivity analysis
the right way to use the tool.

## Solar and wind resource conversion

Every constant in this section is fixed in the model. A scenario config cannot change any of them;
it can only scale the resulting trace by a fleet's `nameplateCapacityMw`. The wind values are
`WindPowerCurveSettings` defaults, which a library caller could override, but the CLI never does,
so every published artifact was produced with the values below.

### Solar array is dual-axis tracking

**Where:** `GlobalTiltedIrradiationSeries`, `DualAxisSolarPowerCurve`
**Why:** the modelled asset is a utility-scale N-type HJT plant on dual-axis trackers, which keeps
the array normal to the sun and removes tilt and azimuth from the input surface entirely.
**Impact:** it is the most favourable orientation there is. A fixed-tilt or single-axis plant on the
same site would produce less, and with a different daily shape, so a NEMSweep solar trace is not a
generic "solar" trace.
**Revisit when:** you need to model an existing fixed-tilt fleet rather than new build.

### Ground albedo: 0.2

**Where:** `GlobalTiltedIrradiationSeries.GroundAlbedo`
**Source:** the conventional generic-ground value used in solar resource assessment. Not a
site-specific measurement.
**Impact:** it sets how much ground-reflected irradiation reaches the tilted array. It is a small
term in the total, and it is not measured, so a site over sand or snow is modelled low and a site
over dark soil high.
**Revisit when:** modelling a site with a measured albedo, or one where ground reflectance is
plainly not generic.

### System factor: 0.95, before temperature derating

**Where:** `DualAxisSolarPowerCurve.SystemFactor`
**Source:** a single lumped figure standing in for inverter, wiring and soiling losses. Not sourced
from a specific plant's performance data.
**Impact:** it scales every solar interval by 5%, so it moves the level of solar output without
changing its shape. Any conclusion about solar's *share* is far less sensitive to it than any
conclusion about absolute solar energy.
**Revisit when:** plant-specific performance-ratio data is available.

### Cell temperature: 25 °C above ambient, derated at −0.27% per °C

**Where:** `DualAxisSolarPowerCurve.CellTemperatureRiseAboveDryBulbCelsius`,
`ReferenceCellTemperatureDegreesCelsius`, `TemperatureCoefficientPerDegreeCelsius`
**Source:** the temperature coefficient is a typical N-type HJT figure; the 25 °C rise is a flat
assumption rather than a thermal model. Neither is measured for a specific site.
**Impact:** cell temperature is modelled as dry-bulb plus a constant, so it does not respond to
irradiance, wind speed or mounting. Hot, still, high-irradiance hours are the ones this understates
most, and those are summer afternoons.
**Revisit when:** you need summer peak solar output to be right in level rather than in shape.

### Standard test irradiance: 1000 W/m²

**Where:** `DualAxisSolarPowerCurve.StandardTestIrradiance`
**Source:** the IEC standard test condition every panel's nameplate rating is quoted against.
**Impact:** none that is discretionary. It is the definition of the rating the model scales from.

### Wind turbine: a 3.4 MW reference curve at 1.225 kg/m³

**Where:** `WindPowerCurve.ReferenceTurbineCapacity`,
`ReferenceAirDensityKilogramsPerCubicMetre`
**Source:** a digitised Goldwind GW 140/3MW(S) power curve, published at 1.225 kg/m³.
**Impact:** one machine stands in for every wind fleet in the model, and **no air-density
correction is applied**, so a site materially denser or thinner than sea-level standard is modelled
slightly wrong. Applying a non-linear curve to an interval-mean wind speed also approximates rather
than equals mean power over the interval.
**Revisit when:** modelling a fleet whose turbines differ materially from this class, or a site at
altitude.

### Hub height 120 m, shear exponent 0.2

**Where:** `WindPowerCurve.DefaultHubHeightMetres`, `DefaultShearExponent`, applied through
`WindPowerCurveSettings`
**Source:** 120 m is a representative modern onshore hub height; 0.2 is the conventional open-land
power-law shear exponent. Neither is site-measured.
**Impact:** EPW wind speed is measured at 10 m and extrapolated to hub height by the power law, so
the shear exponent is applied to every interval and moves modelled wind energy directly. Over
forest or complex terrain the true exponent is higher, and over water lower.
**Revisit when:** site-measured shear or a mast at hub height is available.

### Cut-in 2.5 m/s, rated 11 m/s, cut-out 20 m/s

**Where:** `WindPowerCurve.CutInWindSpeedMetresPerSecond`, `RatedWindSpeedMetresPerSecond`,
`DefaultCutOutWindSpeedMetresPerSecond`
**Source:** the reference turbine's own curve. `MinimumCutOutWindSpeedMetresPerSecond` (20 m/s) is
the floor a caller-supplied cut-out may not go below.
**Impact:** below cut-in the turbine produces nothing; from rated speed up to cut-out it produces
its full rating; above cut-out it shuts down and output falls to zero. Storm shutdowns are
therefore modelled as instantaneous and fleet-wide across a region, with no hysteresis and no
spatial diversity between turbines.
**Revisit when:** modelling a region where high-wind shutdown events are material to reliability.

## Economics

### Costs are annuitised over technical life at a real discount rate

**Where:** `LevelisedCostCalculator`
**Why:** so a single modelled year can carry its share of assets that last decades.
**Impact:** the discount rate is real, not nominal. Costs are stated in the scenario's real-dollar
year, so inflation must not be applied twice. At a zero rate the annuity formula is undefined and
recovery degenerates to straight-line, `1/n`.

### One year of operating cost, on gross generation

**Where:** `PowerSystemCostCalculator`
**Impact:** variable operating cost and fuel are charged on **gross** generated energy, which is
why storage charging losses are already priced and the storage component adds no charging energy of
its own. The storage figure is annualised storage asset cost over served energy; it is **not** a
standalone levelised cost of storage.

### Transmission is charged once, at system level

**Where:** `PowerSystemCostCalculator`
**Impact:** regional costs deliberately do not sum to the system total. A regional result states
that transmission is not modelled in its cost scope, even though it discloses its incoming-link
loss allocation.

### Every levelised figure is divided by energy served

**Where:** `PowerSystemCostBreakdown`, `RegionCostBreakdown`
**Impact:** the denominator is `DispatchOutcome.DeliveredToLoad`, meaning demand minus unserved,
never generation. A regional levelised cost uses only that region's served energy, never the system
total.

## Next

- [Limitations](limitations.md): where these assumptions will mislead you.
- [Scenario parameters](scenario-parameters.md): the values you choose.
- [Domain model reference](../domain-model.md): the full ownership and invariant detail.
