using System.Globalization;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Scenarios;

/// <summary>
/// Builds the <see cref="RunLimitationsDTO"/> a published result carries. The engine statements are
/// fixed here; the run-specific ones are read off the result being published, so a run on the
/// caller's own inputs describes those inputs rather than any dataset this tool knows about.
/// </summary>
internal static class RunLimitations
{
    /// <summary>Where the reasoning behind the engine statements is set out in full.</summary>
    private const string Reference = "https://docs.nemsweep.com/assumptions/limitations.html";

    /// <summary>
    /// True of every run, whatever the data. Kept short deliberately: this is the list a reader
    /// scans before quoting a figure, and the reference carries the argument behind each one.
    /// </summary>
    private static readonly string[] EngineLimitations =
    [
        "This is one system against the demand and weather supplied, with no stochastic draws. It "
        + "reports a realised outcome, not an expectation; the difference between two scenarios is "
        + "more trustworthy than the level of either.",
        "Generation is dispatched in merit order by short-run marginal cost. There is no market "
        + "bidding, no unit commitment, no security-constrained dispatch and no investment "
        + "valuation.",
        "Only Battery capacity is sized. Every other storage technology, all generation and all "
        + "transmission stay exactly as the scenario declared them.",
        "Storage charge and discharge follows a fixed rule with no foresight of later intervals, so "
        + "the capacity the search settles on can be too small against a prolonged shortfall or "
        + "too large against a system able to arbitrage.",
        "The reliability target is the one the scenario set, and it is tested over the modelled "
        + "period only. It is not a multi-year expectation.",
        "Cost is the levelised cost of building and running the declared system per MWh served. It "
        + "excludes network, retail, scheme and tax costs, so it is not a retail price.",
        "Emissions are operational combustion only, charged on generated energy and reported per "
        + "MWh served. They are not a life-cycle figure.",
    ];

    /// <summary>Assembles the block published on a whole-system result.</summary>
    /// <param name="reliability">The system reliability assessment this run recorded.</param>
    /// <param name="sizing">Sizing evidence, or <see langword="null"/> for a fixed-fleet run.</param>
    /// <param name="storageCapacity">The installed-storage split this run published.</param>
    /// <param name="weatherBases">Each region's weather basis, from the input artifacts' provenance.</param>
    /// <param name="periodStart">First instant of the dispatch period, in market time.</param>
    /// <param name="periodEnd">Instant the dispatch period ends, in market time.</param>
    public static RunLimitationsDTO Create(
        ReliabilityBasisDTO reliability,
        StorageSizingOutcomeDTO? sizing,
        StorageCapacityBreakdownDTO storageCapacity,
        IEnumerable<WeatherBasisDTO> weatherBases,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd)
    {
        ArgumentNullException.ThrowIfNull(reliability);
        ArgumentNullException.ThrowIfNull(storageCapacity);
        ArgumentNullException.ThrowIfNull(weatherBases);

        var statements = new List<string>
        {
            "The reliability target of " + Number(reliability.TargetUsePercentageOfDemand)
            + " per cent of demand energy was tested over " + Day(periodStart) + " to "
            + Day(periodEnd) + " in market time and over no other period.",
        };

        if (!reliability.WithinTarget)
        {
            statements.Add(
                "The target was not met: " + Number(reliability.AchievedUsePercentageOfDemand)
                + " per cent of demand energy was left unserved. Figures from this run describe a "
                + "system that did not reach the standard it was tested against.");
        }

        statements.AddRange(SizingStatements(sizing, storageCapacity));

        if (weatherBases.Any(basis => basis.Kind == WeatherBasisKind.TypicalMeteorologicalYear))
        {
            statements.Add(
                "The weather input is a typical meteorological year, a composite of representative "
                + "months applied to the dispatch period by calendar hour. It does not contain the "
                + "tail weather events that drive storage and reliability outcomes, so storage "
                + "need is likely understated.");
        }

        double fixedStorage = storageCapacity.PumpedHydroEnergyMwh + storageCapacity.OtherStorageEnergyMwh;
        if (fixedStorage > 0)
        {
            statements.Add(
                "The declared system includes " + Quantity(fixedStorage) + " MWh of storage energy "
                + "the search cannot grow, counted in the installed-storage totals but not in the "
                + "battery figures. Read storageCapacity rather than storageEnergyMwh for battery.");
        }

        return new RunLimitationsDTO(EngineLimitations, [.. statements], Reference);
    }

    /// <summary>
    /// What the sizing search established, and what it did not. The distinction that matters is
    /// whether the search ran out of room or showed that more room would not have helped, because
    /// only the energy-limited outcome rules out a larger battery.
    /// </summary>
    private static IEnumerable<string> SizingStatements(
        StorageSizingOutcomeDTO? sizing,
        StorageCapacityBreakdownDTO storageCapacity)
    {
        if (sizing is null)
        {
            yield return "Execution was fixed-fleet, so no storage was sized. The declared fleets "
                + "were dispatched unchanged, whether or not they met the target.";
            yield break;
        }

        yield return sizing.Outcome switch
        {
            StorageSizingOutcome.NotRequired =>
                "The declared fleets met the target unchanged, so the search added no battery. It "
                + "did not look for the smallest fleet that would also have met it.",
            StorageSizingOutcome.Resized =>
                "The search grew battery energy by " + Quantity(storageCapacity.AddedBatteryEnergyMwh)
                + " MWh over " + Count(sizing.PassesUsed) + " dispatch passes to reach the target. "
                + "That is a capacity sufficient under this dispatch rule, not a least-cost or "
                + "otherwise optimal one.",
            StorageSizingOutcome.EnergyLimited =>
                "The search stopped because available generation energy is below demand energy. No "
                + "battery size could have met the target: more generation is required, which this "
                + "run did not test.",
            StorageSizingOutcome.StorageNoLongerImprovesReliability =>
                "The search stopped because a larger battery no longer reduced unserved energy "
                + "under this dispatch rule. A policy with foresight might still have done better, "
                + "so this does not establish that no battery would work.",
            StorageSizingOutcome.BatteryCapacityLimitReached =>
                "The search stopped at the configured bounds of " + Quantity(sizing.MaximumEnergyMwh)
                + " MWh and " + Quantity(sizing.MaximumPowerMw) + " MW summed across regions. It has not "
                + "established that no battery would meet the target, only that none within those "
                + "bounds did.",
            StorageSizingOutcome.PassLimitReached =>
                "The search exhausted its budget of dispatch passes after " + Count(sizing.PassesUsed)
                + ". It has not established that no battery would meet the target, and the "
                + "capacity reported may be coarser than a completed search would have found.",
            _ => "The search stopped with outcome '" + sizing.Outcome
                + "'. Read storageSizing.outcome before treating the reported capacity as "
                + "sufficient.",
        };
    }

    /// <summary>A value as written, invariantly, with no padding or thousands separators.</summary>
    private static string Number(double value) =>
        value.ToString("G", CultureInfo.InvariantCulture);

    /// <summary>A physical quantity, to one decimal place as published values are.</summary>
    private static string Quantity(double value) =>
        value.ToString("N1", CultureInfo.InvariantCulture);

    private static string Count(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A date alone: the statement is about which period was covered, not which instant.</summary>
    private static string Day(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
