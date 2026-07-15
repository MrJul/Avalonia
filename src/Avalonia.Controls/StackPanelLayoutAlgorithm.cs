using System;
using Avalonia.Layout;
using Avalonia.Layout.Pipeline;

namespace Avalonia.Controls;

/// <summary>
/// The <see cref="LayoutPipeline"/> algorithm for <see cref="StackPanel"/>. Every child gets
/// the available size with the stacking dimension unbounded — the constraints don't depend on
/// the siblings, making this an <see cref="LayoutChildrenDependency.Independent"/> container
/// whose children can be measured in parallel.
/// </summary>
/// <remarks>
/// The snapshot only contains visible children, so spacing simply applies between every
/// consecutive pair, like the classic engine skipping invisible children.
/// The snap points changed event raised by the classic ArrangeOverride is not implemented
/// (it belongs to the publish stage).
/// </remarks>
internal sealed class StackPanelLayoutAlgorithm : LayoutAlgorithm
{
    private readonly bool _horizontal;
    private readonly double _spacing;

    public StackPanelLayoutAlgorithm(LayoutNodeInputs inputs, Orientation orientation, double spacing)
        : base(inputs)
    {
        _horizontal = orientation == Orientation.Horizontal;
        _spacing = spacing;
    }

    public override Size GetChildAvailableSize(int childIndex, Size availableSize, ReadOnlySpan<Size> measuredSiblings)
        => _horizontal ?
            availableSize.WithWidth(double.PositiveInfinity) :
            availableSize.WithHeight(double.PositiveInfinity);

    public override Size CombineChildSizes(Size availableSize, ReadOnlySpan<Size> childSizes)
    {
        double stacked = 0.0, cross = 0.0;

        foreach (ref readonly var childSize in childSizes)
        {
            if (_horizontal)
            {
                stacked += childSize.Width + _spacing;
                cross = Math.Max(cross, childSize.Height);
            }
            else
            {
                stacked += childSize.Height + _spacing;
                cross = Math.Max(cross, childSize.Width);
            }
        }

        if (childSizes.Length > 0)
            stacked -= _spacing;

        return _horizontal ? new Size(stacked, cross) : new Size(cross, stacked);
    }

    public override void ArrangeChildren(Size finalSize, Size desiredSize, ReadOnlySpan<Size> childSizes, Span<Rect> childSlots)
    {
        var offset = 0.0;

        for (var i = 0; i < childSlots.Length; i++)
        {
            ref readonly var childSize = ref childSizes[i];

            if (_horizontal)
            {
                childSlots[i] = new Rect(offset, 0.0, childSize.Width, Math.Max(finalSize.Height, childSize.Height));
                offset += childSize.Width + _spacing;
            }
            else
            {
                childSlots[i] = new Rect(0.0, offset, Math.Max(finalSize.Width, childSize.Width), childSize.Height);
                offset += childSize.Height + _spacing;
            }
        }
    }
}
