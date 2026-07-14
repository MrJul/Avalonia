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
internal sealed class TextBlockLayoutAlgorithm : LayoutAlgorithm
{
    private readonly TextBlock _textBlock;
    private readonly Thickness _padding;
    private readonly string? _text;
    private readonly GenericTextParagraphProperties _paragraphProperties;
    private readonly TextTrimming _textTrimming;
    private readonly int _maxLines;
    private readonly TextRunCache _textRunCache;
    private TextLayout? _measuredLayout;
    private Size _measuredConstraint;

    public TextBlockLayoutAlgorithm(TextBlock textBlock, Thickness padding)
    {
        _textBlock = textBlock;
        _padding = padding;
        _text = textBlock.Text;

        var defaultProperties = new GenericTextRunProperties(
            new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch),
            textBlock.FontSize,
            textBlock.TextDecorations,
            textBlock.Foreground,
            fontFeatures: textBlock.FontFeatures);

        _paragraphProperties = new GenericTextParagraphProperties(
            textBlock.FlowDirection,
            textBlock.TextAlignment,
            true,
            false,
            defaultProperties,
            textBlock.TextWrapping,
            textBlock.LineHeight,
            0,
            textBlock.LetterSpacing)
        {
            LineSpacing = textBlock.LineSpacing,
        };

        _textTrimming = textBlock.TextTrimming;
        _maxLines = textBlock.MaxLines;
        _textRunCache = textBlock.TextRunCache;
    }

    public override Size MeasureContent(Size availableSize)
    {
        var constraint = availableSize.Deflate(_padding);
        var maxWidth = double.IsNaN(constraint.Width) ? 0.0 : constraint.Width;
        var maxHeight = double.IsNaN(constraint.Height) ? 0.0 : constraint.Height;

        _measuredLayout?.Dispose();
        _measuredLayout = new TextLayout(
            new TextBlock.SimpleTextSource(_text ?? "", _paragraphProperties.DefaultTextRunProperties),
            _paragraphProperties,
            _textTrimming,
            maxWidth,
            maxHeight,
            _maxLines,
            _textRunCache);
        _measuredConstraint = constraint;

        return new Size(_measuredLayout.WidthIncludingTrailingWhitespace, _measuredLayout.Height).Inflate(_padding);
    }

    public override void OnPublish(Layoutable control, Size finalSize)
    {
        _textBlock.SetPipelineTextLayout(_measuredLayout, _measuredConstraint, finalSize.Deflate(_padding));
        _measuredLayout = null;
    }
}
