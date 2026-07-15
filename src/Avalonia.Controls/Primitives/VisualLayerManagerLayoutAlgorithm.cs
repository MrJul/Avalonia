using System;
using Avalonia.Layout.Pipeline;

namespace Avalonia.Controls.Primitives;

/// <summary>
/// The <see cref="LayoutPipeline"/> algorithm for <see cref="VisualLayerManager"/>: the
/// decorated child behaves like in a <see cref="Decorator"/> (available size deflated by the
/// padding, desired size inflated back), while the layers get the full available size and
/// the full final rect, and contribute nothing to the desired size.
/// </summary>
internal sealed class VisualLayerManagerLayoutAlgorithm : LayoutAlgorithm
{
    private readonly Thickness _padding;
    private readonly int _childIndex;

    public VisualLayerManagerLayoutAlgorithm(LayoutNodeInputs inputs, Thickness padding, int childIndex)
        : base(inputs)
    {
        _padding = padding;
        _childIndex = childIndex;
    }

    public override Size MeasureContent(Size availableSize)
        => new Size().Inflate(_padding);

    public override Size GetChildAvailableSize(int childIndex, Size availableSize, ReadOnlySpan<Size> measuredSiblings)
        => childIndex == _childIndex ? availableSize.Deflate(_padding) : availableSize;

    public override Size CombineChildSizes(Size availableSize, ReadOnlySpan<Size> childSizes)
        => _childIndex >= 0 && _childIndex < childSizes.Length ?
            childSizes[_childIndex].Inflate(_padding) :
            new Size().Inflate(_padding);

    public override void ArrangeChildren(Size finalSize, Size desiredSize, ReadOnlySpan<Size> childSizes, Span<Rect> childSlots)
    {
        childSlots.Fill(new Rect(finalSize));

        if (_childIndex >= 0 && _childIndex < childSlots.Length)
            childSlots[_childIndex] = new Rect(finalSize).Deflate(_padding);
    }
}
