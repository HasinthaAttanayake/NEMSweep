using NEMSweep.Model.Grid;
using NEMSweep.Model.Simulation;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.StorageSizing;

internal sealed class StorageSizingSearch
{
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromHours(4);
    private const double ReliabilityImprovementToleranceMwh = 0.001;

    private readonly PowerSystem _installedPowerSystem;
    private readonly StorageSizingOptions _options;
    private readonly IReadOnlyDictionary<string, StorageFleet?> _installedBatteryByRegion;
    private readonly EnergyLimitedAssessment _energyLimitedAssessment;
    private readonly HashSet<string> _changedRegions = new(StringComparer.OrdinalIgnoreCase);
    private PowerSystem _candidate;
    private PowerSystem _lastDispatchedCandidate;
    private IReadOnlyList<DispatchOutcome> _outcomes = [];
    private IReadOnlyList<InterconnectorFlow> _interconnectorFlows = [];
    private IReadOnlyList<InstalledBatteryAssessment> _installedBatteryAssessments = [];
    private readonly List<StorageSizingPass> _trajectory = [];

    public StorageSizingSearch(
        PowerSystem powerSystem,
        StorageSizingOptions options)
    {
        _installedPowerSystem = powerSystem;
        _options = options;
        _candidate = powerSystem;
        _lastDispatchedCandidate = powerSystem;
        _installedBatteryByRegion = powerSystem.Regions.ToDictionary(
            region => region.RegionId,
            FindBattery,
            StringComparer.OrdinalIgnoreCase);
        _energyLimitedAssessment = EnergyLimitedAssessment.Assess(powerSystem);
    }

    public int DispatchPassCount { get; private set; }

    public StorageSizingRunResult Execute()
    {
        StorageSizingRunResult? growthFailure = GrowUntilCompliant();
        if (growthFailure is not null)
        {
            return growthFailure;
        }

        foreach (string regionId in _changedRegions.OrderBy(regionId => regionId, StringComparer.Ordinal))
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
            if (!TryDispatch(
                _candidate,
                out IReadOnlyList<DispatchOutcome> dispatchedOutcomes,
                out IReadOnlyList<InterconnectorFlow> interconnectorFlows))
            {
                return CreateResult(
                    _lastDispatchedCandidate,
                    StorageSizingStatus.PassLimitReached,
                    "The dispatch pass limit was reached before all regions met the reliability target.");
            }

            _outcomes = dispatchedOutcomes;
            _interconnectorFlows = interconnectorFlows;
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

            if (_energyLimitedAssessment.IsEnergyLimited)
            {
                return CreateResult(
                    _candidate,
                    StorageSizingStatus.EnergyLimited,
                    $"Available generation is {_energyLimitedAssessment.ShortfallEnergy.MegawattHours:F3} MWh "
                    + "below total demand over the dispatch period.",
                    _energyLimitedAssessment);
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
        // One region grows per call, then the caller re-dispatches. Growing every failing
        // region in one pass would credit each one with reliability the others' new capacity
        // supplied, so the search would overshoot and install capacity no region needed.
        // Lowest region id first, purely so the trajectory is deterministic.
        Region? region = _candidate.Regions
            .Where(candidate => failingByRegion.ContainsKey(candidate.RegionId))
            .OrderBy(candidate => candidate.RegionId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (region is null)
        {
            return null;
        }

        DispatchOutcome outcome = failingByRegion[region.RegionId];

        Region[] growthCandidates = GrowthCandidates(region);
        if (growthCandidates.Length == 0)
        {
            int firstUnservedIndex = Enumerable.Range(0, outcome.Unserved.Length)
                .First(index => outcome.Unserved[index] > Power.Zero);
            return CreateResult(
                _candidate,
                StorageSizingStatus.BatteryCapacityLimitReached,
                $"The Battery capacity bounds are insufficient for region {region.RegionId}: "
                + $"{BatterySizingDescription(region)} reached; "
                + $"{outcome.Reliability.UnservedEnergy.MegawattHours:F3} MWh remains unserved "
                + $"across {outcome.Reliability.UnservedHours} hours, first at "
                + $"{outcome.Unserved.InstantAt(firstUnservedIndex):O}; peak shortfall is "
                + $"{outcome.Reliability.PeakUnservedPower.Megawatts:F3} MW.");
        }

        var probes = new List<GrowthProbe>(growthCandidates.Length);
        foreach (Region growthCandidate in growthCandidates)
        {
            PowerSystem probeSystem = ReplaceRegion(_candidate, growthCandidate);
            if (!TryDispatch(probeSystem, out IReadOnlyList<DispatchOutcome> probeOutcomes))
            {
                return CreateResult(
                    _candidate,
                    StorageSizingStatus.PassLimitReached,
                    "The dispatch pass limit was reached while testing larger Battery candidates.");
            }

            probes.Add(new GrowthProbe(
                growthCandidate,
                probeOutcomes.Single(candidate => string.Equals(
                    candidate.RegionId,
                    region.RegionId,
                    StringComparison.OrdinalIgnoreCase)).Reliability));
        }

        GrowthProbe? bestProbe = probes
            .Where(probe => MateriallyImproves(outcome.Reliability, probe.Reliability))
            .OrderBy(probe => probe.Reliability.UnservedEnergy)
            .ThenBy(probe => probe.Reliability.PeakUnservedPower)
            .FirstOrDefault();
        if (bestProbe is null)
        {
            return CreateResult(
                _candidate,
                StorageSizingStatus.StorageNoLongerImprovesReliability,
                StagnationEvidence(region, outcome.Reliability, probes));
        }

        _candidate = ReplaceRegion(_candidate, bestProbe.Region);
        _changedRegions.Add(region.RegionId);
        return null;
    }

    private Region[] GrowthCandidates(Region region)
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
            return power > _options.MaximumPower || energy > _options.MaximumEnergy
                ? []
                : [region.WithBatteryStorage(energy, power)];
        }

        Power currentPower = battery.PowerCapacity;
        Energy currentEnergy = battery.StorageCapacity;
        Power maximumPowerAtDuration = _options.MaximumEnergy / MinimumDuration;
        Power grownPower = Power.Min(
            currentPower * 2,
            Power.Min(_options.MaximumPower, maximumPowerAtDuration));
        Energy grownEnergy = Energy.Min(currentEnergy * 2, _options.MaximumEnergy);
        var candidates = new List<Region>();
        if (grownEnergy > currentEnergy)
        {
            candidates.Add(region.WithBatteryStorage(grownEnergy, currentPower));
        }

        if (grownPower > currentPower)
        {
            candidates.Add(region.WithBatteryStorage(
                Energy.Max(currentEnergy, grownPower * MinimumDuration),
                grownPower));
        }

        if (grownEnergy > currentEnergy && grownPower > currentPower)
        {
            candidates.Add(region.WithBatteryStorage(
                Energy.Max(grownEnergy, grownPower * MinimumDuration),
                grownPower));
        }

        return candidates
            .GroupBy(candidate =>
            {
                StorageFleet candidateBattery = RequireBattery(candidate);
                return (candidateBattery.PowerCapacity, candidateBattery.StorageCapacity);
            })
            .Select(group => group.First())
            .ToArray();
    }

