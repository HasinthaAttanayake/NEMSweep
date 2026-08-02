using System.Collections.ObjectModel;
using NEM.Model.Grid;
using NEM.Model.Simulation;
using NEM.Model.Units;

namespace NEM.Model.StorageSizing;

public enum StorageSizingStatus
{
    TargetMet,
    SingleFleetInsufficient,
    PassLimitReached,
}

public sealed record StorageSizingOptions
{
    public const double DefaultTargetUsePercentage = 0.002;
    public const double MinimumPowerMw = 30;
    public const double MinimumEnergyMwh = 120;
    public const int DefaultMaximumPasses = 256;

    public StorageSizingOptions(
        Power maximumPower,
        Energy maximumEnergy,
        double targetUsePercentage = DefaultTargetUsePercentage,
        int maximumPasses = DefaultMaximumPasses)
    {
        if (maximumPower < Power.FromMegawatts(MinimumPowerMw))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPower));
        }

        if (maximumEnergy < Energy.FromMegawattHours(MinimumEnergyMwh))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEnergy));
        }

        if (maximumEnergy < maximumPower * TimeSpan.FromHours(4))
        {
            throw new ArgumentException(
                "Maximum energy must support four hours at maximum power.",
                nameof(maximumEnergy));
        }

        if (double.IsNaN(targetUsePercentage)
            || double.IsInfinity(targetUsePercentage)
            || targetUsePercentage < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetUsePercentage));
        }

        if (maximumPasses <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPasses));
        }

        MaximumPower = maximumPower;
        MaximumEnergy = maximumEnergy;
        TargetUsePercentage = targetUsePercentage;
        MaximumPasses = maximumPasses;
    }

    public Power MaximumPower { get; }
    public Energy MaximumEnergy { get; }
    public double TargetUsePercentage { get; }
    public int MaximumPasses { get; }
}

public sealed record StorageSizing(
    string RegionId,
    Energy EnergyCapacity,
    Power PowerCapacity,
    bool WasSolvedFor = true);

public sealed record RegionalSizingResult(
    DispatchOutcome DispatchOutcome,
    StorageSizing StorageSizing,
    StorageSizingStatus Status,
    string TerminationEvidence)
{
    public ReliabilityMetrics Reliability => DispatchOutcome.Reliability;
    public Energy TotalUnservedEnergy => Reliability.UnservedEnergy;
    public Power PeakUnservedPower => Reliability.PeakUnservedPower;
}

public sealed record InstalledStorageAssessment(
    DispatchOutcome DispatchOutcome,
    StorageSizing StorageSizing,
    bool MeetsTarget,
    string Evidence)
{
    public ReliabilityMetrics Reliability => DispatchOutcome.Reliability;
}

public sealed class StorageSizingRunResult
{
    public StorageSizingRunResult(
        PowerSystem powerSystem,
        IReadOnlyList<RegionalSizingResult> regions,
        IReadOnlyList<InstalledStorageAssessment> installedStorageAssessments,
        int dispatchPassCount,
        StorageSizingStatus status,
        string terminationEvidence)
    {
        ArgumentNullException.ThrowIfNull(powerSystem);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(installedStorageAssessments);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminationEvidence);

        PowerSystem = powerSystem;
        Regions = new ReadOnlyCollection<RegionalSizingResult>(regions.ToArray());
        InstalledStorageAssessments = new ReadOnlyCollection<InstalledStorageAssessment>(
            installedStorageAssessments.ToArray());
        DispatchPassCount = dispatchPassCount;
        Status = status;
        TerminationEvidence = terminationEvidence;
    }

    public PowerSystem PowerSystem { get; }
    public IReadOnlyList<RegionalSizingResult> Regions { get; }
    public IReadOnlyList<InstalledStorageAssessment> InstalledStorageAssessments { get; }
    public int DispatchPassCount { get; }
    public StorageSizingStatus Status { get; }
    public string TerminationEvidence { get; }
}

