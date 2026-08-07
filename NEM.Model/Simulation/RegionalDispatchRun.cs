using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Units;

namespace NEM.Model.Simulation;

internal sealed class RegionalDispatchRun
{
    private readonly Region _region;
    private readonly IStoragePolicy _storagePolicy;
    private readonly FlowSeries _demand;
    private readonly TimeSpan _resolution;
    private readonly GeneratingFleet[] _generatingFleets;
    private readonly Dictionary<GenerationTechnology, FlowSeries> _availableByTechnology;
    private readonly Dictionary<GenerationTechnology, GenerationBudgetState> _budgetByTechnology;
    private readonly Dictionary<GenerationTechnology, double[]> _generationMwByTechnology;
    private readonly Dictionary<GenerationTechnology, double[]> _curtailmentMwByTechnology;
    private readonly Dictionary<GenerationTechnology, double[]> _chargeMwByTechnology;
    private readonly Dictionary<StorageTechnology, StorageFleet> _storageByTechnology;
    private readonly Dictionary<StorageTechnology, Energy> _storageLevelByTechnology;
    private readonly Dictionary<StorageTechnology, double[]> _stateOfChargeMwhByTechnology;
    private readonly double[] _unservedMw;
    private readonly double[] _dischargeMw;

    public RegionalDispatchRun(Region region, IStoragePolicy storagePolicy)
    {
        _region = region;
        _storagePolicy = storagePolicy;
        _demand = region.Demand.TotalDemand;
        _resolution = _demand.Resolution;
        _generatingFleets = region.GeneratingFleets
            .OrderBy(fleet => fleet.ShortRunMarginalCost)
            .ThenBy(fleet => fleet.GenerationTechnology)
            .ToArray();
        _availableByTechnology = _generatingFleets.ToDictionary(
            fleet => fleet.GenerationTechnology,
            fleet => fleet.AvailableCapacityFor(region.ResourceProfile, _demand));
        _budgetByTechnology = _generatingFleets.ToDictionary(
            fleet => fleet.GenerationTechnology,
            fleet => new GenerationBudgetState(fleet));
        _generationMwByTechnology = CreateGenerationSeries();
        _curtailmentMwByTechnology = CreateGenerationSeries();
        _chargeMwByTechnology = CreateGenerationSeries();
        _storageByTechnology = region.StorageFleets.ToDictionary(
            fleet => fleet.StorageTechnology);
        _storageLevelByTechnology = region.StorageFleets.ToDictionary(
            fleet => fleet.StorageTechnology,
            _ => Energy.Zero);
        _stateOfChargeMwhByTechnology = region.StorageFleets.ToDictionary(
            fleet => fleet.StorageTechnology,
            _ => new double[_demand.Length]);
        _unservedMw = new double[_demand.Length];
        _dischargeMw = new double[_demand.Length];
    }

    public DispatchOutcome Execute()
    {
        for (int index = 0; index < _demand.Length; index++)
        {
            RecordStateOfCharge(index);
            DateTimeOffset instant = _demand.InstantAt(index);
            Power remainingDemand = DispatchGeneration(index, instant);
            Power surplus = Power.FromMegawatts(
                _curtailmentMwByTechnology.Values.Sum(values => values[index]));
            StorageDecision decision = _storagePolicy.Decide(
                CreateStorageContext(index, instant, remainingDemand, surplus))
                ?? throw new InvalidOperationException("Storage policy returned no decision.");

            _unservedMw[index] = ExecuteStorage(
                index,
                instant,
                remainingDemand,
                surplus,
                decision).Megawatts;
        }

        return BuildOutcome();
    }

    private void RecordStateOfCharge(int index)
    {
        foreach ((StorageTechnology technology, Energy level) in _storageLevelByTechnology)
        {
            _stateOfChargeMwhByTechnology[technology][index] = level.MegawattHours;
        }
    }

    private Dictionary<GenerationTechnology, double[]> CreateGenerationSeries() =>
        _generatingFleets.ToDictionary(
            fleet => fleet.GenerationTechnology,
            _ => new double[_demand.Length]);

