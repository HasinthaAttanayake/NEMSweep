# Domain model

This document tracks the domain types currently implemented in `NEM.Model`.
It describes code that exists now; future concepts belong here only when their
domain types and invariants are implemented.

```mermaid
classDiagram
    class Scenario {
        ScenarioId id
        string name
        string regionId
        DateTimeOffset periodStart
        DateTimeOffset periodEnd
        ScenarioFleet[] fleets
    }
    class ScenarioFleet {
        TechnologyKey technology
        Power nameplateCapacity
        monthlyCapacityFactors
    }
    class PowerSystem {
        PowerSystemId id
        ScenarioId derivedFromScenario
        Region[] regions
    }
    class Region {
        string regionId
        DemandProfile demand
        GeneratingFleet[] fleets
        RegionalResourceProfile resourceProfile
    }
    class DispatchOutcome

    Scenario "1" *-- "1..*" ScenarioFleet
    Scenario --> PowerSystem : ScenarioDerivation.Derive
    PowerSystem "1" *-- "1..*" Region
    Region "1" *-- "1..*" GeneratingFleet
    Region "1" *-- "1" DemandProfile
    Dispatcher --> Region : consumes
    Dispatcher --> DispatchOutcome : produces
```

## Ownership boundaries

- `Scenario` is the aggregate root for scenario intent. It validates identity,
  NEM-time period bounds, target region, and fleet plans.
- `ScenarioDerivation` is a pure domain service that realises a `PowerSystem`
  from scenario intent and aligned demand/resources.
- `PowerSystem` is the realised grid aggregate and cites its source scenario by
  `ScenarioId`. It owns one or more `Region` aggregates.
- `Dispatcher` remains scenario-blind. It consumes a realised `Region` and
  produces a `DispatchOutcome`.
- Exported run results cite scenario and power-system identities rather than
  serialising the domain object graph.

## Input provenance

Demand and weather paths are CLI configuration used to locate inputs; they are
not part of `Scenario` identity. A result records the filename, input schema
version, and SHA-256 digest of the exact bytes parsed for each input. The digest
is the reproducibility boundary when a configured path is later overwritten.

The upstream demand archive names remain descriptive provenance from the demand
artifact. They do not replace the demand artifact digest.

## Time and units

Scenario periods and model series use fixed NEM market time (UTC+10). Result
generation timestamps use UTC because they describe when an artifact was
created; seeing `+10:00` period bounds and a `+00:00` generated timestamp in the
same result is intentional.

Flow series are interval-average MW and integrate to MWh through
`FlowSeries.Integrate()`. The dispatch invariant is:

```text
generation + discharge + imports + unserved
    = demand + charge + exports + curtailment
```

The current single-region, no-storage model sets charge, discharge, imports,
and exports to zero. Result JSON exports generation delivered to load
(`generation - curtailment`), for which the current reduced identity is:

```text
delivered generation + unserved = demand
```

Reliability reports both unserved energy (USE) as a percentage of demand and
hours served. USE percentage is the binding reliability measure; hours served
is diagnostic and must not be compared with an energy-based reliability target.