    private bool RefinePower(string regionId, double minimumPowerMw)
    {
        StorageFleet battery = RequireBattery(_candidate, regionId);
        double lowerMw = Math.Floor(minimumPowerMw) - 1;
        double upperMw = Math.Ceiling(battery.PowerCapacity.Megawatts);
        while (upperMw - lowerMw > 1)
        {
            double probeMw = Math.Floor((lowerMw + upperMw) / 2);
            PowerSystem probe = ReplaceBattery(
                _candidate,
                regionId,
                battery.StorageCapacity,
                Power.FromMegawatts(probeMw));
            if (!TryDispatch(
                probe,
                out IReadOnlyList<DispatchOutcome> probeOutcomes,
                out IReadOnlyList<InterconnectorFlow> probeInterconnectorFlows))
            {
                return false;
            }

            if (AllMeetTarget(probeOutcomes))
            {
                _candidate = probe;
                _outcomes = probeOutcomes;
                _interconnectorFlows = probeInterconnectorFlows;
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
        double lowerMwh = Math.Floor(Math.Max(minimumEnergyMwh, durationFloorMwh)) - 1;
        double upperMwh = Math.Ceiling(battery.StorageCapacity.MegawattHours);
        while (upperMwh - lowerMwh > 1)
        {
            double probeMwh = Math.Floor((lowerMwh + upperMwh) / 2);
            PowerSystem probe = ReplaceBattery(
                _candidate,
                regionId,
                Energy.FromMegawattHours(probeMwh),
                battery.PowerCapacity);
            if (!TryDispatch(
                probe,
                out IReadOnlyList<DispatchOutcome> probeOutcomes,
                out IReadOnlyList<InterconnectorFlow> probeInterconnectorFlows))
            {
                return false;
            }

            if (AllMeetTarget(probeOutcomes))
            {
                _candidate = probe;
                _outcomes = probeOutcomes;
                _interconnectorFlows = probeInterconnectorFlows;
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
        out IReadOnlyList<DispatchOutcome> outcomes,
        out IReadOnlyList<InterconnectorFlow> interconnectorFlows)
    {
        if (DispatchPassCount >= _options.MaximumPasses)
        {
            outcomes = [];
            interconnectorFlows = [];
            return false;
        }

        SystemDispatchRunResult result = Dispatcher.DispatchSystem(powerSystem);
        outcomes = result.RegionalOutcomes;
        interconnectorFlows = result.InterconnectorFlows;
        DispatchPassCount++;
        _trajectory.Add(CreatePass(powerSystem, outcomes));
        return true;
    }

    private StorageSizingPass CreatePass(PowerSystem powerSystem, IReadOnlyList<DispatchOutcome> outcomes)
    {
        var outcomesByRegion = outcomes.ToDictionary(outcome => outcome.RegionId, StringComparer.OrdinalIgnoreCase);
        StorageSizingRegionPass[] regions = powerSystem.Regions.Select(region =>
        {
            StorageFleet? battery = FindBattery(region);
            DispatchOutcome outcome = outcomesByRegion[region.RegionId];
            return new StorageSizingRegionPass(
                region.RegionId,
                battery?.StorageCapacity ?? Energy.Zero,
                battery?.PowerCapacity ?? Power.Zero,
                outcome.Reliability.UnservedEnergy,
                outcome.Reliability.UnservedHours);
        }).ToArray();
        int systemUnservedHours = Enumerable.Range(0, outcomes[0].Unserved.Length)
            .Count(index => outcomes.Any(outcome => outcome.Unserved[index] > Power.Zero));
        return new StorageSizingPass(
            DispatchPassCount,
            regions,
            regions.Aggregate(Energy.Zero, (total, region) => total + region.UnservedEnergy),
            systemUnservedHours);
    }

    private bool TryDispatch(
        PowerSystem powerSystem,
        out IReadOnlyList<DispatchOutcome> outcomes) =>
        TryDispatch(powerSystem, out outcomes, out _);

    private bool AllMeetTarget(IReadOnlyList<DispatchOutcome> outcomes) =>
        outcomes.All(MeetsTarget);

    private bool MeetsTarget(DispatchOutcome outcome) =>
        outcome.Reliability.UnservedEnergyPercentageOfDemand <= _options.TargetUsePercentage;

    private static bool MateriallyImproves(
        ReliabilityMetrics current,
        ReliabilityMetrics probe) =>
        probe.UnservedEnergy.MegawattHours
            < current.UnservedEnergy.MegawattHours - ReliabilityImprovementToleranceMwh;

    private static string StagnationEvidence(
        Region region,
        ReliabilityMetrics current,
        IReadOnlyList<GrowthProbe> probes) =>
        $"Larger Battery candidates did not materially reduce unserved energy for region "
        + $"{region.RegionId}: {current.UnservedEnergy.MegawattHours:F3} MWh remains unserved "
        + $"across {current.UnservedHours} hours with a peak shortfall of "
        + $"{current.PeakUnservedPower.Megawatts:F3} MW. Tested "
        + string.Join(", ", probes.Select(probe => BatterySizingDescription(probe.Region)))
        + ".";

    private static string BatterySizingDescription(Region region)
    {
        StorageFleet? battery = FindBattery(region);
        return $"{(battery?.PowerCapacity.Megawatts ?? 0):F0} MW / "
            + $"{(battery?.StorageCapacity.MegawattHours ?? 0):F0} MWh";
    }

    private StorageSizingRunResult CreateResult(
        PowerSystem powerSystem,
        StorageSizingStatus status,
        string evidence,
        EnergyLimitedAssessment? energyLimitedAssessment = null)
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
            evidence,
            energyLimitedAssessment,
            _interconnectorFlows,
            _trajectory);
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

        StorageSizingStatus status = meetsTarget
            ? StorageSizingStatus.TargetMet
            : runStatus;
        string evidence = meetsTarget
            ? "The region meets its USE target."
            : runEvidence;

        return new RegionalSizingResult(
            outcome,
            new RegionalBatterySizing(
                region.RegionId,
                energyCapacity,
                powerCapacity,
                energyCapacity != installedCapacity.EnergyCapacity
                    || powerCapacity != installedCapacity.PowerCapacity),
            meetsTarget,
            status,
            evidence);
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
        RequireBattery(powerSystem.Regions.Single(
            region => string.Equals(region.RegionId, regionId, StringComparison.OrdinalIgnoreCase)));

    private static StorageFleet RequireBattery(Region region) =>
        FindBattery(region)
        ?? throw new InvalidOperationException($"Region {region.RegionId} has no Battery fleet to refine.");

    private static PowerSystem ReplaceRegion(PowerSystem powerSystem, Region replacement) =>
        powerSystem.WithRegions(powerSystem.Regions.Select(region =>
            string.Equals(region.RegionId, replacement.RegionId, StringComparison.OrdinalIgnoreCase)
                ? replacement
                : region).ToArray());

    private static PowerSystem ReplaceBattery(
        PowerSystem powerSystem,
        string regionId,
        Energy energy,
        Power power) =>
        powerSystem.WithRegions(powerSystem.Regions.Select(region =>
            string.Equals(region.RegionId, regionId, StringComparison.OrdinalIgnoreCase)
                ? region.WithBatteryStorage(energy, power)
                : region).ToArray());

    private sealed record GrowthProbe(Region Region, ReliabilityMetrics Reliability);
}