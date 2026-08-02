using NEM.Model.Grid;
using NEM.Model.Simulation;
using NEM.Model.Units;

namespace NEM.Model.StorageSizing;

internal sealed class StorageSizingSearch
{
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromHours(4);

    private readonly PowerSystem _installedPowerSystem;
    private readonly StorageSizingOptions _options;
    private readonly IReadOnlyDictionary<string, StorageFleet?> _installedBatteryByRegion;
    private readonly HashSet<string> _changedRegions = new(StringComparer.OrdinalIgnoreCase);
    private PowerSystem _candidate;
    private PowerSystem _lastDispatchedCandidate;
    private IReadOnlyList<DispatchOutcome> _outcomes = [];
    private IReadOnlyList<InstalledBatteryAssessment> _installedBatteryAssessments = [];

    public StorageSizingSearch(PowerSystem powerSystem, StorageSizingOptions options)
    {
        _installedPowerSystem = powerSystem;
        _options = options;
        _candidate = powerSystem;
        _lastDispatchedCandidate = powerSystem;
        _installedBatteryByRegion = powerSystem.Regions.ToDictionary(
            region => region.RegionId,
            FindBattery,
            StringComparer.OrdinalIgnoreCase);
    }

    public int DispatchPassCount { get; private set; }

    public StorageSizingRunResult Execute()
    {
        StorageSizingRunResult? growthFailure = GrowUntilCompliant();
        if (growthFailure is not null)
        {
            return growthFailure;
        }

        foreach (string regionId in _changedRegions)
        {
            StorageFleet? installedBattery = _installedBatteryByRegion[regionId];
            double minimumPowerMw = Math.Max(
                StorageSizingOptions.MinimumPowerMw,
                installedBattery?.PowerCapacity.Megawatts ?? 0);
            double minimumEnergyMwh = Math.Max(
                StorageSizingOptions.MinimumEnergyMwh,
                installedBattery?.StorageCapacity.MegawattHours ?? 0);

            if (!RefinePower(regionId, minimumPowerMw)
                || !RefineEnergy(regionId, minimumEnergyMwh))
            {
                return CreateResult(
                    _candidate,
                    StorageSizingStatus.PassLimitReached,
                    "The dispatch pass limit was reached during compliant frontier refinement.");
            }
        }

        return CreateResult(
            _candidate,
            StorageSizingStatus.TargetMet,
            "Every region meets the reliability target after bounded growth and coordinate refinement.");
    }

    private StorageSizingRunResult? GrowUntilCompliant()
    {
        while (true)
        {
            if (!TryDispatch(_candidate, out IReadOnlyList<DispatchOutcome> dispatchedOutcomes))
            {
                return CreateResult(
                    _lastDispatchedCandidate,
                    StorageSizingStatus.PassLimitReached,
                    "The dispatch pass limit was reached before all regions met the reliability target.");
            }

            _outcomes = dispatchedOutcomes;
            _lastDispatchedCandidate = _candidate;
            if (_installedBatteryAssessments.Count == 0)
            {
                _installedBatteryAssessments = AssessInstalledBattery(_outcomes);
            }

            DispatchOutcome[] failingOutcomes = _outcomes
                .Where(outcome => !MeetsTarget(outcome))
                .ToArray();
            if (failingOutcomes.Length == 0)
            {
                return null;
            }

            StorageSizingRunResult? failure = GrowFailingRegions(failingOutcomes);
            if (failure is not null)
            {
                return failure;
            }
        }
    }

    private StorageSizingRunResult? GrowFailingRegions(
        IReadOnlyCollection<DispatchOutcome> failingOutcomes)
    {
        var failingByRegion = failingOutcomes.ToDictionary(
            outcome => outcome.RegionId,
            StringComparer.OrdinalIgnoreCase);
        var nextRegions = new List<Region>(_candidate.Regions.Count);

        foreach (Region region in _candidate.Regions)
        {
            if (!failingByRegion.TryGetValue(region.RegionId, out DispatchOutcome? outcome))
            {
                nextRegions.Add(region);
                continue;
            }

            Region? grown = Grow(region, outcome.Reliability);
            if (grown is null)
            {
                return CreateResult(
                    _candidate,
                    StorageSizingStatus.BatteryCapacityLimitReached,
                    $"The Battery capacity bounds are insufficient for region {region.RegionId}.");
            }

            nextRegions.Add(grown);
            _changedRegions.Add(region.RegionId);
        }

        _candidate = _candidate.WithRegions(nextRegions);
        return null;
    }

