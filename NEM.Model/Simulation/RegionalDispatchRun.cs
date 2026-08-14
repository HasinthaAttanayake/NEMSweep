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
    private readonly double[] _importsMw;
    private readonly double[] _exportsMw;
    private int _currentIndex = -1;
    private DateTimeOffset _currentInstant;
    private Power _intervalDeficit;

    public RegionalDispatchRun(
        Region region,
        IStoragePolicy storagePolicy)
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
        _importsMw = new double[_demand.Length];
        _exportsMw = new double[_demand.Length];
    }

    /// <summary>Quantities below this are treated as zero when reconciling transfers.</summary>
    private const double Tolerance = 1e-9;

    public string RegionId => _region.RegionId;

    public int Length => _demand.Length;

    /// <summary>Deficit still outstanding in the interval being processed.</summary>
    public Power CurrentDeficit => _intervalDeficit;

    /// <summary>
    /// Opens an interval. Records the opening state of charge, which is what the outcome
    /// reports, and must be called before any other step for that interval.
    /// </summary>
    public void BeginInterval(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _demand.Length);
        _currentIndex = index;
        _currentInstant = _demand.InstantAt(index);
        _intervalDeficit = Power.Zero;
        RecordStateOfCharge(index);
    }

    /// <summary>Dispatches generation in merit order and returns the unmet demand.</summary>
    public Power DispatchGeneration()
    {
        RequireOpenInterval();
        _intervalDeficit = DispatchGeneration(_currentIndex, _currentInstant);
        return _intervalDeficit;
    }

    /// <summary>
    /// Power this region could deliver to another region: renewable output already being
    /// spilled, plus headroom on dispatchable plant that could be started to serve an
    /// export. Pumped hydro is excluded because it is storage, and storage is decided
    /// after transfer.
    /// </summary>
    public Power ExportableSurplus()
    {
        RequireOpenInterval();
        if (_intervalDeficit > Power.Zero)
        {
            return Power.Zero;
        }

        Power exportable = Power.FromMegawatts(
            _curtailmentMwByTechnology.Values.Sum(values => values[_currentIndex]));
        foreach (GeneratingFleet fleet in _generatingFleets)
        {
            if (fleet.IsIntermittentRenewable)
            {
                continue;
            }

            exportable += IncrementalHeadroom(fleet.GenerationTechnology);
        }

        return exportable;
    }

    /// <summary>Books energy received from another region against this interval's deficit.</summary>
    public void ApplyImport(Power delivered)
    {
        RequireOpenInterval();
        if (delivered <= Power.Zero)
        {
            return;
        }

        Power remainingDeficit = _intervalDeficit - delivered;
        if (remainingDeficit.Megawatts < -Tolerance)
        {
            throw new InvalidOperationException(
                $"Region '{_region.RegionId}' was sent more energy than it needed at index "
                + $"{_currentIndex}.");
        }

        _importsMw[_currentIndex] += delivered.Megawatts;
        _intervalDeficit = remainingDeficit < Power.Zero ? Power.Zero : remainingDeficit;
    }

    /// <summary>
    /// Books energy sent to another region, drawing first on renewable output that would
    /// otherwise be spilled and then on dispatchable headroom in merit order.
    /// </summary>
    public void ApplyExport(Power sent)
    {
        RequireOpenInterval();
        if (sent <= Power.Zero)
        {
            return;
        }

        Power outstanding = sent - ExportFromCurtailment(sent);
        if (outstanding > Power.Zero)
        {
            outstanding -= ExportFromIncrementalGeneration(outstanding);
        }

        if (outstanding.Megawatts > Tolerance)
        {
            throw new InvalidOperationException(
                $"Region '{_region.RegionId}' was asked to export {sent.Megawatts} MW at index "
                + $"{_currentIndex} but could only source {(sent - outstanding).Megawatts} MW.");
        }

        _exportsMw[_currentIndex] += sent.Megawatts;
    }

    /// <summary>
    /// Runs the storage policy against whatever deficit or surplus remains after transfer,
    /// and books the leftover deficit as unserved energy.
    /// </summary>
    public void CompleteInterval()
    {
        RequireOpenInterval();
        Power surplus = Power.FromMegawatts(
            _curtailmentMwByTechnology.Values.Sum(values => values[_currentIndex]));
        StorageDecision decision = _storagePolicy.Decide(
            CreateStorageContext(_currentIndex, _currentInstant, _intervalDeficit, surplus))
            ?? throw new InvalidOperationException("Storage policy returned no decision.");

        _unservedMw[_currentIndex] = ExecuteStorage(
            _currentIndex,
            _currentInstant,
            _intervalDeficit,
            surplus,
            decision).Megawatts;
        _currentIndex = -1;
    }

    private void RequireOpenInterval()
    {
        if (_currentIndex < 0)
        {
            throw new InvalidOperationException(
                $"No interval is open for region '{_region.RegionId}'.");
        }
    }

    private Power IncrementalHeadroom(GenerationTechnology technology) =>
        _budgetByTechnology[technology].Headroom(
            _availableByTechnology[technology][_currentIndex],
            Power.FromMegawatts(_generationMwByTechnology[technology][_currentIndex]),
            _currentInstant,
            _resolution);

    /// <summary>
    /// Moves spilled renewable output into exports, cheapest first. Generation is
    /// unchanged; the energy simply stops being curtailed, so the derived per-fleet
    /// delivered figure absorbs it.
    /// </summary>
    private Power ExportFromCurtailment(Power request)
    {
        double outstandingMw = request.Megawatts;
        double takenMw = 0;
        foreach (GeneratingFleet fleet in _generatingFleets)
        {
            double[] curtailmentMw = _curtailmentMwByTechnology[fleet.GenerationTechnology];
            double reductionMw = Math.Min(outstandingMw, curtailmentMw[_currentIndex]);
            curtailmentMw[_currentIndex] -= reductionMw;
            outstandingMw -= reductionMw;
            takenMw += reductionMw;
            if (outstandingMw <= Tolerance)
            {
                break;
            }
        }

        return Power.FromMegawatts(takenMw);
    }

    /// <summary>
    /// Starts dispatchable plant specifically to serve an export, in ascending short-run
    /// marginal cost. Generation rises and the energy leaves as an export, so both sides
    /// of the regional balance move together.
    /// </summary>
    private Power ExportFromIncrementalGeneration(Power request)
    {
        double outstandingMw = request.Megawatts;
        double takenMw = 0;
        foreach (GeneratingFleet fleet in _generatingFleets)
        {
            if (fleet.IsIntermittentRenewable)
            {
                continue;
            }

            GenerationTechnology technology = fleet.GenerationTechnology;
            Power requested = Power.Min(
                Power.FromMegawatts(outstandingMw),
                IncrementalHeadroom(technology));
            if (requested <= Power.Zero)
            {
                continue;
            }

            Power accepted = _budgetByTechnology[technology].Take(
                requested,
                _currentInstant,
                _resolution);
            _generationMwByTechnology[technology][_currentIndex] += accepted.Megawatts;
            outstandingMw -= accepted.Megawatts;
            takenMw += accepted.Megawatts;
            if (outstandingMw <= Tolerance)
            {
                break;
            }
        }

        return Power.FromMegawatts(takenMw);
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
                        _resolution),
                fleet.ShortRunMarginalCost))
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

    public DispatchOutcome BuildOutcome()
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
            imports: Flow(_importsMw),
            exports: Flow(_exportsMw),
            stateOfChargeByTechnology,
            demandProfile: _region.Demand);
    }

    private FlowSeries Flow(double[] values) =>
        new(_demand.Start, _resolution, values);
}