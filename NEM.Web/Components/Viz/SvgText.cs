using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace NEM.Web.Components.Viz;

/// <summary>
/// An SVG <c>text</c> element. Razor reserves the <c>&lt;text&gt;</c> tag for its own markup
/// transitions and refuses to compile one carrying attributes, so plots build theirs through the
/// render tree instead. Everything else about it is an ordinary labelled element.
/// </summary>
public sealed class SvgText : ComponentBase
{
    [Parameter] public double X { get; set; }

    [Parameter] public double Y { get; set; }

    [Parameter] public string? Class { get; set; }

    /// <summary>Maps to <c>text-anchor</c>: <c>start</c>, <c>middle</c> or <c>end</c>.</summary>
    [Parameter] public string? Anchor { get; set; }

    [Parameter] public string? Transform { get; set; }

    [Parameter] public string Value { get; set; } = string.Empty;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.OpenElement(0, "text");
        builder.AddAttribute(1, "x", PlotFormat.Length(X));
        builder.AddAttribute(2, "y", PlotFormat.Length(Y));
        if (!string.IsNullOrWhiteSpace(Class))
        {
            builder.AddAttribute(3, "class", Class);
        }

        if (!string.IsNullOrWhiteSpace(Anchor))
        {
            builder.AddAttribute(4, "text-anchor", Anchor);
        }

        if (!string.IsNullOrWhiteSpace(Transform))
        {
            builder.AddAttribute(5, "transform", Transform);
        }

        builder.AddContent(6, Value);
        builder.CloseElement();
    }
}
