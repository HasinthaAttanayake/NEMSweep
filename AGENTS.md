# AGENTS.md

## Using NEMSweep

Read [`skills/nemsweep/SKILL.md`](skills/nemsweep/SKILL.md). It is the single source of guidance for
running NEMSweep: install routes, preflight checks, the starters, a smoke test, recipes, and how to
read and report results. Nothing else in this repository repeats it.

Raw URL, for fetching rather than browsing:
<https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/skills/nemsweep/SKILL.md>

## Working on this repository

```bash
dotnet build NEMSweep.slnx
```

```bash
dotnet test NEMSweep.slnx
```

```bash
dotnet tool restore
```

```bash
dotnet docfx docs/docfx.json --warningsAsErrors
```

CI runs the docs build with `--warningsAsErrors`, so a broken cross-reference or a TOC entry with no
page behind it fails the build. `dotnet docfx docs/docfx.json --serve` previews the site at
<http://localhost:8080>.

**Do not edit published worked-example artifacts.** Every `results*.json`, every file under a
`sweeps/<sweepId>/` directory, and every ingested `demand-*.json` / `weather-*.json` is generated,
and the results site reads the published set. Regenerate by rerunning the command that produced it
(`--ingest`, `--run-scenario`, `--run-sweep`) rather than editing one by hand.

The committed JSON schemas under `schema/` are generated too: CI regenerates each with
`--describe-schema` and fails on a diff.