    private Region? Grow(Region region, ReliabilityMetrics reliability)
    {
        StorageFleet? battery = FindBattery(region);
        Power minimumPower = Power.FromMegawatts(StorageSizingOptions.MinimumPowerMw);
        Energy minimumEnergy = Energy.FromMegawattHours(StorageSizingOptions.MinimumEnergyMwh);
        if (battery is null
            || battery.PowerCapacity < minimumPower
            || battery.StorageCapacity < minimumEnergy
            || battery.Duration < MinimumDuration)
        {
            Power power = Power.Max(minimumPower, battery?.PowerCapacity ?? Power.Zero);
            Energy energy = Energy.Max(
                Energy.Max(minimumEnergy, battery?.StorageCapacity ?? Energy.Zero),
                power * MinimumDuration);
            if (power > _options.MaximumPower || energy > _options.MaximumEnergy)
            {
                return null;
            }

            return region.WithBatteryStorage(energy, power);
        }

        Power currentPower = battery.PowerCapacity;
        Energy currentEnergy = battery.StorageCapacity;
        double energyPressure = reliability.UnservedEnergy / currentEnergy;
        double powerPressure = reliability.PeakUnservedPower / currentPower;

        if (energyPressure >= powerPressure)
        {
            Energy grownEnergy = Energy.Min(currentEnergy * 2, _options.MaximumEnergy);
            if (grownEnergy > currentEnergy)
            {
                return region.WithBatteryStorage(grownEnergy, currentPower);
            }
        }

        Power maximumPowerAtDuration = _options.MaximumEnergy / MinimumDuration;
        Power grownPower = Power.Min(
            currentPower * 2,
            Power.Min(_options.MaximumPower, maximumPowerAtDuration));
        if (grownPower > currentPower)
        {
            Energy grownEnergy = Energy.Max(currentEnergy, grownPower * MinimumDuration);
            return region.WithBatteryStorage(grownEnergy, grownPower);
        }

        if (energyPressure < powerPressure)
        {
            Energy grownEnergy = Energy.Min(currentEnergy * 2, _options.MaximumEnergy);
            if (grownEnergy > currentEnergy)
            {
                return region.WithBatteryStorage(grownEnergy, currentPower);
            }
        }

        return null;
    }

    private bool RefinePower(string regionId, double minimumPowerMw)
    {
        StorageFleet battery = RequireBattery(_candidate, regionId);
        double lowerMw = minimumPowerMw - 1;
        double upperMw = battery.PowerCapacity.Megawatts;
        while (upperMw - lowerMw > 1)
        {
            double probeMw = Math.Floor((lowerMw + upperMw) / 2);
            PowerSystem probe = ReplaceBattery(
                _candidate,
                regionId,
                battery.StorageCapacity,
                Power.FromMegawatts(probeMw));
            if (!TryDispatch(probe, out IReadOnlyList<DispatchOutcome> probeOutcomes))
            {
                return false;
            }

            if (AllMeetTarget(probeOutcomes))
            {
                _candidate = probe;
                _outcomes = probeOutcomes;
                upperMw = probeMw;
                battery = RequireBattery(_candidate, regionId);
            }
            else
            {
                lowerMw = probeMw;
            }
        }

        return true;
    }

    private bool RefineEnergy(string regionId, double minimumEnergyMwh)
    {
        StorageFleet battery = RequireBattery(_candidate, regionId);
        double durationFloorMwh = battery.PowerCapacity.Megawatts * MinimumDuration.TotalHours;
        double lowerMwh = Math.Max(minimumEnergyMwh, durationFloorMwh) - 1;
        double upperMwh = battery.StorageCapacity.MegawattHours;
        while (upperMwh - lowerMwh > 1)
        {
            double probeMwh = Math.Floor((lowerMwh + upperMwh) / 2);
            PowerSystem probe = ReplaceBattery(
                _candidate,
                regionId,
                Energy.FromMegawattHours(probeMwh),
                battery.PowerCapacity);
            if (!TryDispatch(probe, out IReadOnlyList<DispatchOutcome> probeOutcomes))
            {
                return false;
            }

            if (AllMeetTarget(probeOutcomes))
            {
                _candidate = probe;
                _outcomes = probeOutcomes;
                upperMwh = probeMwh;
                battery = RequireBattery(_candidate, regionId);
            }
            else
            {
                lowerMwh = probeMwh;
            }
        }

        return true;
    }

