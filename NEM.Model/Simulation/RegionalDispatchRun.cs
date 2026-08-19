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
    private readonly GeneratingFleet? _hydroFleet;
    private readonly HydroReservationState? _hydroReservation;
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
    private Power _currentResidualDemand;
    private Power _hydroPacedCap;

    public RegionalDispatchRun(
        Region region,
        IStoragePolicy storagePolicy)
    {
        _region = region;
        _storagePolicy = storagePolicy;
        _demand = region.Demand.TotalDemand;
        _resolution = _demand.Resolution;
        _generatingFleets = GenerationMeritOrder.Sort(region.GeneratingFleets).ToArray();
        _hydroFleet = _generatingFleets.SingleOrDefault(
            fleet => fleet.GenerationTechnology == GenerationTechnology.Hydro);
        _hydroReservation = _hydroFleet is null ? null : new HydroReservationState();
        _availableByTechnology = _generatingFleets.ToDictionary(
            fleet => fleet.GenerationTechnology,
            fleet => fleet.AvailableCapacityFor(region.ResourceProfile, _demand));
        _budgetByTechnology = _generatingFleets.ToDictionary(
            fleet => fleet.GenerationTechnology,
            fleet => new GenerationBudgetState(
                fleet,
                fleet.GenerationTechnology == GenerationTechnology.Hydro
                    ? HydroReservationState.ReserveFraction
                    : 0));
        _generationMwByTechnology = CreateGenerationSeries();
        _curtailmentMwByTechnology = CreateGenerationSeries();
        _chargeMwByTechnology = CreateGenerationSeries();
        _storageByTechnology = region.StorageFleets.ToDictionary(
            fleet => fleet.StorageTechnology);
        _storageLevelByTechnology = region.StorageFleets.ToDictionary(
            fleet => fleet.StorageTechnology,
            fleet => fleet.SeedEnergy);
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

    /// <summary>
    /// How much of a month must remain for Hydro's reserve share to still be worth holding
    /// back as a last-resort backstop. Expressed as a duration rather than an interval count
    /// so the behaviour is identical at hourly and sub-hourly resolution. Three days is long
    /// enough for the pacer to place the released energy on real peaks - even a fleet
    /// reserving 10% of a month runs well under nameplate over 72 hours - without giving up
    /// the backstop for a materially long stretch of the month.
    /// </summary>
    private static readonly TimeSpan ReserveReleaseWindow = TimeSpan.FromDays(3);

    public string RegionId => _region.RegionId;

    public int Length => _demand.Length;

    /// <summary>Deficit still outstanding in the interval being processed.</summary>
    public Power CurrentDeficit => _intervalDeficit;

    /// <summary>
    /// Opens an interval. Records the opening state of charge, which is what the outcome
    /// reports, and must be called before any other step for that interval. Also computes
    /// this interval's residual demand (demand net of intermittent renewables) and Hydro's
    /// paced offtake cap once, up front: residual demand is the only causal signal
    /// <see cref="HydroReservationState"/> is allowed to see, and both are then reused
    /// unchanged for the rest of the interval (generation, exports, storage charging) so
    /// every phase paces Hydro against the same allowance.
    ///
    /// The cap must be settled here rather than recomputed per phase. Recomputing it later
    /// in the interval would price it against a paced pool this interval's own local dispatch
    /// has already drawn down, and against a trailing window this interval's own
    /// <see cref="HydroReservationState.Observe"/> has already joined - so exports and storage
    /// charging would silently see a smaller allowance than local demand did, for no modelled
    /// reason.
    /// </summary>
    public void BeginInterval(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _demand.Length);
        _currentIndex = index;
        _currentInstant = _demand.InstantAt(index);
        _intervalDeficit = Power.Zero;
        _currentResidualDemand = ResidualDemandExcludingIntermittents(index);
        ReleaseHydroReserveIfMonthIsEnding();
        _hydroPacedCap = HydroPacedCap();
        RecordStateOfCharge(index);
    }

    /// <summary>
    /// Hands Hydro's unspent reserve share to the pacer once too little of the month remains
    /// for a last-resort backstop to be worth holding. Unspent reserve expires at the month
    /// boundary (see <see cref="GenerationBudgetState.ReleaseUnspentReserve"/>), so past this
    /// point holding it only guarantees the energy is wasted, while releasing it lets the
    /// pacer spend it on the window's highest residual-demand hours. The trade is a genuine
    /// one - the final <see cref="ReserveReleaseWindow"/> of each month has no reserve-funded
    /// backstop left - but the released energy re-enters merit order ahead of storage, so it
    /// meets those hours' demand earlier rather than later. NEM-076.
    /// </summary>
    private void ReleaseHydroReserveIfMonthIsEnding()
    {
        if (_hydroFleet is null)
        {
            return;
        }

        int intervalsLeft = IntervalsLeftInMonth(_currentInstant, _resolution);
        if (intervalsLeft > ReserveReleaseWindow / _resolution)
        {
            return;
        }

        _budgetByTechnology[_hydroFleet.GenerationTechnology]
            .ReleaseUnspentReserve(_currentInstant);
    }

    /// <summary>
    /// This interval's paced offtake cap for conventional Hydro, or <see cref="Power.Zero"/>
    /// where the region has no Hydro fleet. Evaluated once per interval from
    /// <see cref="BeginInterval"/>; every consumer reads the stored value.
    /// </summary>
    private Power HydroPacedCap()
    {
        if (_hydroFleet is null || _hydroReservation is null)
        {
            return Power.Zero;
        }

        GenerationTechnology technology = _hydroFleet.GenerationTechnology;
        return _hydroReservation.OfftakeCap(
            _hydroFleet.NameplateCapacity,
            _budgetByTechnology[technology].PacedRemaining(_currentInstant),
            IntervalsLeftInMonth(_currentInstant, _resolution),
            _currentResidualDemand,
            _resolution);
    }

    /// <summary>
    /// Dispatches generation in merit order and returns the unmet demand. Conventional
    /// Hydro's request is paced against its monthly budget by
    /// <see cref="HydroReservationState"/> rather than dispatched greedily - see
    /// <see cref="DispatchGeneration(int, DateTimeOffset)"/>.
    /// </summary>
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
    /// after transfer. Conventional Hydro's incremental headroom here is paced exactly like
    /// its local dispatch (see <see cref="IncrementalHeadroom"/>) - serving an export can
    /// substitute for local demand this interval, but never draws on budget beyond what
    /// pacing would have allowed locally anyway.
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
    /// dispatches Hydro's reserve share as a final backstop against whatever deficit storage
    /// could not cover, and books the leftover deficit as unserved energy.
    /// </summary>
    public void CompleteInterval()
    {
        RequireOpenInterval();
        Power surplus = Power.FromMegawatts(
            _curtailmentMwByTechnology.Values.Sum(values => values[_currentIndex]));
        StorageDecision decision = _storagePolicy.Decide(
            CreateStorageContext(_intervalDeficit, surplus))
            ?? throw new InvalidOperationException("Storage policy returned no decision.");

        Power remainingDeficit = ExecuteStorage(
            _currentIndex,
            _currentInstant,
            _intervalDeficit,
            surplus,
            decision);
        remainingDeficit = DispatchHydroFallback(_currentIndex, remainingDeficit);

        _unservedMw[_currentIndex] = remainingDeficit.Megawatts;
        _currentIndex = -1;
    }

    /// <summary>
    /// Dispatches conventional Hydro's RESERVE share (see <see cref="GenerationBudgetState"/>,
    /// <see cref="HydroReservationState.ReserveFraction"/>) as a final, local-only backstop
    /// for whatever deficit storage could not cover this interval. By the time this runs,
    /// generation, transfer, and storage have already completed for the interval, so this
    /// energy can never be exported or used to charge storage. This is the true last-resort
    /// share; the other 90% of the budget is paced through normal merit-order dispatch (see
    /// <see cref="DispatchGeneration(int, DateTimeOffset)"/>) rather than held back
    /// entirely - see <see cref="StorageSeedPolicy"/> for the paired storage-seed assumption.
    /// NEM-076.
    /// </summary>
    private Power DispatchHydroFallback(int index, Power remainingDeficit)
    {
        if (_hydroFleet is null || remainingDeficit <= Power.Zero)
        {
            return remainingDeficit;
        }

        GenerationTechnology technology = _hydroFleet.GenerationTechnology;
        GenerationBudgetState budget = _budgetByTechnology[technology];
        Power generated = Power.FromMegawatts(_generationMwByTechnology[technology][index]);
        Power available = _availableByTechnology[technology][index];
        Power requested = Power.Min(
            remainingDeficit,
            budget.ReserveHeadroom(available, generated, _currentInstant, _resolution));
        Power delivered = budget.TakeReserve(requested, _currentInstant, _resolution);
        _generationMwByTechnology[technology][index] += delivered.Megawatts;
        return remainingDeficit - delivered;
    }

    private void RequireOpenInterval()
    {
        if (_currentIndex < 0)
        {
            throw new InvalidOperationException(
                $"No interval is open for region '{_region.RegionId}'.");
        }
    }

    /// <summary>
    /// Incremental generation headroom available this interval - used for exports and
    /// storage's incremental-generation charging. For conventional Hydro this is capped to
    /// whatever of this interval's paced allowance (see <see cref="HydroReservationState"/>)
    /// hasn't already been dispatched locally, not the full remaining paced-pool budget. That
    /// is a deliberate choice, not an oversight: without it, an export or a battery charge
    /// could drain budget paced for a future local peak, since neither is metered against
    /// residual demand the way local dispatch is. The allowance is the one settled in
    /// <see cref="BeginInterval"/>, so exports and storage charging see exactly what local
    /// demand saw. The reserve share
    /// (<see cref="GenerationBudgetState.ReserveHeadroom"/>) is never included here at all -
    /// it is reachable only from <see cref="DispatchHydroFallback"/>, after storage.
    /// </summary>
    private Power IncrementalHeadroom(GenerationTechnology technology)
    {
        Power rawHeadroom = _budgetByTechnology[technology].Headroom(
            _availableByTechnology[technology][_currentIndex],
            Power.FromMegawatts(_generationMwByTechnology[technology][_currentIndex]),
            _currentInstant,
            _resolution);

        if (technology != GenerationTechnology.Hydro || _hydroReservation is null)
        {
            return rawHeadroom;
        }

        Power alreadyDispatched = Power.FromMegawatts(
            _generationMwByTechnology[technology][_currentIndex]);
        Power remainingPacedThisInterval = Power.Max(
            Power.Zero,
            _hydroPacedCap - alreadyDispatched);
        return Power.Min(rawHeadroom, remainingPacedThisInterval);
    }

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

    /// <summary>
    /// This interval's demand net of intermittent-renewable availability - the only signal
    /// <see cref="HydroReservationState"/> is allowed to see. Computed once per interval in
    /// <see cref="BeginInterval"/>, before any generation is dispatched, so it never depends
    /// on dispatch order or on anything beyond the interval itself.
    /// </summary>
    private Power ResidualDemandExcludingIntermittents(int index)
    {
        Power residual = _demand[index];
        foreach (GeneratingFleet fleet in _generatingFleets)
        {
            if (fleet.IsIntermittentRenewable)
            {
                residual -= _availableByTechnology[fleet.GenerationTechnology][index];
            }
        }

        return Power.Max(Power.Zero, residual);
    }

    /// <summary>Whole intervals remaining in <paramref name="instant"/>'s calendar month, including it.</summary>
    private static int IntervalsLeftInMonth(DateTimeOffset instant, TimeSpan resolution)
    {
        var monthEndExclusive = new DateTimeOffset(
            instant.Year, instant.Month, 1, 0, 0, 0, instant.Offset).AddMonths(1);
        TimeSpan remaining = monthEndExclusive - instant;
        return (int)Math.Round(remaining / resolution);
    }

    /// <summary>
    /// Dispatches generation in merit order against local demand. Conventional Hydro's
    /// request is capped by <see cref="HydroReservationState"/> at
    /// <c>max(0, residualDemand - T)</c> for a threshold T paced against its remaining
    /// monthly budget (see <see cref="ResidualDemandExcludingIntermittents"/>,
    /// <see cref="HydroReservationState.OfftakeCap"/>) - a deliberate, causal departure from
    /// pure merit-order greed for this one technology, not a change to the ordering itself.
    /// The current interval's residual demand is recorded via <see cref="HydroReservationState.Observe"/>
    /// at the end, for future intervals' pacing only.
    /// </summary>
    private Power DispatchGeneration(int index, DateTimeOffset instant)
    {
        Power remainingDemand = _demand[index];
        foreach (GeneratingFleet fleet in _generatingFleets)
        {
            GenerationTechnology technology = fleet.GenerationTechnology;
            Power available = _availableByTechnology[technology][index];
            GenerationBudgetState budget = _budgetByTechnology[technology];
            Power demandCap = Power.Max(Power.Zero, remainingDemand);

            if (technology == GenerationTechnology.Hydro && _hydroReservation is not null)
            {
                demandCap = Power.Min(demandCap, _hydroPacedCap);
            }

            Power requested = Power.Min(
                demandCap,
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

        _hydroReservation?.Observe(_currentResidualDemand);
        return remainingDemand;
    }

    /// <summary>
    /// Snapshots the open interval for the storage policy. Reads the interval's own state
    /// directly (<see cref="IncrementalHeadroom"/> and the storage levels are already scoped
    /// to it), so it takes no index or instant - passing one that disagreed with the open
    /// interval would have been silently ignored.
    /// </summary>
    private DispatchContext CreateStorageContext(Power remainingDemand, Power surplus)
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
                    : IncrementalHeadroom(fleet.GenerationTechnology),
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

        Power sourceHeadroom = sourceFleet.IsIntermittentRenewable
            ? Power.Zero
            : IncrementalHeadroom(sourceTechnology);
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
