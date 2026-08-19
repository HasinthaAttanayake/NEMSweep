using NEM.Model.Units;

namespace NEM.Model.Grid;

/// <summary>
/// Assumed opening charge for a storage fleet at the start of a dispatch run.
///
/// Dispatch used to open every storage fleet at zero MWh regardless of installed
/// capacity, which is unrealistic: real batteries and pumped-hydro schemes are not
/// run flat before a simulation starts. PumpedHydro is assumed to open at 80% of its
/// installed energy capacity (large reservoir schemes are typically operated close to
/// full as an operating reserve); every other storage technology is assumed to open
/// at 50% (a mid-point absent fleet-specific cycling data). Both fractions are
/// assumptions pending real operational data - see NEM-076.
///
/// The seed is always computed from a fleet's ORIGINALLY INSTALLED (scenario-declared)
/// energy capacity, never from a capacity that storage sizing has since grown. Sizing
/// searches for the smallest compliant capacity; if growing a fleet also grew its
/// opening balance, sizing would be handing the dispatch run free energy and the
/// search would no longer be measuring what installed capacity alone can achieve. A
/// region with no installed fleet of a technology has no installed capacity to seed
/// from, so it gets no seed even if sizing later introduces one for that technology.
/// </summary>
internal static class StorageSeedPolicy
{
    internal const double PumpedHydroSeedFraction = 0.8;
    internal const double DefaultSeedFraction = 0.5;

    internal static Energy SeedFor(StorageTechnology technology, Energy installedEnergyCapacity) =>
        installedEnergyCapacity * FractionFor(technology);

    private static double FractionFor(StorageTechnology technology) =>
        technology == StorageTechnology.PumpedHydro
            ? PumpedHydroSeedFraction
            : DefaultSeedFraction;
}
