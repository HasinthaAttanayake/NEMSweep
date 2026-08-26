# NEMSweep Project Guidelines

## Architecture

- Treat this as a layered .NET 10 solution: `NEMSweep.Model` owns domain behavior,
  `NEMSweep.Contracts` owns serialized DTO shapes, `NEMSweep.CLI` ingests source data and
  produces site artifacts, and `NEMSweep.Web` presents those artifacts.
- Keep dependencies pointing inward. Domain code must not depend on contracts,
  CLI, or web concerns. Put parsing, file access, configuration, and JSON I/O in
  `NEMSweep.CLI`, not `NEMSweep.Model`.
- Read `README.md` for the current project map and commands. Read
  `docs/domain-model.md` before changing domain ownership, invariants, time, or
  unit semantics.

## Working Practice

- Implement only the currently requested validated layer; do not add speculative
  market concepts or abstractions for future roadmap items.
- Preserve explicit electricity units in names and contracts (`Mw`, `Mwh`,
  `AudPerMwh`). A run has one market-time offset: a single fixed UTC offset, no
  daylight saving, taken from the scenario period bounds
  (`Scenario.MarketTimeOffset`) and defaulting to the NEM's UTC+10. Every model
  series and period bound in a run must share it; a timestamp that records
  artifact creation uses UTC instead.
- Add or update focused xUnit tests beside the affected project. Tests use
  FluentAssertions and mirror production feature folders.
- Run the narrow affected test project first, then run
  `dotnet test .\NEMSweep.slnx` before finishing a cross-project change.
- Files under `NEMSweep.Web/wwwroot/data` are committed generated artifacts. Change
  their CLI producer or source inputs and regenerate them; do not hand-edit JSON.
- When a change makes these instructions inaccurate or establishes a stable new
  boundary, convention, or recurring command, update the smallest relevant
  instruction file in the same change. Do not record roadmap ideas, temporary
  implementation details, or facts readily discovered from code.