public static class StorageSizingService
{
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromHours(4);

    public static StorageSizingRunResult Size(
        PowerSystem powerSystem,
        StorageSizingOptions options)
    {
        ArgumentNullException.ThrowIfNull(powerSystem);
        ArgumentNullException.ThrowIfNull(options);

        var originalBatteryByRegion = powerSystem.Regions.ToDictionary(
            region => region.RegionId,
            FindBattery,
            StringComparer.OrdinalIgnoreCase);
        var sizedRegions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PowerSystem candidate = powerSystem;
        PowerSystem lastDispatchedCandidate = powerSystem;
        IReadOnlyList<DispatchOutcome> outcomes = [];
        IReadOnlyList<InstalledStorageAssessment> installedStorageAssessments = [];
        int passes = 0;

        while (true)
        {
            if (!TryDispatch(
                    candidate,
                    options,
                    ref passes,
                    out IReadOnlyList<DispatchOutcome> dispatchedOutcomes))
            {
                return Result(
                    lastDispatchedCandidate,
                    outcomes,
                    installedStorageAssessments,
                    passes,
                    options,
                    StorageSizingStatus.PassLimitReached,
                    "The dispatch pass limit was reached before all regions met the reliability target.");
            }

            outcomes = dispatchedOutcomes;
            lastDispatchedCandidate = candidate;
            if (installedStorageAssessments.Count == 0)
            {
                installedStorageAssessments = AssessInstalledStorage(
                    powerSystem,
                    outcomes,
                    options);
            }

            DispatchOutcome[] failing = outcomes.Where(
                outcome => !MeetsTarget(outcome, options)).ToArray();
            if (failing.Length == 0)
            {
                break;
            }

            var failingIds = failing.Select(outcome => outcome.RegionId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var nextRegions = new List<Region>(candidate.Regions.Count);
            bool changed = false;
            foreach (Region region in candidate.Regions)
            {
                if (!failingIds.Contains(region.RegionId))
                {
                    nextRegions.Add(region);
                    continue;
                }

                DispatchOutcome outcome = failing.Single(
                    item => string.Equals(
                        item.RegionId,
                        region.RegionId,
                        StringComparison.OrdinalIgnoreCase));
                Region? grown = Grow(region, outcome.Reliability, options);
                if (grown is null)
                {
                    return Result(
                        candidate,
                        outcomes,
                        installedStorageAssessments,
                        passes,
                        options,
                        StorageSizingStatus.SingleFleetInsufficient,
                        $"The Battery fleet bounds are insufficient for region {region.RegionId}.");
                }

                nextRegions.Add(grown);
                sizedRegions.Add(region.RegionId);
                changed = true;
            }

            if (!changed)
            {
                return Result(
                    candidate,
                    outcomes,
                    installedStorageAssessments,
                    passes,
                    options,
                    StorageSizingStatus.SingleFleetInsufficient,
                    "No failing region could make a legal storage increase.");
            }

            candidate = candidate.WithRegions(nextRegions);
        }

        foreach (string regionId in sizedRegions)
        {
            StorageFleet? originalBattery = originalBatteryByRegion[regionId];
            double minimumPowerMw = Math.Max(
                StorageSizingOptions.MinimumPowerMw,
                originalBattery?.PowerCapacity.Megawatts ?? 0);
            double minimumEnergyMwh = Math.Max(
                StorageSizingOptions.MinimumEnergyMwh,
                originalBattery?.StorageCapacity.MegawattHours ?? 0);

            if (!RefinePower(
                    regionId,
                    minimumPowerMw,
                    options,
                    ref candidate,
                    ref outcomes,
                    ref passes)
                || !RefineEnergy(
                    regionId,
                    minimumEnergyMwh,
                    options,
                    ref candidate,
                    ref outcomes,
                    ref passes))
            {
                return Result(
                    candidate,
                    outcomes,
                    installedStorageAssessments,
                    passes,
                    options,
                    StorageSizingStatus.PassLimitReached,
                    "The dispatch pass limit was reached during compliant frontier refinement.");
            }
        }

        return Result(
            candidate,
            outcomes,
            installedStorageAssessments,
            passes,
            options,
            StorageSizingStatus.TargetMet,
            "Every region meets the reliability target after bounded growth and coordinate refinement.");
    }

    private static Region? Grow(
        Region region,
        ReliabilityMetrics reliability,
        StorageSizingOptions options)
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
            if (power > options.MaximumPower || energy > options.MaximumEnergy)
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
            Energy grownEnergy = Energy.Min(currentEnergy * 2, options.MaximumEnergy);
            if (grownEnergy > currentEnergy)
            {
                return region.WithBatteryStorage(
                    grownEnergy,
                    battery.PowerCapacity);
            }
        }

