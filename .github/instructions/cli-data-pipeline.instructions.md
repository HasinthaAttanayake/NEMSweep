---
description: "Use when changing NEM.CLI commands, source-data parsing, configuration, exports, scenario runs, provenance, or generated web data artifacts."
applyTo: ["NEM.CLI/**", "NEM.CLI.Tests/**"]
---
# CLI and Data Pipeline

- Keep argument routing, usage text, and exit codes in `Application`; settings in
  `Configuration`; repository path and JSON policy in `Infrastructure`; and
  parsing/export behavior in its feature folder.
- Resolve configured relative input paths from the solution root through
  `RepositoryPaths`. Write published outputs through its `WebDataPath` locations.
- Reject malformed, incomplete, duplicated, or misaligned source data explicitly;
  do not silently repair it. Use invariant culture for machine-readable formats.
- Preserve reproducibility: scenario results identify exact input bytes with
  schema version and SHA-256, while upstream filenames remain descriptive
  provenance.
- Keep command failures concise on stderr with exit code `1`; invalid usage returns
  `2`. Do not leak stack traces from the command boundary.
- Test parsers with small synthetic fixtures and temporary files. Add contract
  tests when an exporter or generated JSON shape changes.
- Regenerate dispatch data with
  `dotnet run --project .\NEM.CLI\NEM.CLI.csproj -- --run-scenario` after changing
  its producer or committed inputs.
