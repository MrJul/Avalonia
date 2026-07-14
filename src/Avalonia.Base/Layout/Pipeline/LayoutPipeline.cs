using System;
using System.Threading.Tasks;
using Avalonia.Metadata;
using Avalonia.Utilities;

namespace Avalonia.Layout.Pipeline;

/// <summary>
/// Experimental staged layout engine designed for parallelism. A frame is a strict pipeline:
/// <list type="number">
/// <item><b>Inputs</b> (UI thread, before the frame): bindings, animations and user mutations
/// are flushed by the dispatcher; the property system is then considered frozen.</item>
/// <item><b>Prepare</b> (UI thread, mutates the tree): styling and template expansion run to
/// completion so the tree reaches its final shape. This is the only stage allowed to mutate
/// controls before publish.</item>
/// <item><b>Snapshot</b> (UI thread, reads the tree): the opted-in controls are frozen into an
/// immutable, index-based <see cref="LayoutTreeSnapshot"/>. Controls opt in by overriding
/// <see cref="Layoutable.GetLayoutAlgorithm"/>; others are skipped entirely and won't render.</item>
/// <item><b>Measure</b> (parallel): a pure fork-join pass over the snapshot. Children of
/// <see cref="LayoutChildrenDependency.Independent"/> containers are measured concurrently;
/// <see cref="LayoutChildrenDependency.Sequential"/> containers serialize their own level while
/// their subtrees still parallelize internally.</item>
/// <item><b>Arrange</b> (parallel): slot rects flow top-down; children always arrange
/// concurrently since their slots are computed before recursing.</item>
/// <item><b>Publish</b> (UI thread, mutates controls): desired sizes and bounds are written back
/// to the live controls, making the results observable. Layout events belong here (not yet
/// implemented).</item>
/// </list>
/// Not yet handled: container queries, virtualization islands, the scrollbar-style bounded
/// outer loop, and effective viewport notifications.
/// </summary>
[Unstable]
public sealed class LayoutPipeline
{
    /// <summary>
    /// Experimental switch: when enabled before a top level is created, its layout is driven by
    /// a <see cref="PipelineLayoutManager"/> instead of the classic engine. Controls that don't
    /// override <see cref="Layoutable.GetLayoutAlgorithm"/> are then skipped along with their
    /// subtree and won't render.
    /// </summary>
    public static bool UseForTopLevels { get; set; }

    /// <summary>
    /// Minimum number of nodes in a subtree for the measure and arrange stages to fork its
    /// children instead of processing them on the current thread.
    /// </summary>
    public int ParallelismThreshold { get; init; } = 32;

    /// <summary>
    /// Runs the layout stages for the tree rooted at <paramref name="root"/>.
    /// </summary>
    /// <param name="root">The root of the tree to lay out. Must provide a layout algorithm.</param>
    /// <param name="availableSize">The size available to the root.</param>
    /// <param name="arrangeRect">
    /// The rect the root is arranged into, or null to use the measured desired size.
    /// </param>
    public void ExecuteFrame(Layoutable root, Size availableSize, Rect? arrangeRect = null)
    {
        // Stage 1 — inputs: assumed already flushed by the dispatcher.

        // Stage 2 — prepare (UI thread): bring the tree to its final shape.
        PrepareStage(root);

        // Stage 3 — snapshot (UI thread): freeze the opted-in tree into an immutable arena.
        var tree = SnapshotStage(root)
            ?? throw new InvalidOperationException(
                $"The layout pipeline root ({root.GetType().Name}) must provide a layout algorithm.");

        // Stage 4 — measure (parallel): pure fork-join pass over the snapshot.
        MeasureStage(tree, availableSize);

        // Stage 5 — arrange (parallel): slots down, bounds out.
        ArrangeStage(tree, arrangeRect ?? new Rect(tree.DesiredSize[LayoutTreeSnapshot.RootIndex]));

        // Stage 6 — publish (UI thread): make the results observable on the live controls.
        PublishStage(tree);
    }

