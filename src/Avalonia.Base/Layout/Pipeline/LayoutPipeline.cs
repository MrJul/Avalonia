using System;
using System.Threading;
using Avalonia.Metadata;
using Avalonia.Utilities;

namespace Avalonia.Layout.Pipeline;

/// <summary>
/// Experimental staged layout engine designed for parallelism. A frame is a strict pipeline:
/// <list type="number">
/// <item><b>Inputs</b> (UI thread, before the frame): bindings, animations and user mutations
/// are flushed by the dispatcher; the property system is then considered frozen.</item>
/// <item><b>Prepare + snapshot</b> (UI thread, merged into a single walk): each control is
/// styled and templated — the only mutations allowed before publish — right before being
/// frozen into an immutable, index-based <see cref="LayoutTreeSnapshot"/>, so the tree reaches
/// its final shape as the arena is built. Controls opt in by overriding
/// <see cref="Layoutable.LayoutAlgorithm"/>; others are skipped entirely, stay unprepared
/// and won't render.</item>
/// <item><b>Measure</b> (parallel): a dependency-driven wavefront over the snapshot, executed
/// on the dedicated <see cref="LayoutWorkerPool"/>. Work items are node indices: a container
/// pushes its children as items (<see cref="LayoutChildrenDependency.Independent"/> all at
/// once, <see cref="LayoutChildrenDependency.Sequential"/> one at a time), and its own combine
/// runs when the last child completes — no work item ever blocks. Subtrees smaller than
/// <see cref="ParallelismThreshold"/> are processed inline.</item>
/// <item><b>Arrange</b> (parallel): slot rects flow top-down through the same pool; a node is
/// ready as soon as its parent assigned its slot, so the wavefront has no join at all.</item>
/// <item><b>Publish</b> (UI thread, mutates controls): desired sizes and bounds are written back
/// to the live controls and <see cref="LayoutAlgorithm.OnPublish"/> hooks run, making the
/// results observable. Layout events belong here (not yet implemented).</item>
/// </list>
/// Not yet handled: container queries, virtualization islands, the scrollbar-style bounded
/// outer loop, and effective viewport notifications.
/// </summary>
[Unstable]
public sealed class LayoutPipeline
{
    private readonly LayoutTreeSnapshotBuilder _snapshotBuilder = new();

    /// <summary>
    /// Experimental switch: when enabled before a top level is created, its layout is driven by
    /// a <see cref="PipelineLayoutManager"/> instead of the classic engine. Controls that don't
    /// override <see cref="Layoutable.LayoutAlgorithm"/> are then skipped along with their
    /// subtree and won't render.
    /// </summary>
    public static bool UseForTopLevels { get; set; }

    /// <summary>
    /// Minimum number of nodes in a subtree for it to be split into separate work items
    /// executed by the <see cref="LayoutWorkerPool"/>; smaller subtrees are processed inline
    /// on the thread that reaches them, and a whole tree below the threshold never touches
    /// the pool at all.
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

        // Stages 2 + 3 — prepare + snapshot (UI thread), merged into a single walk: each
        // control is styled and templated right before being captured into the immutable arena.
        using var tree = SnapshotStage(root)
            ?? throw new InvalidOperationException(
                $"The layout pipeline root ({root.GetType().Name}) must provide a layout algorithm.");

        // Stage 4 — measure (parallel): dependency-driven wavefront over the snapshot.
        MeasureStage(tree, availableSize);

        // Stage 5 — arrange (parallel): slots down, bounds out.
        ArrangeStage(tree, arrangeRect ?? new Rect(tree.DesiredSize[LayoutTreeSnapshot.RootIndex]));

