using NEMSweep.Model.Grid;
using NEMSweep.Model.Units;

namespace NEMSweep.Model.Simulation;

/// <summary>
/// Requests immediate storage discharge for demand deficits and immediate charging from
/// would-be-curtailed surplus generation. Unlike
/// <see cref="GreedySurplusAndIncrementalGenerationChargingPolicy"/>, it never requests
/// additional dispatchable generation to charge storage.
/// </summary>
public sealed class GreedyPolicy : IStoragePolicy
{
    /// <summary>
    /// Requests discharge for a deficit, or surplus-only charging for a surplus, from
    /// Battery first and pumped hydro second, stopping once the residual is fully covered or
    /// fleet headroom runs out.
    /// </summary>
    /// <param name="context">An immutable snapshot of the current interval.</param>
    /// <returns>
    /// Zero or more discharge or surplus-charge intents, or <see cref="StorageDecision.None"/>
    /// when the residual is zero or no fleet has headroom.
    /// </returns>
    public StorageDecision Decide(DispatchContext context)
    {
        if (context.Residual == Power.Zero)
        {
            return StorageDecision.None;
        }

        Power remaining = Power.FromMegawatts(Math.Abs(context.Residual.Megawatts));
        var intents = new List<StorageIntent>();
        foreach (StorageFleetSnapshot fleet in context.StorageFleets
                     .OrderBy(fleet => PriorityFor(fleet.StorageTechnology)))
        {
            Power fleetHeadroom = context.Residual > Power.Zero
                ? fleet.DischargeHeadroom
                : fleet.ChargeHeadroom;
            Power requestedMagnitude = Power.Min(remaining, fleetHeadroom);
            if (requestedMagnitude == Power.Zero)
            {
                continue;
            }

            Power requestedFlow = context.Residual > Power.Zero
                ? requestedMagnitude
                : requestedMagnitude * -1;
            intents.Add(new StorageIntent(
                fleet.StorageTechnology,
                requestedFlow,
                context.Residual < Power.Zero ? ChargeSource.Surplus : null));
            remaining -= requestedMagnitude;
            if (remaining == Power.Zero)
            {
                break;
            }
        }

        return intents.Count == 0 ? StorageDecision.None : new StorageDecision(intents);
    }

    private static int PriorityFor(StorageTechnology technology) =>
        technology switch
        {
            StorageTechnology.Battery => 0,
            StorageTechnology.PumpedHydro => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(technology)),
        };
}
