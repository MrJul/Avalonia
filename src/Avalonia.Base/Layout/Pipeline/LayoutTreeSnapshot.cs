using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

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
internal sealed class LayoutTreeSnapshot
{
    public const int RootIndex = 0;

    // Tree structure and inputs: immutable once built.
    public readonly Layoutable[] Controls;
    public readonly LayoutAlgorithm[] Algorithms;
    public readonly LayoutNodeInputs[] Inputs;
    public readonly bool[] IsVisible;
    public readonly int[] ChildrenStart;
    public readonly int[] ChildrenCount;
    public readonly int[] ChildrenFlat;
    public readonly int[] SubtreeSize;
    public readonly double Scale;

    // Outputs: each measure/arrange task writes to indices no other task reads or writes.
    public readonly Size[] DesiredSize;
    public readonly Size[] ChildMeasuredSizes; // aligned with ChildrenFlat
    public readonly Rect[] ChildSlots; // aligned with ChildrenFlat
    public readonly Rect[] Bounds;
    public readonly bool[] Arranged;

    public int Count => Controls.Length;

    private LayoutTreeSnapshot(
        Layoutable[] controls,
        LayoutAlgorithm[] algorithms,
        LayoutNodeInputs[] inputs,
        bool[] isVisible,
        int[] childrenStart,
        int[] childrenCount,
        int[] childrenFlat,
        int[] subtreeSize,
        double scale)
    {
        Controls = controls;
        Algorithms = algorithms;
        Inputs = inputs;
        IsVisible = isVisible;
        ChildrenStart = childrenStart;
        ChildrenCount = childrenCount;
        ChildrenFlat = childrenFlat;
        SubtreeSize = subtreeSize;
        Scale = scale;

        DesiredSize = new Size[controls.Length];
        ChildMeasuredSizes = new Size[childrenFlat.Length];
        ChildSlots = new Rect[childrenFlat.Length];
        Bounds = new Rect[controls.Length];
        Arranged = new bool[controls.Length];
    }

    /// <summary>
    /// Builds a snapshot of the subtree rooted at <paramref name="root"/>, including only the
    /// controls providing a <see cref="LayoutAlgorithm"/>. Children that don't opt in are
    /// excluded along with their whole subtree: they won't be measured, arranged or rendered.
    /// Invisible children are excluded too, matching the classic engine where they always
    /// measure to an empty size and containers skip them (e.g. for spacing) — this way
    /// algorithms only ever see visible children. Returns null if the root itself doesn't opt in.
    /// </summary>
    public static LayoutTreeSnapshot? TryBuild(Layoutable root, double scale)
    {
        if (root.GetLayoutAlgorithm() is not { } rootAlgorithm)
            return null;

        var controls = new List<Layoutable>();
        var algorithms = new List<LayoutAlgorithm>();
        var inputs = new List<LayoutNodeInputs>();
        var isVisible = new List<bool>();
        var childrenStart = new List<int>();
        var childrenCount = new List<int>();
        var childrenFlat = new List<int>();

        AddNode(root, rootAlgorithm);

        // Breadth-first: each dequeued node appends its opted-in children contiguously.
        for (var node = 0; node < controls.Count; node++)
        {
            childrenStart.Add(childrenFlat.Count);

            var visualChildren = controls[node].VisualChildren;
            var count = 0;

            for (var i = 0; i < visualChildren.Count; i++)
            {
                if (TryGetSnapshotChild(visualChildren[i], out var layoutable, out var algorithm))
                {
                    childrenFlat.Add(AddNode(layoutable, algorithm));
                    count++;
                }
            }

            childrenCount.Add(count);
        }

        // Children always have larger indices than their parent, so a reverse scan accumulates
        // subtree sizes (used by the pipeline to decide whether forking is worth it).
        var subtreeSize = new int[controls.Count];

        for (var node = controls.Count - 1; node >= 0; node--)
        {
            var size = 1;
            var start = childrenStart[node];
            var end = start + childrenCount[node];

            for (var i = start; i < end; i++)
                size += subtreeSize[childrenFlat[i]];

            subtreeSize[node] = size;
        }

        return new LayoutTreeSnapshot(
            controls.ToArray(),
            algorithms.ToArray(),
            inputs.ToArray(),
            isVisible.ToArray(),
            childrenStart.ToArray(),
            childrenCount.ToArray(),
            childrenFlat.ToArray(),
            subtreeSize,
            scale);

        int AddNode(Layoutable control, LayoutAlgorithm algorithm)
        {
            var index = controls.Count;
            controls.Add(control);
            algorithms.Add(algorithm);
            inputs.Add(new LayoutNodeInputs(control));
            isVisible.Add(control.IsVisible);
            return index;
        }
    }

    /// <summary>
    /// The single definition of which visual children participate in a snapshot: visible
    /// layoutables providing a layout algorithm. Containers whose algorithm depends on child
    /// indices (e.g. VisualLayerManager) use this to map controls to snapshot indices.
    /// </summary>
    public static bool TryGetSnapshotChild(
        Visual visual,
        [NotNullWhen(true)] out Layoutable? layoutable,
        [NotNullWhen(true)] out LayoutAlgorithm? algorithm)
    {
        if (visual is Layoutable { IsVisible: true } l && l.GetLayoutAlgorithm() is { } a)
        {
            layoutable = l;
            algorithm = a;
            return true;
        }

        layoutable = null;
        algorithm = null;
        return false;
    }
}
