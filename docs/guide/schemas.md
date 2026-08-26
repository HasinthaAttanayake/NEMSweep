# Schemas

The `NEMSweep.CLI` scenario config and sweep definition each have a published JSON Schema
(draft 2020-12). Point an editor at one and you get autocomplete on field names, validation as you
type, and hover documentation. For hand-editing JSON that is the difference between guessing and
being guided.

The schemas describe the two CLI input formats. They say nothing about the framework, which has no
JSON interface of its own.

## Use them

Add `$schema` to the top of your file:

```json
{
  "$schema": "https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/schema/scenario-v5.json",
  "schemaVersion": 5,
  "id": "my-scenario"
}
```

That is all. Every mainstream editor reads `$schema`, fetches it, and validates against it. The
scaffold from `--new-scenario` already declares it, and so does `scenarios/starter-nsw1.json`.

The repository is the host. The raw endpoint serves with a permissive cross-origin header and
editors do not mind that it returns `text/plain`, so putting the file anywhere else would be
presentation rather than capability.

| File | Schema |
|---|---|
| Scenario config | `schema/scenario-v5.json` |
| Sweep definition | `schema/sweep-v1.json` |

File names carry the version, so bumping a schema does not invalidate files pinned to the old one.
Each schema's `$id` is that same versioned URL, which is what keeps a later version from colliding
with this one in a tool that caches schemas by identity.

`--describe-schema scenario` and `--describe-schema sweep` still print the same documents to
standard output, which is the route to take when you want the schema without a network round trip,
or want to hand it to something that cannot fetch a URL.

## `$schema` is a hint, not a field

The loader removes it before deserialising. It describes the document rather than forming part of
it, so it never reaches provenance, a serialised copy, or a hash.

This carve-out is deliberate and narrow. Deserialisation otherwise **rejects unknown properties**
rather than ignoring them, which is what makes a mistyped or invented field fail loudly in seconds.
Without the carve-out you would be choosing between an editor that can validate your file and a file
that loads.

## Keeping them honest

The schemas are generated from the same constants the validator enforces, so a committed copy can
fall behind them silently. CI regenerates both and diffs them against what is committed, and fails
the build if they differ. Regenerate after changing a constant:

```bash
dotnet run --project NEMSweep.CLI -- --describe-schema scenario > schema/scenario-v5.json
```

## What a schema cannot check

A schema checks shape. It cannot check that your numbers mean what you think they mean, and there
is one case worth knowing about.

**`axisValue` is a display value.** Nothing in the model reads it: it positions a point on the
sweep's x-axis and no more. A point labelled +3,000 MW whose overrides actually add 2,900 runs
perfectly and produces a chart that is quietly wrong. The one part of this that can be caught
generically is now caught, because two points cannot both sit at the same axis value and a duplicate
is what a copy-pasted point looks like. Agreement between an axis value and what the overrides
actually change is still yours to check.

See [Sweeps](sweeps.md) for the merge-patch rules the overrides follow.
