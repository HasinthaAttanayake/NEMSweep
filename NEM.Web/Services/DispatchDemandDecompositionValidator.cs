namespace NEM.Web.Services;

public static class DispatchDemandDecompositionValidator
{
    private const double RoundedValueMaximumErrorMw = 0.05;
    private const double FloatingPointEpsilonMw = 1e-12;

    public static bool IsWithinRoundingEnvelope(
        double baseDemandMw,
        IEnumerable<double> additiveComponentsMw,
        double totalDemandMw)
    {
        ArgumentNullException.ThrowIfNull(additiveComponentsMw);

        double composedDemandMw = baseDemandMw;
        int summandCount = 1;
        foreach (double componentMw in additiveComponentsMw)
        {
            composedDemandMw += componentMw;
            summandCount++;
        }

        double roundingEnvelopeMw = (summandCount + 1) * RoundedValueMaximumErrorMw;
        return Math.Abs(totalDemandMw - composedDemandMw)
            <= roundingEnvelopeMw + FloatingPointEpsilonMw;
    }
}