using MudBlazor;

namespace NEMSweep.Web.Layout;

/// <summary>
/// The one MudBlazor theme both layouts hand to <c>MudThemeProvider</c>.
/// </summary>
/// <remarks>
/// MudBlazor's stock primary is a purple unrelated to the site's own palette. The input pages pick
/// an accent from what they show (demand teal, generation red, wind green - see the per-page
/// <c>--accent</c> custom property), but MudBlazor components have one global theme and appear on
/// every page, so this is the brand ink rather than any one of those data colours.
/// </remarks>
internal static class AppTheme
{
    public static MudTheme Instance { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#141b2d",
        },
        // MudBlazor's stylesheet loads after app.css and sets its own family on body, so the site
        // font has to be stated here as well or every Mud component keeps MudBlazor's Roboto.
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Inter", "Segoe UI", "system-ui", "-apple-system", "sans-serif"],
            },
        },
    };
}
