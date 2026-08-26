---
description: "Use when changing NEMSweep.CLI commands, source-data parsing, configuration, exports, scenario runs, provenance, or generated web data artifacts."
applyTo: ["NEMSweep.CLI/**", "NEMSweep.CLI.Tests/**"]
---
# CLI and Data Pipeline

- Keep argument routing, usage text, and exit codes in `Application`; settings in
  `Configuration`; repository path and JSON policy in `Infrastructure`; and
  parsing/export behavior in its feature folder.
- Resolve every path through `WorkspacePaths`. Inputs come from the data root,
  published outputs go to the output root, and both are supplied by the caller:
  nothing may search the filesystem for a repository.
- Reject malformed, incomplete, duplicated, or misaligned source data explicitly;
  do not silently repair it. Use invariant culture for machine-readable formats.
- Preserve reproducibility: scenario results identify exact input bytes with
  schema version and SHA-256, while upstream filenames remain descriptive
  provenance.
- Keep command failures concise on stderr with exit code `1`; a command line that
  cannot be read returns `2` and throws `UsageException`. Do not leak stack traces
  from the command boundary.
- Test parsers with small synthetic fixtures and temporary files. Add contract
  tests when an exporter or generated JSON shape changes.
- Regenerate the committed web data with
  `dotnet run --project .\NEMSweep.CLI\NEMSweep.CLI.csproj -- --run-scenario --output .\NEMSweep.Web\wwwroot\data`
  after changing its producer or committed inputs. Without `--output` a run writes to
  `out/` and leaves the committed artifacts untouched.