    private Power DispatchGeneration(int index, DateTimeOffset instant)
    {
        Power remainingDemand = _demand[index];
        foreach (GeneratingFleet fleet in _generatingFleets)
        {
            GenerationTechnology technology = fleet.GenerationTechnology;
            Power available = _availableByTechnology[technology][index];
            GenerationBudgetState budget = _budgetByTechnology[technology];
            Power requested = Power.Min(
                Power.Max(Power.Zero, remainingDemand),
                budget.Headroom(available, Power.Zero, instant, _resolution));
            Power delivered = budget.Take(requested, instant, _resolution);

            _generationMwByTechnology[technology][index] = fleet.IsIntermittentRenewable
                ? available.Megawatts
                : delivered.Megawatts;
            _curtailmentMwByTechnology[technology][index] = fleet.IsIntermittentRenewable
                ? available.Megawatts - delivered.Megawatts
                : 0;
            remainingDemand -= delivered;
        }

        return remainingDemand;
    }

    private DispatchContext CreateStorageContext(
        int index,
        DateTimeOffset instant,
        Power remainingDemand,
        Power surplus)
    {
        Power residual = remainingDemand > Power.Zero
            ? remainingDemand
            : surplus * -1;
        StorageFleetSnapshot[] storageSnapshots = _region.StorageFleets
            .Select(fleet => new StorageFleetSnapshot(
                fleet.StorageTechnology,
                _storageLevelByTechnology[fleet.StorageTechnology],
                fleet.ChargeHeadroom(
                    _storageLevelByTechnology[fleet.StorageTechnology],
                    _resolution),
                fleet.DischargeHeadroom(
                    _storageLevelByTechnology[fleet.StorageTechnology],
                    _resolution)))
            .ToArray();
        GenerationFleetSnapshot[] generationSnapshots = _generatingFleets
            .Select(fleet => new GenerationFleetSnapshot(
                fleet.GenerationTechnology,
                fleet.IsIntermittentRenewable
                    ? Power.Zero
                    : _budgetByTechnology[fleet.GenerationTechnology].Headroom(
                        _availableByTechnology[fleet.GenerationTechnology][index],
                        Power.FromMegawatts(
                            _generationMwByTechnology[fleet.GenerationTechnology][index]),
                        instant,
                        _resolution)))
            .ToArray();

        return new DispatchContext(
            residual,
            storageSnapshots,
            generationSnapshots,
            _resolution);
    }

    private Power ExecuteStorage(
        int index,
        DateTimeOffset instant,
        Power remainingDemand,
        Power surplus,
        StorageDecision decision)
    {
        Power remainingDeficit = Power.Max(Power.Zero, remainingDemand);
        Power remainingSurplus = surplus;
        foreach (StorageIntent intent in decision.Intents)
        {
            if (!_storageByTechnology.TryGetValue(intent.StorageTechnology, out StorageFleet? fleet))
            {
                throw new InvalidOperationException(
                    $"Storage policy returned an intent for unknown fleet {intent.StorageTechnology}.");
            }

            Energy storageLevel = _storageLevelByTechnology[intent.StorageTechnology];
            if (intent.RequestedFlow > Power.Zero)
            {
                Power requested = Power.Min(intent.RequestedFlow, remainingDeficit);
                if (requested == Power.Zero)
                {
                    continue;
                }

                StorageOutcome outcome = fleet.Operate(storageLevel, requested, _resolution);
                _storageLevelByTechnology[intent.StorageTechnology] = outcome.FinalStorageLevel;
                remainingDeficit -= outcome.DeliveredFlow;
                _dischargeMw[index] += outcome.DeliveredFlow.Megawatts;
                continue;
            }

            if (remainingDeficit > Power.Zero)
            {
                continue;
            }

            Power requestedCharge = intent.RequestedFlow * -1;
            if (intent.ChargeSource == ChargeSource.Surplus)
            {
                remainingSurplus = ChargeFromSurplus(
                    index,
                    fleet,
                    storageLevel,
                    requestedCharge,
                    remainingSurplus);
                continue;
            }

            ChargeFromIncrementalGeneration(
                index,
                instant,
                fleet,
                storageLevel,
                requestedCharge,
                intent);
        }

        return remainingDeficit;
    }

    private Power ChargeFromSurplus(
        int index,
        StorageFleet fleet,
        Energy storageLevel,
        Power requestedCharge,
        Power remainingSurplus)
    {
        requestedCharge = Power.Min(requestedCharge, remainingSurplus);
        if (requestedCharge == Power.Zero)
        {
            return remainingSurplus;
        }

        StorageOutcome outcome = fleet.Operate(
            storageLevel,
            requestedCharge * -1,
            _resolution);
        Power actualCharge = outcome.DeliveredFlow * -1;
        _storageLevelByTechnology[fleet.StorageTechnology] = outcome.FinalStorageLevel;
        ReduceCurtailment(index, actualCharge);
        return remainingSurplus - actualCharge;
    }

