using System;
using Avalonia.Layout.Pipeline;

namespace Avalonia.Controls;

/// <summary>
/// The <see cref="LayoutPipeline"/> algorithm for <see cref="Decorator"/> and
/// <see cref="Border"/>: the child receives the available size deflated by the padding
/// (plus the border thickness for a border), and the desired size is the child's size
/// inflated back by it. The padding is captured pre-rounded at snapshot time.
/// </summary>
internal class DecoratorLayoutAlgorithm : LayoutAlgorithm
{
    public DecoratorLayoutAlgorithm(Thickness padding) => Padding = padding;

    protected Thickness Padding { get; }

    public override Size MeasureContent(Size availableSize)
        => new Size().Inflate(Padding);

    public override Size GetChildAvailableSize(int childIndex, Size availableSize, ReadOnlySpan<Size> measuredSiblings)
        => availableSize.Deflate(Padding);

    public override Size CombineChildSizes(Size availableSize, ReadOnlySpan<Size> childSizes)
    {
        // A decorator has a single child, but be robust to subclasses adding more visual children.
        double width = 0.0, height = 0.0;

        foreach (ref readonly var childSize in childSizes)
        {
            if (childSize.Width > width)
                width = childSize.Width;

            if (childSize.Height > height)
                height = childSize.Height;
        }

        return new Size(width, height).Inflate(Padding);
    }

    public override void ArrangeChildren(Size finalSize, Size desiredSize, ReadOnlySpan<Size> childSizes, Span<Rect> childSlots)
        => childSlots.Fill(new Rect(finalSize).Deflate(Padding));
}
