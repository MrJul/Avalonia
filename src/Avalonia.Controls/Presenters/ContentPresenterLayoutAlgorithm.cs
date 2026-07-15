using System;
using Avalonia.Layout;
using Avalonia.Layout.Pipeline;

namespace Avalonia.Controls.Presenters;

/// <summary>
/// The <see cref="LayoutPipeline"/> algorithm for <see cref="ContentPresenter"/>: measures like
/// a decorator (padding plus border thickness around the child) and arranges the child slot
/// according to the content alignments, mirroring the classic
/// <c>ContentPresenter.ArrangeOverrideImpl</c>.
/// </summary>
internal sealed class ContentPresenterLayoutAlgorithm : DecoratorLayoutAlgorithm
{
    private readonly HorizontalAlignment _horizontalContentAlignment;
    private readonly VerticalAlignment _verticalContentAlignment;

    public ContentPresenterLayoutAlgorithm(
        LayoutNodeInputs inputs,
        Thickness padding,
        HorizontalAlignment horizontalContentAlignment,
        VerticalAlignment verticalContentAlignment)
        : base(inputs, padding)
    {
        _horizontalContentAlignment = horizontalContentAlignment;
        _verticalContentAlignment = verticalContentAlignment;
    }

    public override void ArrangeChildren(Size finalSize, Size desiredSize, ReadOnlySpan<Size> childSizes, Span<Rect> childSlots)
    {
        var availableSize = finalSize;
        var sizeForChild = availableSize;
        var originX = 0.0;
        var originY = 0.0;

        if (_horizontalContentAlignment != HorizontalAlignment.Stretch)
        {
            sizeForChild = sizeForChild.WithWidth(Math.Min(sizeForChild.Width, desiredSize.Width));
        }

        if (_verticalContentAlignment != VerticalAlignment.Stretch)
        {
            sizeForChild = sizeForChild.WithHeight(Math.Min(sizeForChild.Height, desiredSize.Height));
        }

        if (Inputs.UseLayoutRounding)
        {
            sizeForChild = LayoutHelper.RoundLayoutSizeUp(sizeForChild, Inputs.LayoutScale);
            availableSize = LayoutHelper.RoundLayoutSizeUp(availableSize, Inputs.LayoutScale);
        }

        switch (_horizontalContentAlignment)
        {
            case HorizontalAlignment.Center:
                originX += (availableSize.Width - sizeForChild.Width) / 2;
                break;
            case HorizontalAlignment.Right:
                originX += availableSize.Width - sizeForChild.Width;
                break;
        }

        switch (_verticalContentAlignment)
        {
            case VerticalAlignment.Center:
                originY += (availableSize.Height - sizeForChild.Height) / 2;
                break;
            case VerticalAlignment.Bottom:
                originY += availableSize.Height - sizeForChild.Height;
                break;
        }

        var origin = new Point(originX, originY);

        if (Inputs.UseLayoutRounding)
        {
            origin = LayoutHelper.RoundLayoutPoint(origin, Inputs.LayoutScale);
        }

        childSlots.Fill(new Rect(origin, sizeForChild).Deflate(Padding));
    }
}
