# NemSim Project Guidelines

## Architecture

- Treat this as a layered .NET 10 solution: `NEM.Model` owns domain behavior,
  `NEM.Contracts` owns serialized DTO shapes, `NEM.CLI` ingests source data and
  produces site artifacts, and `NEM.Web` presents those artifacts.
- Keep dependencies pointing inward. Domain code must not depend on contracts,
  CLI, or web concerns. Put parsing, file access, configuration, and JSON I/O in
  `NEM.CLI`, not `NEM.Model`.
- Read `README.md` for the current project map and commands. Read
  `docs/domain-model.md` before changing domain ownership, invariants, time, or
  unit semantics.

## Working Practice

- Implement only the currently requested validated layer; do not add speculative
  market concepts or abstractions for future roadmap items.
- Preserve explicit electricity units in names and contracts (`Mw`, `Mwh`,
  `AudPerMwh`). Preserve fixed NEM market time (UTC+10) unless a timestamp records
  artifact creation, which uses UTC.
- Add or update focused xUnit tests beside the affected project. Tests use
  FluentAssertions and mirror production feature folders.
- Run the narrow affected test project first, then run
  `dotnet test .\NemSim.slnx` before finishing a cross-project change.
- Files under `NEM.Web/wwwroot/data` are committed generated artifacts. Change
  their CLI producer or source inputs and regenerate them; do not hand-edit JSON.
- When a change makes these instructions inaccurate or establishes a stable new
  boundary, convention, or recurring command, update the smallest relevant
  instruction file in the same change. Do not record roadmap ideas, temporary
  implementation details, or facts readily discovered from code.
