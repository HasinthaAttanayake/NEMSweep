---
description: "Use when adding or changing NEM.Contracts DTOs, JSON schema versions, serialized field names, units, or producer-consumer data contracts."
applyTo: "NEM.Contracts/**"
---
# Serialized Contracts

- Treat public record constructor parameters as versioned serialized API fields;
  use explicit unit suffixes for numeric energy, power, and cost values.
- Keep contracts behavior-free and independent of `NEM.Model`, `NEM.CLI`, and
  `NEM.Web`.
- For a breaking shape or meaning change, increment the artifact schema version
  and update the CLI producer, every web consumer, and the relevant round-trip
  contract test in the same change.
- Regenerate affected files under `NEM.Web/wwwroot/data`; never patch generated
  JSON to make consumers pass.
