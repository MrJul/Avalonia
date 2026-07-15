using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Avalonia.Layout.Pipeline;

/// <summary>
/// The property values a node needs during the measure and arrange stages, captured on the
/// UI thread by the snapshot stage so that the parallel stages never touch the property system.
/// </summary>
[UnconditionalSuppressMessage("Performance", "CA1815:Override equals and operator equals on value types")]
public readonly struct LayoutNodeInputs(
    Thickness margin,
    HorizontalAlignment horizontalAlignment,
    VerticalAlignment verticalAlignment,
    bool useLayoutRounding,
    double layoutScale,
    MinMax minMax)
{
    public readonly Thickness Margin = margin;
    public readonly HorizontalAlignment HorizontalAlignment = horizontalAlignment;
    public readonly VerticalAlignment VerticalAlignment = verticalAlignment;
    public readonly bool UseLayoutRounding = useLayoutRounding;
    public readonly double LayoutScale = layoutScale;
    public readonly MinMax MinMax = minMax;

    public static LayoutNodeInputs FromLayoutable(Layoutable layoutable)
        => new(
            layoutable.Margin,
            layoutable.HorizontalAlignment,
            layoutable.VerticalAlignment,
            layoutable.UseLayoutRounding,
            LayoutHelper.GetLayoutScale(layoutable),
            new MinMax(layoutable));
}

/// <summary>
/// The per-node data written once when the snapshot builder adds a node — grouped into a
/// single struct so that adding a node performs one sequential record write instead of
/// scattering across parallel arrays, and ordered to fit one cache line.
/// </summary>
internal struct LayoutNodeRecord
{
    /// <summary>
    /// Classic arrange guard: the rect of the previous arrange pass when the node and its
    /// whole subtree are still arrange-valid, or NaN — which never compares equal — otherwise.
    /// </summary>
    public Rect PreviousArrangeRect;

    /// <summary>
    /// Classic measure guard: the constraint of the previous measure pass when the node and
    /// its whole subtree are still measure-valid, or NaN — which never compares equal —
    /// otherwise.
    /// </summary>
    public Size PreviousMeasureSize;

    /// <summary>The parent node index, -1 for the root.</summary>
    public int Parent;

    public bool IsMeasureValid;

    public bool IsArrangeValid;
}

/// <summary>
/// The children and subtree data of a node, written by the snapshot builder — grouped into a
/// single struct because the three fields are read together on every hot path: the work item
/// expansion, the combines, the arrange stage and the parallelism threshold check.
/// </summary>
internal struct LayoutNodeChildren
{
    /// <summary>
    /// The node index of the first child: thanks to the breadth-first construction, the
    /// children are the contiguous node range [FirstChild, FirstChild + Count).
    /// </summary>
    public int FirstChild;

    /// <summary>The number of children.</summary>
    public int Count;

    /// <summary>The number of nodes in the subtree, including the node itself.</summary>
    public int SubtreeSize;
}

/// <summary>
/// The output of the snapshot stage of the <see cref="LayoutPipeline"/>: an immutable,
/// index-based copy of the opted-in part of a visual tree, stored as structure-of-arrays.
/// </summary>
/// <remarks>
/// After the snapshot is built, the measure and arrange stages only ever read the structural
/// arrays and write to disjoint indices of the output arrays, which is what makes them safe to
/// run on multiple threads without synchronization. No live control is touched until the
/// publish stage.
/// Nodes are indexed in breadth-first order (the root is <see cref="RootIndex"/>), so the
/// children of any node are the contiguous node range starting at
/// <see cref="LayoutNodeChildren.FirstChild"/>.
/// The builder asserts that invariant; if a future change breaks it (e.g. incremental
/// patching of a persistent snapshot), an explicit child index mapping must come back.
/// </remarks>
internal sealed class LayoutTreeSnapshot(
    ArraySegment<Layoutable> controls,
    ArraySegment<LayoutAlgorithm> algorithms,
    ArraySegment<LayoutNodeRecord> nodes,
    ArraySegment<bool> isVisible,
    ArraySegment<LayoutNodeChildren> children,
    ArraySegment<Size> desiredSize,
    double scale)
    : IDisposable
{
    public const int RootIndex = 0;

    // Tree structure and inputs: immutable once built.
    public ArraySegment<Layoutable> Controls = controls;
    public ArraySegment<LayoutAlgorithm> Algorithms = algorithms;
    public ArraySegment<LayoutNodeRecord> Nodes = nodes;

    // Dense so that a per-parent slice can be handed to the layout algorithms, which may need
    // to treat invisible children specially like their classic implementations do.
    public ArraySegment<bool> IsVisible = isVisible;

    public ArraySegment<LayoutNodeChildren> Children = children;
    public readonly double Scale = scale;

    // Outputs: each measure/arrange task writes to indices no other task reads or writes.
    // DesiredSize arrives prefilled by the builder with the previously published values, so
    // that subtrees skipped by the classic validity guard — including their unvisited
    // descendants — expose correct sizes to parent combines, arrange and publish. Thanks to
    // the breadth-first contiguity, it doubles as every parent's child sizes span.
    public ArraySegment<Size> DesiredSize = desiredSize;
    public ArraySegment<Rect> Bounds = Rent<Rect>(controls.Count);
    public ArraySegment<bool> Measured = Rent<bool>(controls.Count, clear: true);
    public ArraySegment<bool> Arranged = Rent<bool>(controls.Count, clear: true);

    // Wavefront scheduling state, used when a stage runs on the LayoutWorkerPool: the size
    // made available to a node, its constrained size, the slot rect assigned by its parent,
    // and the completion tracking of its children (a countdown for independent containers,
    // a cursor for sequential ones).
    public ArraySegment<Size> NodeAvailableSize = Rent<Size>(controls.Count);
    public ArraySegment<Size> NodeConstrainedSize = Rent<Size>(controls.Count);
    public ArraySegment<Rect> NodeSlot = Rent<Rect>(controls.Count);
    public ArraySegment<int> PendingChildren = Rent<int>(controls.Count);
    public ArraySegment<int> SequentialCursor = Rent<int>(controls.Count);

    public int Count
        => Controls.Count;

    private static ArraySegment<T> Rent<T>(int count, bool clear = false)
    {
        var array = ArrayPool<T>.Shared.Rent(count);

        if (clear)
            Array.Clear(array, 0, count);

        return new ArraySegment<T>(array, 0, count);
    }

    private static void Return<T>(ref ArraySegment<T> arraySegment)
    {
        if (arraySegment.Array is null)
            return;

        ArrayPool<T>.Shared.Return(arraySegment.Array, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        arraySegment = default;
    }

    public void Dispose()
    {
        Return(ref Controls);
        Return(ref Algorithms);
        Return(ref Nodes);
        Return(ref IsVisible);
        Return(ref Children);

        Return(ref DesiredSize);
        Return(ref Bounds);
        Return(ref Measured);
        Return(ref Arranged);

        Return(ref NodeAvailableSize);
        Return(ref NodeConstrainedSize);
        Return(ref NodeSlot);
        Return(ref PendingChildren);
        Return(ref SequentialCursor);
    }
}
