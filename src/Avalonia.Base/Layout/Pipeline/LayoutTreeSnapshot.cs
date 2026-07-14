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
internal readonly struct LayoutNodeInputs
{
    public readonly Thickness Margin;
    public readonly HorizontalAlignment HorizontalAlignment;
    public readonly VerticalAlignment VerticalAlignment;
    public readonly bool UseLayoutRounding;
    public readonly MinMax MinMax;

    public LayoutNodeInputs(Layoutable control)
    {
        Margin = control.Margin;
        HorizontalAlignment = control.HorizontalAlignment;
        VerticalAlignment = control.VerticalAlignment;
        UseLayoutRounding = control.UseLayoutRounding;
        MinMax = new MinMax(control);
    }
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
/// children of any node form a contiguous range both in the node arrays (their indices are
/// consecutive) and in <see cref="ChildrenFlat"/>.
/// </remarks>
internal sealed class LayoutTreeSnapshot(
    ArraySegment<Layoutable> controls,
    ArraySegment<LayoutAlgorithm> algorithms,
    ArraySegment<LayoutNodeInputs> inputs,
    ArraySegment<bool> isVisible,
    ArraySegment<int> parent,
    ArraySegment<int> indexInParent,
    ArraySegment<int> childrenStart,
    ArraySegment<int> childrenCount,
    ArraySegment<int> childrenFlat,
    ArraySegment<int> subtreeSize,
    double scale)
    : IDisposable
{
    public const int RootIndex = 0;

    // Tree structure and inputs: immutable once built.
    public ArraySegment<Layoutable> Controls = controls;
    public ArraySegment<LayoutAlgorithm> Algorithms = algorithms;
    public ArraySegment<LayoutNodeInputs> Inputs = inputs;
    public ArraySegment<bool> IsVisible = isVisible;
    public ArraySegment<int> Parent = parent; // -1 for the root
    public ArraySegment<int> IndexInParent = indexInParent; // index into ChildrenFlat, -1 for the root
    public ArraySegment<int> ChildrenStart = childrenStart;
    public ArraySegment<int> ChildrenCount = childrenCount;
    public ArraySegment<int> ChildrenFlat = childrenFlat;
    public ArraySegment<int> SubtreeSize = subtreeSize;
    public readonly double Scale = scale;

    // Outputs: each measure/arrange task writes to indices no other task reads or writes.
    public ArraySegment<Size> DesiredSize = Rent<Size>(controls.Count);
    public ArraySegment<Size> ChildMeasuredSizes = Rent<Size>(childrenFlat.Count); // aligned with ChildrenFlat
    public ArraySegment<Rect> ChildSlots = Rent<Rect>(childrenFlat.Count); // aligned with ChildrenFlat
    public ArraySegment<Rect> Bounds = Rent<Rect>(controls.Count);
    public ArraySegment<bool> Arranged = Rent<bool>(controls.Count);

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

    private static ArraySegment<T> Rent<T>(int count)
    {
        var array = ArrayPool<T>.Shared.Rent(count);
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
        Return(ref Inputs);
        Return(ref IsVisible);
        Return(ref Parent);
        Return(ref IndexInParent);
        Return(ref ChildrenStart);
        Return(ref ChildrenCount);
        Return(ref ChildrenFlat);
        Return(ref SubtreeSize);

        Return(ref DesiredSize);
        Return(ref ChildMeasuredSizes);
        Return(ref ChildSlots);
        Return(ref Bounds);
        Return(ref Arranged);

        Return(ref NodeAvailableSize);
        Return(ref NodeConstrainedSize);
        Return(ref NodeSlot);
        Return(ref PendingChildren);
        Return(ref SequentialCursor);
    }
}
