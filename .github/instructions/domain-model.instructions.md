---
description: "Use when adding or changing NEM.Model domain types, aggregate roots, domain services, relationships, invariants, time semantics, or unit semantics. Keeps the tracked domain model accurate."
applyTo: ["NEM.Model/**", "NEM.Model.Tests/**"]
---
# Domain Model Maintenance

- Read `docs/domain-model.md` before changing domain ownership or relationships.
- Keep `Scenario` as intent, `ScenarioDerivation` as the pure transformation,
  `PowerSystem` as realised configuration, and `Dispatcher` scenario-blind.
- Update `docs/domain-model.md` in the same change whenever a domain type,
  aggregate relationship, derivation boundary, invariant, time rule, or unit
  rule changes.
- Document only implemented behavior; do not add speculative future types.
- Add focused tests for new or changed domain invariants.