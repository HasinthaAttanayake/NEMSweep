---
description: "Use when adding or changing NEMSweep.Model domain types, aggregate roots, domain services, relationships, invariants, time semantics, or unit semantics. Keeps the tracked domain model accurate."
applyTo:
    - "NEMSweep.Model/**"
    - "NEMSweep.Model.Tests/**"
---
# Domain Model Maintenance

- Read `docs/domain-model.md` before changing domain ownership or relationships.
- Keep `Scenario` as intent, `ScenarioDerivation` as the pure transformation,
  `PowerSystem` as realised configuration, and `Dispatcher` scenario-blind.
- Keep `StorageSizingService` pure and whole-system scoped. It may create
    immutable `PowerSystem` candidates and rerun `Dispatcher`, but only Battery
    storage is sizeable; pumped hydro remains fixed.
- Describe sizing results as coordinate-wise near-frontier unless a cost or
    explicit ordering objective is implemented. Do not call the 2-D result a
    global minimum.
- When a public domain process-output type changes, update its XML documentation
    to describe the resulting public semantics, relationships, and units.
- Update `docs/domain-model.md` in the same change whenever a domain type,
  aggregate relationship, derivation boundary, invariant, time rule, or unit
  rule changes.
- Document only implemented behavior; do not add speculative future types.
- Add focused tests for new or changed domain invariants.