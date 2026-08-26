# Contributing

NEMSweep is built in public, one validated layer at a time. Contributions are welcome, and the
conventions below are what keep the thing trustworthy rather than just working.

## Before you start

For anything beyond a typo, open an issue first. The model is built in a deliberate order and a
change that looks small can depend on a layer that is not there yet, so it is worth a short
conversation before you spend an evening on it.

## Getting set up

```bash
dotnet build NEMSweep.slnx
```

```bash
dotnet test NEMSweep.slnx
```

A fresh clone runs without configuration. See [Getting started](docs/guide/index.md).

## The one property that matters most

**The model is deterministic.** The same inputs at the same commit reproduce every modelled value
exactly. That is the property every claim NEMSweep makes rests on, and it is easy to break by
accident: an unordered dictionary, a `DateTime.Now`, a parallel loop that accumulates in completion
order, a floating-point sum whose order changed.

If you touch dispatch, sizing, costing or input resolution, run the acceptance checks and diff the
artifacts. Only `runId` may differ between two runs of the same scenario.

```bash
dotnet test NEMSweep.Model.Tests --filter FullyQualifiedName~ManualScenarioFixtureTests
```

```bash
dotnet test NEMSweep.Model.Tests -c Release --filter FullyQualifiedName~FullYearSizingAcceptanceTests --logger "console;verbosity=detailed"
```

## Conventions

**Comments say why, not what.** A line or two explaining a decision that is not obvious from the
code. The detail belongs in the commit message.

**Public API needs XML documentation.** The docs site is generated from it, and a missing comment is
a build break, not a warning. State units explicitly, and where a value is easily misread, say what
it is *not*.

**Artifacts are generated, never hand-edited.** If a published file needs to change, change what
produces it and regenerate.

**Schema changes bump the version** in `NEMSweep.Contracts/ArtifactSchemaVersions.cs`, and the
committed schemas under `schema/` are regenerated. CI diffs them and fails if they drift.

**No em dashes** in documentation, site copy, code comments or issue text.

## Tests

New behaviour needs a test that would fail without it. Prefer a test that states the property being
protected over one that restates the implementation: the test names in this repository read as
sentences for that reason.

Fixtures synthesise their own inputs rather than reading committed data, so the suite runs anywhere
and does not depend on artifacts that will one day live elsewhere.

## Commits and pull requests

Explain **why**, not what: the diff already says what. A commit message that states the problem, the
decision and the reason it was made is what makes the change reviewable in a year.

Keep a pull request to one coherent change. If you find something else worth fixing on the way,
that is a second pull request.

## Data

Do not commit raw upstream data. Demand archives, weather files and generation workbooks are
third-party material with their own terms; see [DATA-LICENSE.md](DATA-LICENSE.md). The input bundle
directory is gitignored for that reason.

## Licence

By contributing you agree that your contribution is licensed under
[BSD-3-Clause](LICENSE.md), the same terms as the rest of the code.