        Power maximumPowerAtDuration = options.MaximumEnergy / MinimumDuration;
        Power grownPower = Power.Min(
            currentPower * 2,
            Power.Min(options.MaximumPower, maximumPowerAtDuration));
        if (grownPower > currentPower)
        {
            Energy grownEnergy = Energy.Max(currentEnergy, grownPower * MinimumDuration);
            return region.WithBatteryStorage(
                grownEnergy,
                grownPower);
        }

        if (energyPressure < powerPressure)
        {
            Energy grownEnergy = Energy.Min(currentEnergy * 2, options.MaximumEnergy);
            if (grownEnergy > currentEnergy)
            {
                return region.WithBatteryStorage(
                    grownEnergy,
                    battery.PowerCapacity);
            }
        }

        return null;
    }

    private static bool RefinePower(
        string regionId,
        double minimumPowerMw,
        StorageSizingOptions options,
        ref PowerSystem candidate,
        ref IReadOnlyList<DispatchOutcome> outcomes,
        ref int passes)
    {
        StorageFleet battery = RequireBattery(candidate, regionId);
        double lowerMw = minimumPowerMw - 1;
        double upperMw = battery.PowerCapacity.Megawatts;
        while (upperMw - lowerMw > 1)
        {
            double probeMw = Math.Floor((lowerMw + upperMw) / 2);
            PowerSystem probe = ReplaceBattery(
                candidate,
                regionId,
                battery.StorageCapacity,
                Power.FromMegawatts(probeMw));
            if (!TryDispatch(probe, options, ref passes, out IReadOnlyList<DispatchOutcome> probeOutcomes))
            {
                return false;
            }

            if (AllMeetTarget(probeOutcomes, options))
            {
                candidate = probe;
                outcomes = probeOutcomes;
                upperMw = probeMw;
                battery = RequireBattery(candidate, regionId);
            }
            else
            {
                lowerMw = probeMw;
            }
        }

        return true;
    }

    private static bool RefineEnergy(
        string regionId,
        double minimumEnergyMwh,
        StorageSizingOptions options,
        ref PowerSystem candidate,
        ref IReadOnlyList<DispatchOutcome> outcomes,
        ref int passes)
    {
        StorageFleet battery = RequireBattery(candidate, regionId);
        double durationFloorMwh = battery.PowerCapacity.Megawatts * MinimumDuration.TotalHours;
        double lowerMwh = Math.Max(minimumEnergyMwh, durationFloorMwh) - 1;
        double upperMwh = battery.StorageCapacity.MegawattHours;
        while (upperMwh - lowerMwh > 1)
        {
            double probeMwh = Math.Floor((lowerMwh + upperMwh) / 2);
            PowerSystem probe = ReplaceBattery(
                candidate,
                regionId,
                Energy.FromMegawattHours(probeMwh),
                battery.PowerCapacity);
            if (!TryDispatch(probe, options, ref passes, out IReadOnlyList<DispatchOutcome> probeOutcomes))
            {
                return false;
            }

            if (AllMeetTarget(probeOutcomes, options))
            {
                candidate = probe;
                outcomes = probeOutcomes;
                upperMwh = probeMwh;
                battery = RequireBattery(candidate, regionId);
            }
            else
            {
                lowerMwh = probeMwh;
            }
        }

        return true;
    }

    private static bool TryDispatch(
        PowerSystem powerSystem,
        StorageSizingOptions options,
        ref int passes,
        out IReadOnlyList<DispatchOutcome> outcomes)
    {
        if (passes >= options.MaximumPasses)
        {
            outcomes = [];
            return false;
        }

        outcomes = Dispatcher.Dispatch(powerSystem);
        passes++;
        return true;
    }

    private static bool AllMeetTarget(
        IReadOnlyList<DispatchOutcome> outcomes,
        StorageSizingOptions options) =>
        outcomes.All(outcome => MeetsTarget(outcome, options));

    private static bool MeetsTarget(DispatchOutcome outcome, StorageSizingOptions options) =>
        outcome.Reliability.UnservedEnergyPercentageOfDemand <= options.TargetUsePercentage;

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

    private static StorageSizingRunResult Result(
        PowerSystem powerSystem,
        IReadOnlyList<DispatchOutcome> outcomes,
        IReadOnlyList<InstalledStorageAssessment> installedStorageAssessments,
        int passes,
        StorageSizingOptions options,
        StorageSizingStatus status,
        string evidence)
    {
        var outcomeByRegion = outcomes.ToDictionary(
            outcome => outcome.RegionId,
            StringComparer.OrdinalIgnoreCase);
        var installedByRegion = installedStorageAssessments.ToDictionary(
            assessment => assessment.StorageSizing.RegionId,
            StringComparer.OrdinalIgnoreCase);
        RegionalSizingResult[] regions = powerSystem.Regions
            .Where(region => outcomeByRegion.ContainsKey(region.RegionId))
            .Select(region =>
            {
                DispatchOutcome outcome = outcomeByRegion[region.RegionId];
                StorageFleet? battery = FindBattery(region);
                StorageSizing installed = installedByRegion[region.RegionId].StorageSizing;
                bool met = MeetsTarget(outcome, options);
                Energy energyCapacity = battery?.StorageCapacity ?? Energy.Zero;
                Power powerCapacity = battery?.PowerCapacity ?? Power.Zero;
                StorageSizingStatus regionStatus = met
                    ? StorageSizingStatus.TargetMet
                    : status;
                return new RegionalSizingResult(
                    outcome,
                    new StorageSizing(
                        region.RegionId,
                        energyCapacity,
                        powerCapacity,
                        energyCapacity != installed.EnergyCapacity
                            || powerCapacity != installed.PowerCapacity),
                    regionStatus,
                    met
                        ? "The region meets its USE target."
                        : evidence);
            })
            .ToArray();

        return new StorageSizingRunResult(
            powerSystem,
            regions,
            installedStorageAssessments,
            passes,
            status,
            evidence);
    }

    private static IReadOnlyList<InstalledStorageAssessment> AssessInstalledStorage(
        PowerSystem powerSystem,
        IReadOnlyList<DispatchOutcome> outcomes,
        StorageSizingOptions options)
    {
        var outcomeByRegion = outcomes.ToDictionary(
            outcome => outcome.RegionId,
            StringComparer.OrdinalIgnoreCase);
        return Array.AsReadOnly(powerSystem.Regions.Select(region =>
        {
            DispatchOutcome outcome = outcomeByRegion[region.RegionId];
            StorageFleet? battery = FindBattery(region);
            bool meetsTarget = MeetsTarget(outcome, options);
            return new InstalledStorageAssessment(
                outcome,
                new StorageSizing(
                    region.RegionId,
                    battery?.StorageCapacity ?? Energy.Zero,
                    battery?.PowerCapacity ?? Power.Zero,
                    WasSolvedFor: false),
                meetsTarget,
                meetsTarget
                    ? "Installed storage meets the USE target."
                    : "Installed storage does not meet the USE target.");
        }).ToArray());
    }
}