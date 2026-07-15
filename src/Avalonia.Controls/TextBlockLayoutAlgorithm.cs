using Avalonia.Layout;
using Avalonia.Layout.Pipeline;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Avalonia.Controls;

/// <summary>
/// The <see cref="LayoutPipeline"/> algorithm for a plain-text <see cref="TextBlock"/>: shapes
/// the text against the measure constraint — potentially on a worker thread, which is where the
/// pipeline's parallelism actually pays off — and hands the resulting layout back to the
/// control during the publish stage. When the final arranged size matches the measure
/// constraint (the common case), the layout shaped in parallel is adopted for rendering;
/// otherwise the render layout is lazily recreated at the final size, like the classic engine.
/// </summary>
/// <remarks>
/// Everything shaping needs is captured on the UI thread at construction: the property system
/// enforces thread affinity, so <see cref="MeasureContent"/> must not touch the control.
/// Shaping itself relies on the font manager and text shaper tolerating concurrent use.
/// </remarks>
internal sealed class TextBlockLayoutAlgorithm(
    LayoutNodeInputs inputs,
    Thickness padding,
    ITextSource textSource,
    GenericTextParagraphProperties paragraphProperties,
    TextTrimming textTrimming,
    int maxLines,
    TextRunCache textRunCache)
    : LayoutAlgorithm(inputs)
{
    private TextLayout? _measuredLayout;
    private Size _measuredConstraint;

    public override Size MeasureContent(Size availableSize)
    {
        var constraint = availableSize.Deflate(padding);

        _measuredLayout?.Dispose();
        _measuredLayout = TextBlock.CreateTextLayout(
            textSource,
            paragraphProperties,
            textTrimming,
            maxLines,
            textRunCache,
            constraint);
        _measuredConstraint = constraint;

        return new Size(_measuredLayout.WidthIncludingTrailingWhitespace, _measuredLayout.Height).Inflate(padding);
    }

    public override void OnPublish(Layoutable control, Size finalSize)
    {
        ((TextBlock) control).SetPipelineTextLayout(_measuredLayout, _measuredConstraint, finalSize.Deflate(padding));
        _measuredLayout = null;
    }
}