        // Stage 6 — publish (UI thread): make the results observable on the live controls.
        PublishStage(tree);
    }

    /// <summary>
    /// Stages 2 and 3, merged into a single walk over the tree: each control is styled and
    /// templated (stage 2 — the only mutations allowed before publish) right before its
    /// opt-in check and its capture into the immutable structure-of-arrays snapshot (stage 3),
    /// so styled values are in effect when captured and template-created children are visited
    /// in turn. Controls that don't participate in the snapshot are left unprepared, like the
    /// classic engine that only styles controls when measuring them.
    /// </summary>
    private LayoutTreeSnapshot? SnapshotStage(Layoutable root)
    {
        root.ApplyStyling();
        root.ApplyTemplate();

        if (root.LayoutAlgorithm is not { } algorithm)
            return null;

        var scale = LayoutHelper.GetLayoutScale(root);
        return _snapshotBuilder.Build(root, algorithm, scale);
    }

    /// <summary>
    /// Stage 4: computes the desired size of every node. This method and everything it calls
    /// only touch snapshot arrays and pure <see cref="LayoutAlgorithm"/> instances, never live
    /// controls — that's the invariant making the multithreaded wavefront safe.
    /// </summary>
    private void MeasureStage(LayoutTreeSnapshot tree, Size availableSize)
    {
        const int root = LayoutTreeSnapshot.RootIndex;

        if (!tree.Nodes.GetRef(root).IsVisible || ShouldProcessInline(tree, root))
        {
            MeasureNode(tree, root, availableSize);
        }
        else
        {
            tree.NodeAvailableSize[root] = availableSize;
            LayoutWorkerPool.Instance.Execute(new MeasureProcessor(this, tree), root);
        }
    }

    private bool ShouldProcessInline(LayoutTreeSnapshot tree, int node)
        => tree.ChildrenCount[node] == 0 || tree.SubtreeSize[node] < ParallelismThreshold;

    private sealed class MeasureProcessor : ILayoutWorkProcessor
    {
        private readonly LayoutPipeline _pipeline;
        private readonly LayoutTreeSnapshot _tree;

        public MeasureProcessor(LayoutPipeline pipeline, LayoutTreeSnapshot tree)
        {
            _pipeline = pipeline;
            _tree = tree;
        }

        public void Process(int item) => _pipeline.ProcessMeasureItem(_tree, item);
    }

    private void ProcessMeasureItem(LayoutTreeSnapshot tree, int node)
    {
        var availableSize = tree.NodeAvailableSize[node];

        // Classic engine guard (Layoutable.Measure): a subtree whose measures are all valid
        // and which receives the same constraint as the previous pass isn't re-measured — the
        // prefilled desired size is reused and the whole subtree is pruned. The captured value
        // is NaN (never equal) when anything in the subtree is invalid.
        ref var record = ref tree.Nodes.GetRef(node);
        if (!record.IsMeasureValid || record.PreviousMeasureSize != availableSize)
        {
            CompleteMeasuredNode(tree, node);
            return;
        }

        if (ShouldProcessInline(tree, node))
        {
            MeasureNode(tree, node, availableSize);
            CompleteMeasuredNode(tree, node);
            return;
        }

        // Expand the container: compute its constraint and push its children as work items.
        // Its own combine runs when the last child completes, in CompleteMeasuredNode.
        var constrainedSize = ComputeConstrainedSize(tree, node, availableSize);
        tree.NodeConstrainedSize[node] = constrainedSize;

        var algorithm = tree.Algorithms[node];
        var start = tree.ChildrenStart[node];
        var count = tree.ChildrenCount[node];

        if (algorithm.MeasureDependency == LayoutChildrenDependency.Sequential)
        {
            tree.SequentialCursor[node] = 0;
            ScheduleMeasure(tree, tree.ChildrenFlat[start],
                algorithm.GetChildAvailableSize(0, constrainedSize, ReadOnlySpan<Size>.Empty));
        }
        else
        {
            tree.PendingChildren[node] = count;

            for (var i = 0; i < count; i++)
            {
                ScheduleMeasure(tree, tree.ChildrenFlat[start + i],
                    algorithm.GetChildAvailableSize(i, constrainedSize, ReadOnlySpan<Size>.Empty));
            }
        }
    }

    private static void ScheduleMeasure(LayoutTreeSnapshot tree, int node, Size availableSize)
    {
        tree.NodeAvailableSize[node] = availableSize;
        LayoutWorkerPool.Instance.Enqueue(node);
    }

    /// <summary>
    /// Called when a node's desired size is known: records it in the parent's child sizes,
    /// then either schedules the next sequential sibling or, when this was the last pending
    /// child, combines the parent and continues cascading upwards.
    /// </summary>
    private void CompleteMeasuredNode(LayoutTreeSnapshot tree, int node)
    {
        while (true)
        {
            ref readonly var record = ref tree.Nodes.GetRef(node);
            var parent = record.Parent;

            if (parent < 0)
                return;

            tree.ChildMeasuredSizes[record.IndexInParent] = tree.DesiredSize[node];

            var algorithm = tree.Algorithms[parent];
            var start = tree.ChildrenStart[parent];
            var count = tree.ChildrenCount[parent];

            if (algorithm.MeasureDependency == LayoutChildrenDependency.Sequential)
            {
                // A sequential container has a single child in flight: the thread completing
                // it owns the cursor and schedules the next sibling.
                var next = ++tree.SequentialCursor[parent];

                if (next < count)
                {
                    ScheduleMeasure(tree, tree.ChildrenFlat[start + next],
                        algorithm.GetChildAvailableSize(
                            next,
                            tree.NodeConstrainedSize[parent],
                            tree.ChildMeasuredSizes.AsSpan(start, next)));
                    return;
                }
            }

            else if (Interlocked.Decrement(ref tree.PendingChildren.GetRef(parent)) != 0)
            {
                return;
            }

            // Last child measured: combine and finish the parent, then cascade.
            var measured = algorithm.CombineChildSizes(
                tree.NodeConstrainedSize[parent],
                tree.ChildMeasuredSizes.AsSpan(start, count));

            FinalizeDesiredSize(tree, parent, tree.NodeAvailableSize[parent], measured);

            node = parent;
        }
    }

    /// <summary>
    /// Measures a whole subtree inline, recursively, on the current thread. Used below the
    /// parallelism threshold, where scheduling separate work items isn't worth it.
    /// </summary>
    private Size MeasureNode(LayoutTreeSnapshot tree, int node, Size availableSize)
    {
        ref readonly var record = ref tree.Nodes.GetRef(node);

        if (!record.IsVisible)
        {
            // Like the classic MeasureCore, an invisible control measures to an empty size but
            // still records the pass, so it can be skipped next frame.
            tree.DesiredSize[node] = default;
            tree.NodeAvailableSize[node] = availableSize;
            tree.Measured[node] = true;
            return default;
        }

        // Classic engine guard (Layoutable.Measure): see ProcessMeasureItem.
        if (record.PreviousMeasureSize == availableSize)
            return tree.DesiredSize[node];

        tree.NodeAvailableSize[node] = availableSize;

        var constrainedSize = ComputeConstrainedSize(tree, node, availableSize);
        var algorithm = tree.Algorithms[node];
        var childCount = tree.ChildrenCount[node];
        Size measured;

        if (childCount == 0)
        {
            measured = algorithm.MeasureContent(constrainedSize);
        }
        else
        {
            var start = tree.ChildrenStart[node];

            if (algorithm.MeasureDependency == LayoutChildrenDependency.Sequential)
            {
                for (var i = 0; i < childCount; i++)
                {
                    var childAvailableSize = algorithm.GetChildAvailableSize(
                        i, constrainedSize, tree.ChildMeasuredSizes.AsSpan(start, i));

                    tree.ChildMeasuredSizes[start + i] =
                        MeasureNode(tree, tree.ChildrenFlat[start + i], childAvailableSize);
                }
            }
            else
            {
                for (var i = 0; i < childCount; i++)
                {
                    var childAvailableSize = algorithm.GetChildAvailableSize(
                        i, constrainedSize, ReadOnlySpan<Size>.Empty);

                    tree.ChildMeasuredSizes[start + i] =
                        MeasureNode(tree, tree.ChildrenFlat[start + i], childAvailableSize);
                }
            }

            measured = algorithm.CombineChildSizes(
                constrainedSize, tree.ChildMeasuredSizes.AsSpan(start, childCount));
        }

        return FinalizeDesiredSize(tree, node, availableSize, measured);
    }

    /// <summary>
    /// The measure chrome preceding the algorithm, mirroring the classic MeasureCore:
    /// margins and min/max constraints.
    /// </summary>
    private static Size ComputeConstrainedSize(LayoutTreeSnapshot tree, int node, Size availableSize)
    {
        ref readonly var inputs = ref tree.Algorithms[node].Inputs;
        var margin = inputs.Margin;

        if (inputs.UseLayoutRounding)
            margin = LayoutHelper.RoundLayoutThickness(margin, tree.Scale);

        return LayoutHelper.ApplyLayoutConstraints(inputs.MinMax, availableSize.Deflate(margin));
    }

    /// <summary>
    /// The measure chrome following the algorithm, mirroring the classic MeasureCore: clamp,
    /// round, add the margins back and cap to the available size. Stores the desired size.
    /// </summary>
    private static Size FinalizeDesiredSize(LayoutTreeSnapshot tree, int node, Size availableSize, Size measured)
    {
        ref readonly var inputs = ref tree.Algorithms[node].Inputs;
        var scale = tree.Scale;
        var margin = inputs.Margin;
        var useLayoutRounding = inputs.UseLayoutRounding;

        if (useLayoutRounding)
            margin = LayoutHelper.RoundLayoutThickness(margin, scale);

        var minMax = inputs.MinMax;
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
        tree.Measured[node] = true;
        return desiredSize;
    }

    /// <summary>
    /// Stage 5: computes the bounds of every node from the slot rect assigned by its parent.
    /// Same purity invariant as the measure stage. A node's slot is known before its children
    /// are scheduled, so the arrange wavefront has no join at all.
    /// </summary>
    private void ArrangeStage(LayoutTreeSnapshot tree, Rect rect)
    {
        const int root = LayoutTreeSnapshot.RootIndex;

        if (!tree.Nodes.GetRef(root).IsVisible)
            return;

        if (ShouldProcessInline(tree, root))
        {
            ArrangeNode(tree, root, rect);
        }
        else
        {
            tree.NodeSlot[root] = rect;
            LayoutWorkerPool.Instance.Execute(new ArrangeProcessor(this, tree), root);
        }
    }

    private sealed class ArrangeProcessor : ILayoutWorkProcessor
    {
        private readonly LayoutPipeline _pipeline;
        private readonly LayoutTreeSnapshot _tree;

        public ArrangeProcessor(LayoutPipeline pipeline, LayoutTreeSnapshot tree)
        {
            _pipeline = pipeline;
            _tree = tree;
        }

        public void Process(int item) => _pipeline.ProcessArrangeItem(_tree, item);
    }

    private void ProcessArrangeItem(LayoutTreeSnapshot tree, int node)
    {
        var slot = tree.NodeSlot[node];

        // Classic engine guard (Layoutable.Arrange): an arrange-valid subtree receiving the
        // same rect keeps its bounds and is pruned. Nothing below was re-measured either: the
        // node itself wasn't (a re-measured node can't be arrange-skipped), and a skipped
        // measure prunes its whole subtree.
        if (!tree.Measured[node])
        {
            ref var record = ref tree.Nodes.GetRef(node);
            if (record.IsArrangeValid && record.PreviousArrangeRect == slot)
                return;
        }

        if (ShouldProcessInline(tree, node))
        {
            ArrangeNode(tree, node, slot);
            return;
        }

        ArrangeNodeCore(tree, node, slot);

        var start = tree.ChildrenStart[node];
        var count = tree.ChildrenCount[node];

        for (var i = 0; i < count; i++)
        {
            var child = tree.ChildrenFlat[start + i];
            tree.NodeSlot[child] = tree.ChildSlots[start + i];
            LayoutWorkerPool.Instance.Enqueue(child);
        }
    }

    /// <summary>
    /// Arranges a whole subtree inline, recursively, on the current thread.
    /// </summary>
    private void ArrangeNode(LayoutTreeSnapshot tree, int node, Rect finalRect)
    {
        ref readonly var record = ref tree.Nodes.GetRef(node);

        if (!record.IsVisible)
            return;

        // Classic engine guard (Layoutable.Arrange): see ProcessArrangeItem.
        if (!tree.Measured[node] && record.IsArrangeValid && record.PreviousArrangeRect == finalRect)
            return;

        ArrangeNodeCore(tree, node, finalRect);

        var start = tree.ChildrenStart[node];
        var count = tree.ChildrenCount[node];

        for (var i = 0; i < count; i++)
            ArrangeNode(tree, tree.ChildrenFlat[start + i], tree.ChildSlots[start + i]);
    }

    /// <summary>
    /// The per-node arrange work, mirroring the classic ArrangeCore: margins, alignment,
    /// min/max and rounding, plus the children slot computation through the algorithm.
    /// Stores the node's bounds; doesn't touch the children.
    /// </summary>
    private void ArrangeNodeCore(LayoutTreeSnapshot tree, int node, Rect finalRect)
    {
        ref readonly var inputs = ref tree.Algorithms[node].Inputs;
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

        var childCount = tree.ChildrenCount[node];

        if (childCount > 0)
        {
            var start = tree.ChildrenStart[node];

            tree.Algorithms[node].ArrangeChildren(
                size,
                desiredSize,
                tree.ChildMeasuredSizes.AsSpan(start, childCount),
                tree.ChildSlots.AsSpan(start, childCount));
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
        tree.NodeSlot[node] = finalRect;
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
            var measured = tree.Measured[node];
            var arranged = tree.Arranged[node];

            // Classic parity: nodes skipped by the validity guards (and their unvisited
            // subtrees) already carry correct published state and aren't touched.
            if (!measured && !arranged)
                continue;

            var control = tree.Controls[node];
            Size? previousMeasure = measured ? tree.NodeAvailableSize[node] : null;

            if (arranged)
            {
                var bounds = tree.Bounds[node];
                control.PublishPipelineLayout(tree.DesiredSize[node], previousMeasure, bounds, tree.NodeSlot[node]);
                tree.Algorithms[node].OnPublish(control, bounds.Size);
            }
            else
            {
                control.PublishPipelineLayout(tree.DesiredSize[node], previousMeasure, null, null);
            }
        }
    }
}
