---
description: "Use when adding or changing NEMSweep.Contracts DTOs, JSON schema versions, serialized field names, units, or producer-consumer data contracts."
applyTo: "NEMSweep.Contracts/**"
---
# Serialized Contracts

- Treat public record constructor parameters as versioned serialized API fields;
  use explicit unit suffixes for numeric energy, power, and cost values.
- Keep contracts behavior-free and independent of `NEMSweep.Model` and `NEMSweep.CLI`.
- For a breaking shape or meaning change, increment the artifact schema version
  and update the CLI producer, downstream consumers, and the relevant round-trip
  contract test in the same change.
- Regenerate affected published artifacts; never patch generated JSON to make consumers pass.
