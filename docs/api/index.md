# API reference

Generated from the XML documentation comments in the source. Two assemblies are published here.

## NEM.Model

The domain model: scenarios, the realised power system, dispatch, storage sizing, economics, and the
typed quantities everything is expressed in. This is the library you would reference to run the
model from your own code.

Namespaces worth starting from:

| Namespace | What lives there |
|---|---|
| `NEM.Model.Scenarios` | Scenario intent, and `ScenarioDerivation` which realises it |
| `NEM.Model.Grid` | The realised system: regions, fleets, storage, interconnectors, and the technology vocabulary |
| `NEM.Model.Simulation` | `Dispatcher`, dispatch evidence, reliability, and the `IStoragePolicy` extension point |
| `NEM.Model.StorageSizing` | The sizing search, its options, and its results |
| `NEM.Model.Economics` | Annuitisation and system costing |
| `NEM.Model.Units` | Typed quantities. No bare `double` crosses a domain boundary |
| `NEM.Model.Series`, `NEM.Model.Weather`, `NEM.Model.Generation` | Time series, weather resources, and the solar and wind power curves |

Internal types are not published. The dispatch run, the merit-order sort, the hydro pacer and the
max-flow algorithms are implementation detail behind `Dispatcher`; they are described in
[Dispatch](../concepts/dispatch.md) and the [domain model reference](../domain-model.md).

## NEM.Contracts

The published artifact schema — the shapes of the JSON that a run writes and that any consumer
reads. If you are working with NemSim's output rather than calling its model, this is the reference
you want.

`ArtifactSchemaVersions` carries the current version of every artifact. `SweepScalarCatalog` names
every scalar a sweep publishes, with its label and unit.

See [Outputs and provenance](../guide/outputs.md) for how the artifacts fit together on disk.

## Reading these pages

Documentation comments state units explicitly and, where a value is easily misread, say what it is
**not**. Those negatives are load-bearing: several figures the model publishes are consistent
bookkeeping allocations rather than physical measurements, and the distinction changes what a
number can be used for.

Before drawing conclusions from any value here, read
[Limitations](../assumptions/limitations.md).