    private bool TryDispatch(
        PowerSystem powerSystem,
        out IReadOnlyList<DispatchOutcome> outcomes)
    {
        if (DispatchPassCount >= _options.MaximumPasses)
        {
            outcomes = [];
            return false;
        }

        outcomes = Dispatcher.Dispatch(powerSystem);
        DispatchPassCount++;
        return true;
    }

    private bool AllMeetTarget(IReadOnlyList<DispatchOutcome> outcomes) =>
        outcomes.All(MeetsTarget);

    private bool MeetsTarget(DispatchOutcome outcome) =>
        outcome.Reliability.UnservedEnergyPercentageOfDemand <= _options.TargetUsePercentage;

    private StorageSizingRunResult CreateResult(
        PowerSystem powerSystem,
        StorageSizingStatus status,
        string evidence)
    {
        var outcomeByRegion = _outcomes.ToDictionary(
            outcome => outcome.RegionId,
            StringComparer.OrdinalIgnoreCase);
        var installedByRegion = _installedBatteryAssessments.ToDictionary(
            assessment => assessment.BatteryCapacity.RegionId,
            StringComparer.OrdinalIgnoreCase);
        RegionalSizingResult[] regions = powerSystem.Regions
            .Where(region => outcomeByRegion.ContainsKey(region.RegionId))
            .Select(region => CreateRegionalResult(
                region,
                outcomeByRegion[region.RegionId],
                installedByRegion[region.RegionId].BatteryCapacity,
                status,
                evidence))
            .ToArray();

        return new StorageSizingRunResult(
            powerSystem,
            regions,
            _installedBatteryAssessments,
            DispatchPassCount,
            status,
            evidence);
    }

    private RegionalSizingResult CreateRegionalResult(
        Region region,
        DispatchOutcome outcome,
        RegionalBatterySizing installedCapacity,
        StorageSizingStatus runStatus,
        string runEvidence)
    {
        StorageFleet? battery = FindBattery(region);
        Energy energyCapacity = battery?.StorageCapacity ?? Energy.Zero;
        Power powerCapacity = battery?.PowerCapacity ?? Power.Zero;
        bool meetsTarget = MeetsTarget(outcome);

        return new RegionalSizingResult(
            outcome,
            new RegionalBatterySizing(
                region.RegionId,
                energyCapacity,
                powerCapacity,
                energyCapacity != installedCapacity.EnergyCapacity
                    || powerCapacity != installedCapacity.PowerCapacity),
            meetsTarget ? StorageSizingStatus.TargetMet : runStatus,
            meetsTarget ? "The region meets its USE target." : runEvidence);
    }

    private IReadOnlyList<InstalledBatteryAssessment> AssessInstalledBattery(
        IReadOnlyList<DispatchOutcome> outcomes)
    {
        var outcomeByRegion = outcomes.ToDictionary(
            outcome => outcome.RegionId,
            StringComparer.OrdinalIgnoreCase);
        return Array.AsReadOnly(_installedPowerSystem.Regions.Select(region =>
        {
            DispatchOutcome outcome = outcomeByRegion[region.RegionId];
            StorageFleet? battery = FindBattery(region);
            bool meetsTarget = MeetsTarget(outcome);
            return new InstalledBatteryAssessment(
                outcome,
                new RegionalBatterySizing(
                    region.RegionId,
                    battery?.StorageCapacity ?? Energy.Zero,
                    battery?.PowerCapacity ?? Power.Zero,
                    wasChanged: false),
                meetsTarget,
                meetsTarget
                    ? "Installed Battery capacity meets the USE target."
                    : "Installed Battery capacity does not meet the USE target.");
        }).ToArray());
    }

    private static StorageFleet? FindBattery(Region region) =>
        region.StorageFleets.SingleOrDefault(
            fleet => fleet.StorageTechnology == StorageTechnology.Battery);

    private static StorageFleet RequireBattery(PowerSystem powerSystem, string regionId) =>
        FindBattery(powerSystem.Regions.Single(
            region => string.Equals(region.RegionId, regionId, StringComparison.OrdinalIgnoreCase)))
        ?? throw new InvalidOperationException($"Region {regionId} has no Battery fleet to refine.");

    private static PowerSystem ReplaceBattery(
        PowerSystem powerSystem,
        string regionId,
        Energy energy,
        Power power) =>
        powerSystem.WithRegions(powerSystem.Regions.Select(region =>
            string.Equals(region.RegionId, regionId, StringComparison.OrdinalIgnoreCase)
                ? region.WithBatteryStorage(energy, power)
                : region).ToArray());
}