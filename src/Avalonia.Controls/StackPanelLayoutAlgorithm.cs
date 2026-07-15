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

    public override Size CombineChildSizes(Size availableSize, ReadOnlySpan<Size> childSizes, ReadOnlySpan<bool> childrenVisibility)
    {
        double stacked = 0.0, cross = 0.0;
        var hasVisibleChild = false;

        for (var i = 0; i < childSizes.Length; i++)
        {
            // Like the classic MeasureOverride, an invisible child contributes its (empty)
            // desired size but no spacing.
            ref readonly var childSize = ref childSizes[i];
            var isVisible = childrenVisibility[i];
            var spacing = isVisible ? _spacing : 0.0;
            hasVisibleChild |= isVisible;

            if (_horizontal)
            {
                stacked += spacing + childSize.Width;
                cross = Math.Max(cross, childSize.Height);
            }
            else
            {
                stacked += spacing + childSize.Height;
                cross = Math.Max(cross, childSize.Width);
            }
        }

        if (hasVisibleChild)
            stacked -= _spacing;

        return _horizontal ? new Size(stacked, cross) : new Size(cross, stacked);
    }

    public override void ArrangeChildren(Size finalSize, Size desiredSize, ReadOnlySpan<Size> childSizes, ReadOnlySpan<bool> childrenVisibility, Span<Rect> childSlots)
    {
        var offset = 0.0;

        for (var i = 0; i < childSlots.Length; i++)
        {
            // Invisible children are skipped like in the classic ArrangeOverride: no slot, no
            // spacing. Their slot value is never used by the arrange stage.
            if (!childrenVisibility[i])
                continue;

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
