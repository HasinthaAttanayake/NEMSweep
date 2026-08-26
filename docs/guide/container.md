# Running the container

There are two ways to run NEMSweep: clone the repository and run it with the .NET SDK, or run the
container image. The image is the one to reach for if you would rather not install a toolchain, or
if your organisation would rather you did not.

## What you need

Any [OCI](https://opencontainers.org/) runtime. Docker and Podman are both first-class here; the
image is an ordinary OCI image, so anything that reads the spec will run it.

```bash
podman pull ghcr.io/hasinthaattanayake/nemsweep:latest
```

```bash
docker pull ghcr.io/hasinthaattanayake/nemsweep:latest
```

Images are published for `linux/amd64` and `linux/arm64`, so Apple Silicon is covered without
emulation.

## The two mounts

A run reads inputs from one directory and writes results to another, and the image defaults them to
`/data` and `/out`. Mount yours over those and there is nothing else to configure:

```bash
podman run --rm -v ./reference:/data:ro -v ./study:/out ghcr.io/hasinthaattanayake/nemsweep:latest --run-scenario /data/my-scenario.json
```

The data mount is read-only because nothing in a scenario run writes to it. Results appear in
`./study` on your own machine, owned by you: the image runs as a non-root user, so bind-mounted
output does not come back owned by root.

To bring your own scenarios and sweeps, mount the directory holding them and name them by absolute
path:

```bash
podman run --rm -v ./my-study:/work:ro -v ./reference:/data:ro -v ./study:/out ghcr.io/hasinthaattanayake/nemsweep:latest --run-scenario /work/my-scenario.json
```

Use an absolute path inside the container rather than a relative one. Relative paths resolve against
the container's working directory, which is where the application itself lives, and that is not
where your files are. `/work` above is just a name; mount wherever you like and point at it.

`--data-root` and `--output` override the defaults per run, exactly as they do outside a container.
See [the workspace](cli.md#the-workspace).

## Where the data comes from

**The image contains no model data.** That is deliberate. The demand, weather and generation
artifacts a scenario reads are inputs, not part of the tool: they carry their own provenance and
their own licensing, they change on a different cadence to the code, and the choice of weather site
behind them is an editorial judgement rather than something the model should assert. Bundling them
would quietly turn one set of choices into the default nobody questions.

So you bring them, and mount them at `/data`. Either build your own from an
[input bundle](input-bundles.md) with `--ingest`, or use a published reference set.

## What is in the image

Framework-dependent on a chiselled .NET runtime base. Two consequences worth knowing:

- A .NET security fix is a **rebase, not a rebuild**. Pull a newer tag and you have the patched
  runtime, without the application being recompiled around it.
- The base is minimal, which keeps the CVE surface small enough to clear a registry scanner, and it
  runs as a non-root user by default.

The image is not trimmed and not AOT-compiled. The workbook and archive readers resolve types by
reflection, so trimming would break ingestion at run time while leaving the build green, and a
smaller image is not worth a feature that fails only in front of a user.

There is no Dockerfile. The image is described by `NEMSweep.CLI.csproj` and built by the SDK, so
there is no second description of the application to keep in step with the first.

## It is the same model

A container run and a host run of the same scenario against the same inputs produce **identical
results**. Verified leaf by leaf across a Windows host and the Linux image: every modelled value
matches, and only `runId` differs, plus the `provenance` block, which is absent in the container
because there is no git working tree inside it.

That is worth knowing before you trust a figure produced one way and compared against a figure
produced the other.

## Reproducibility

A result already records the SHA-256 of every input byte it read and the commit the model was built
from. An image digest completes that chain by pinning the runtime the model executed on, which is
the one part a loose build leaves open. Pull by digest rather than by tag when a run needs to be
reproducible later:

```bash
podman run --rm -v ./reference:/data:ro -v ./study:/out ghcr.io/hasinthaattanayake/nemsweep@sha256:... --run-scenario /data/my-scenario.json
```

## See also

- [CLI reference](cli.md): every command, and how the workspace roots are chosen.
- [Input bundles](input-bundles.md): building your own data from upstream sources.
- [Outputs and provenance](outputs.md): what a run writes, and what it records about itself.