    private void ChargeFromIncrementalGeneration(
        int index,
        DateTimeOffset instant,
        StorageFleet fleet,
        Energy storageLevel,
        Power requestedCharge,
        StorageIntent intent)
    {
        GenerationTechnology sourceTechnology = intent.ChargeSource!.Value.GenerationTechnology
            ?? throw new InvalidOperationException(
                "Incremental-generation charging must identify a generation technology.");
        GeneratingFleet? sourceFleet = _generatingFleets.SingleOrDefault(
            candidate => candidate.GenerationTechnology == sourceTechnology);
        if (sourceFleet is null)
        {
            throw new InvalidOperationException(
                $"Storage policy named unknown generation source {sourceTechnology}.");
        }

        Power generated = Power.FromMegawatts(
            _generationMwByTechnology[sourceTechnology][index]);
        Power sourceHeadroom = sourceFleet.IsIntermittentRenewable
            ? Power.Zero
            : _budgetByTechnology[sourceTechnology].Headroom(
                _availableByTechnology[sourceTechnology][index],
                generated,
                instant,
                _resolution);
        requestedCharge = Power.Min(requestedCharge, sourceHeadroom);
        if (requestedCharge == Power.Zero)
        {
            return;
        }

        StorageOutcome outcome = fleet.Operate(
            storageLevel,
            requestedCharge * -1,
            _resolution);
        Power actualCharge = outcome.DeliveredFlow * -1;
        Power additionalGeneration = _budgetByTechnology[sourceTechnology].Take(
            actualCharge,
            instant,
            _resolution);
        _storageLevelByTechnology[fleet.StorageTechnology] = outcome.FinalStorageLevel;
        _generationMwByTechnology[sourceTechnology][index] += additionalGeneration.Megawatts;
        _chargeMwByTechnology[sourceTechnology][index] += additionalGeneration.Megawatts;
    }

    private void ReduceCurtailment(int index, Power charge)
    {
        double remainingMw = charge.Megawatts;
        foreach (GeneratingFleet fleet in _generatingFleets)
        {
            GenerationTechnology technology = fleet.GenerationTechnology;
            double[] curtailmentMw = _curtailmentMwByTechnology[technology];
            double reductionMw = Math.Min(remainingMw, curtailmentMw[index]);
            curtailmentMw[index] -= reductionMw;
            _chargeMwByTechnology[technology][index] += reductionMw;
            remainingMw -= reductionMw;
            if (remainingMw <= 0)
            {
                return;
            }
        }
    }

    private DispatchOutcome BuildOutcome()
    {
        var perFleetGeneration = _generationMwByTechnology.ToDictionary(
            entry => entry.Key,
            entry => Flow(entry.Value));
        var perFleetCurtailment = _curtailmentMwByTechnology.ToDictionary(
            entry => entry.Key,
            entry => Flow(entry.Value));
        var perFleetCharge = _chargeMwByTechnology.ToDictionary(
            entry => entry.Key,
            entry => Flow(entry.Value));
        var perFleetDelivered = _generationMwByTechnology.ToDictionary(
            entry => entry.Key,
            entry => Flow(entry.Value.Select((generationMw, index) =>
                generationMw
                - _curtailmentMwByTechnology[entry.Key][index]
                - _chargeMwByTechnology[entry.Key][index]).ToArray()));
        var chargeMw = new double[_demand.Length];
        for (int index = 0; index < chargeMw.Length; index++)
        {
            chargeMw[index] = _chargeMwByTechnology.Values.Sum(values => values[index]);
        }

        FlowSeries charge = Flow(chargeMw);
        var stateOfChargeByTechnology = _stateOfChargeMwhByTechnology.ToDictionary(
            entry => entry.Key,
            entry => new StockSeries(_demand.Start, _resolution, entry.Value));
        FlowSeries zeroFlow = Flow(new double[_demand.Length]);
        return new DispatchOutcome(
            regionId: _region.RegionId,
            perFleetGeneration,
            perFleetCurtailment,
            perFleetDelivered,
            perFleetCharge,
            demand: _demand,
            unserved: Flow(_unservedMw),
            charge,
            discharge: Flow(_dischargeMw),
            imports: zeroFlow,
            exports: zeroFlow,
            stateOfChargeByTechnology);
    }

    private FlowSeries Flow(double[] values) =>
        new(_demand.Start, _resolution, values);
}