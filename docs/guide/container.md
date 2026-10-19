# Running the container

The container is route B of the three in the
[README's Install section](https://github.com/HasinthaAttanayake/NEMSweep#install). It is the one
to reach for if you would rather not install a toolchain, or if your organisation would rather you
did not. Either way you are running `NEMSweep.CLI`; the image
is that tool and a .NET runtime, and nothing else.

The image is published to the GitHub Container Registry as
`ghcr.io/hasinthaattanayake/nemsweep`, tagged `latest` and by the commit SHA it was built from. It
is **not** tagged with a release version. Use `:latest`, a published commit tag, or a digest.

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
`./study` on your own machine. The image runs as a non-root user, but host ownership and write
permissions depend on your runtime's user mapping and the mounted directory's permissions.

To bring your own scenarios and sweeps, mount the directory holding them and name them by absolute
path:

```bash
podman run --rm -v ./my-study:/work:ro -v ./reference:/data:ro -v ./study:/out ghcr.io/hasinthaattanayake/nemsweep:latest --run-scenario /work/my-scenario.json
```

Use an absolute path inside the container to avoid depending on its working directory. Relative
command-line paths resolve against that directory, not automatically against your mount; a runtime
working-directory override can change it. `/work` above is just a name; mount wherever you like and
point at it.

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

### Prepare a starter inside the container

You do not need a host .NET installation to prepare a starter. Mount a writable parent directory
and give quick-start a child directory that does not exist yet:

```bash
docker run --rm -v "$PWD:/work" ghcr.io/hasinthaattanayake/nemsweep:latest --quick-start simple /work/study
```

The container needs network access to download the starter. Do not use `/work` itself as the
destination: it already exists as the mount point. Preparation downloads, ingests and validates the
inputs; it does not run a simulation.

Run the prepared scenario with read-only inputs and writable output:

```bash
docker run --rm -v "$PWD/study:/work:ro" -v "$PWD/study/out:/out" ghcr.io/hasinthaattanayake/nemsweep:latest --run-scenario /work/scenarios/sized-scenario.json --data-root /work/data --output /out
```

## What is in the image

Framework-dependent on a chiselled .NET runtime base. Two consequences worth knowing:

- A .NET security fix requires a new image using the patched runtime base, but does not require
  recompiling the application. Pulling an existing image does not update its runtime layers;
  check that a newer image was published with the fix.
- The base is minimal, which keeps the CVE surface small enough to clear a registry scanner, and it
  runs as a non-root user by default.

The image is not trimmed and not AOT-compiled. The workbook and archive readers resolve types by
reflection, so trimming would break ingestion at run time while leaving the build green, and a
smaller image is not worth a feature that fails only in front of a user.

There is no Dockerfile. The image is described by `NEMSweep.CLI.csproj` and built by the SDK, so
there is no second description of the application to keep in step with the first.

## It is the same model

A container run and a host run using the same CLI build and unchanged scenario and inputs reproduce
the same **modelled values**. A leaf-by-leaf check across a Windows host and the Linux image found
only `runId` differed in that setup. Provenance can also differ when build versions or input-path
layouts differ. Check `--version` on both: `latest` is a moving tag, not a promise that it matches
the installed .NET tool release.

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