    /// <summary>
    /// Stage 2: applies styling and expands templates over the whole subtree, so that the tree
    /// has its final shape before it is snapshotted. Children created by template expansion are
    /// visited in turn as part of the same traversal.
    /// </summary>
    private static void PrepareStage(Layoutable control)
    {
        control.ApplyStyling();
        control.ApplyTemplate();

        var children = control.VisualChildren;

        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is Layoutable layoutable)
                PrepareStage(layoutable);
        }
    }

    /// <summary>
    /// Stage 3: freezes the opted-in controls, their cached layout inputs and the layout scale
    /// into an immutable structure-of-arrays snapshot.
    /// </summary>
    private static LayoutTreeSnapshot? SnapshotStage(Layoutable root)
        => LayoutTreeSnapshot.TryBuild(root, LayoutHelper.GetLayoutScale(root));

    /// <summary>
    /// Stage 4: computes the desired size of every node. This method and everything it calls
    /// only touch snapshot arrays and pure <see cref="LayoutAlgorithm"/> instances, never live
    /// controls — that's the invariant making the fork-join safe.
    /// </summary>
    private void MeasureStage(LayoutTreeSnapshot tree, Size availableSize)
        => MeasureNode(tree, LayoutTreeSnapshot.RootIndex, availableSize);

    private Size MeasureNode(LayoutTreeSnapshot tree, int node, Size availableSize)
    {
        if (!tree.IsVisible[node])
        {
            tree.DesiredSize[node] = default;
            return default;
        }

        // Chrome, mirroring the classic MeasureCore: margins and min/max constraints.
        ref readonly var inputs = ref tree.Inputs[node];
        var scale = tree.Scale;
        var margin = inputs.Margin;
        var useLayoutRounding = inputs.UseLayoutRounding;

        if (useLayoutRounding)
            margin = LayoutHelper.RoundLayoutThickness(margin, scale);

        var minMax = inputs.MinMax;
        var constrainedSize = LayoutHelper.ApplyLayoutConstraints(minMax, availableSize.Deflate(margin));

        // Content and children, through the declarative algorithm.
        var algorithm = tree.Algorithms[node];
        var childCount = tree.ChildrenCount[node];
        Size measured;

        if (childCount == 0)
        {
            measured = algorithm.MeasureContent(constrainedSize);
        }
        else
        {
            MeasureChildren(tree, node, algorithm, constrainedSize);
            measured = algorithm.CombineChildSizes(
                constrainedSize,
                tree.ChildMeasuredSizes.AsSpan(tree.ChildrenStart[node], childCount));
        }

        // Chrome again: clamp, round, add margins back, cap to the available size.
        var width = MathUtilities.Clamp(measured.Width, minMax.MinWidth, minMax.MaxWidth);
        var height = MathUtilities.Clamp(measured.Height, minMax.MinHeight, minMax.MaxHeight);

        if (useLayoutRounding)
            (width, height) = LayoutHelper.RoundLayoutSizeUp(new Size(width, height), scale);

        width += margin.Left + margin.Right;
        height += margin.Top + margin.Bottom;

        if (width > availableSize.Width)
            width = availableSize.Width;

        if (height > availableSize.Height)
            height = availableSize.Height;

        if (width < 0)
            width = 0;

        if (height < 0)
            height = 0;

        var desiredSize = new Size(width, height);
        tree.DesiredSize[node] = desiredSize;
        return desiredSize;
    }

    private void MeasureChildren(LayoutTreeSnapshot tree, int node, LayoutAlgorithm algorithm, Size constrainedSize)
    {
        var start = tree.ChildrenStart[node];
        var count = tree.ChildrenCount[node];

        if (algorithm.MeasureDependency == LayoutChildrenDependency.Sequential)
        {
            for (var i = 0; i < count; i++)
            {
                var childAvailableSize = algorithm.GetChildAvailableSize(
                    i, constrainedSize, tree.ChildMeasuredSizes.AsSpan(start, i));

                tree.ChildMeasuredSizes[start + i] =
                    MeasureNode(tree, tree.ChildrenFlat[start + i], childAvailableSize);
            }
        }
        else if (ShouldFork(tree, node, count))
        {
            Parallel.For(0, count, i =>
            {
                var childAvailableSize = algorithm.GetChildAvailableSize(
                    i, constrainedSize, ReadOnlySpan<Size>.Empty);

                tree.ChildMeasuredSizes[start + i] =
                    MeasureNode(tree, tree.ChildrenFlat[start + i], childAvailableSize);
            });
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                var childAvailableSize = algorithm.GetChildAvailableSize(
                    i, constrainedSize, ReadOnlySpan<Size>.Empty);

                tree.ChildMeasuredSizes[start + i] =
                    MeasureNode(tree, tree.ChildrenFlat[start + i], childAvailableSize);
            }
        }
    }

    /// <summary>
    /// Stage 5: computes the bounds of every node from the slot rect assigned by its parent.
    /// Same purity invariant as the measure stage. Children slots are all known before
    /// recursing, so the arrange stage always forks regardless of the measure dependency.
    /// </summary>
    private void ArrangeStage(LayoutTreeSnapshot tree, Rect rect)
        => ArrangeNode(tree, LayoutTreeSnapshot.RootIndex, rect);

    private void ArrangeNode(LayoutTreeSnapshot tree, int node, Rect finalRect)
    {
        if (!tree.IsVisible[node])
            return;

        // Chrome, mirroring the classic ArrangeCore: margins, alignment, min/max and rounding.
        ref readonly var inputs = ref tree.Inputs[node];
        var scale = tree.Scale;
        var useLayoutRounding = inputs.UseLayoutRounding;
        var margin = inputs.Margin;
        var originX = finalRect.X + margin.Left;
        var originY = finalRect.Y + margin.Top;

        // Margin has to be treated separately because the layout rounding function is not linear:
        // see the classic ArrangeCore.
        if (useLayoutRounding)
            margin = LayoutHelper.RoundLayoutThickness(margin, scale);

        var availableWidthMinusMargins = finalRect.Width - margin.Left - margin.Right;
        if (availableWidthMinusMargins < 0)
            availableWidthMinusMargins = 0;

        var availableHeightMinusMargins = finalRect.Height - margin.Top - margin.Bottom;
        if (availableHeightMinusMargins < 0)
            availableHeightMinusMargins = 0;

        var availableSizeMinusMargins = new Size(availableWidthMinusMargins, availableHeightMinusMargins);
        var horizontalAlignment = inputs.HorizontalAlignment;
        var verticalAlignment = inputs.VerticalAlignment;
        var desiredSize = tree.DesiredSize[node];
        var size = availableSizeMinusMargins;

        if (horizontalAlignment != HorizontalAlignment.Stretch)
            size = size.WithWidth(Math.Min(size.Width, desiredSize.Width - margin.Left - margin.Right));

        if (verticalAlignment != VerticalAlignment.Stretch)
            size = size.WithHeight(Math.Min(size.Height, desiredSize.Height - margin.Top - margin.Bottom));

        size = LayoutHelper.ApplyLayoutConstraints(inputs.MinMax, size);

        if (useLayoutRounding)
        {
            size = LayoutHelper.RoundLayoutSizeUp(size, scale);
            availableSizeMinusMargins = LayoutHelper.RoundLayoutSizeUp(availableSizeMinusMargins, scale);
        }

        // Children slots, through the declarative algorithm, then fork.
        var childCount = tree.ChildrenCount[node];

        if (childCount > 0)
        {
            var start = tree.ChildrenStart[node];
            var algorithm = tree.Algorithms[node];

            algorithm.ArrangeChildren(
                size,
                desiredSize,
                tree.ChildMeasuredSizes.AsSpan(start, childCount),
                tree.ChildSlots.AsSpan(start, childCount));

            if (ShouldFork(tree, node, childCount))
            {
                Parallel.For(0, childCount, i =>
                    ArrangeNode(tree, tree.ChildrenFlat[start + i], tree.ChildSlots[start + i]));
            }
            else
            {
                for (var i = 0; i < childCount; i++)
                    ArrangeNode(tree, tree.ChildrenFlat[start + i], tree.ChildSlots[start + i]);
            }
        }

        switch (horizontalAlignment)
        {
            case HorizontalAlignment.Center:
            case HorizontalAlignment.Stretch:
                originX += (availableSizeMinusMargins.Width - size.Width) / 2;
                break;
            case HorizontalAlignment.Right:
                originX += availableSizeMinusMargins.Width - size.Width;
                break;
        }

        switch (verticalAlignment)
        {
            case VerticalAlignment.Center:
            case VerticalAlignment.Stretch:
                originY += (availableSizeMinusMargins.Height - size.Height) / 2;
                break;
            case VerticalAlignment.Bottom:
                originY += availableSizeMinusMargins.Height - size.Height;
                break;
        }

        var origin = new Point(originX, originY);

        if (useLayoutRounding)
            origin = LayoutHelper.RoundLayoutPoint(origin, scale);

        tree.Bounds[node] = new Rect(origin, size);
        tree.Arranged[node] = true;
    }

    /// <summary>
    /// Stage 6: writes the computed desired sizes and bounds back to the live controls.
    /// This runs on the UI thread and is the only stage after prepare allowed to mutate
    /// controls. Batched layout events (LayoutUpdated, effective viewport) belong here.
    /// </summary>
    private static void PublishStage(LayoutTreeSnapshot tree)
    {
        for (var node = 0; node < tree.Count; node++)
        {
            var control = tree.Controls[node];

            if (tree.Arranged[node])
            {
                var bounds = tree.Bounds[node];
                control.PublishPipelineLayout(tree.DesiredSize[node], bounds);
                tree.Algorithms[node].OnPublish(control, bounds.Size);
            }
            else
            {
                control.PublishPipelineLayout(tree.DesiredSize[node], null);
            }
        }
    }

    private bool ShouldFork(LayoutTreeSnapshot tree, int node, int childCount)
        => childCount > 1 && tree.SubtreeSize[node] >= ParallelismThreshold;
}
