using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace NEM.Web.Services;

/// <summary>
/// Brings an element into view after a selection changed something further down the page. The
/// module is loaded on first use rather than at start-up, so a reader who never selects anything
/// never pays for it, and every failure is swallowed: scrolling is a courtesy, and a page that
/// throws because a courtesy failed is worse than one that simply does not scroll.
/// </summary>
public sealed class ViewportScroller(IJSRuntime jsRuntime, NavigationManager navigation) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task RevealAsync(ElementReference element)
    {
        try
        {
            // Resolved against the application base rather than written relative. A relative
            // specifier resolves against the current route, so "./js/nem.js" asks for
            // /sweeps/{id}/js/nem.js on a sweep page and quietly 404s — and because a failed
            // scroll is swallowed below, it fails without a trace.
            string module = new Uri(new Uri(navigation.BaseUri), "js/nem.js").ToString();
            _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", module);
            await _module.InvokeVoidAsync("revealIfOffScreen", element);
        }
        catch (JSException)
        {
            // The element may have been replaced by a re-render between the call and its arrival.
        }
        catch (InvalidOperationException)
        {
            // Prerendering or a disposed circuit: there is no browser to scroll.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is going away; the module goes with it.
        }
    }
}
