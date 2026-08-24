using System.Globalization;
using System.Runtime.CompilerServices;

namespace NEMSweep.Web.Tests;

/// <summary>
/// Pins the culture every test runs under. The site formats its user-facing numbers in the reader's
/// culture — <c>PlotFormat</c> and the findings both take <see cref="CultureInfo.CurrentCulture"/>,
/// which is what a page rendered in a browser should do — so a test asserting "5.93%" or "$11.06b"
/// is really asserting the agent's locale. On one with a comma decimal separator the whole suite
/// fails on formatting rather than on behaviour.
/// </summary>
/// <remarks>
/// Invariant rather than a named culture: it is the one the assertions were written against, and it
/// cannot drift with an ICU update. Anything that needs to prove culture-specific behaviour should
/// set its own culture for the duration of that test rather than relying on the ambient one.
/// </remarks>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Pin()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
    }
}
