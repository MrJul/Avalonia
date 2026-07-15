using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Avalonia.Layout.Pipeline;

internal sealed class LayoutTreeSnapshotBuilder
{
    // Sentinels for the classic validity guard captures: NaN never compares equal, so an
    // invalid (or never laid out) node can never be skipped.
    private static readonly Size s_invalidSize = new(double.NaN, double.NaN);
    private static readonly Rect s_invalidRect = new(double.NaN, double.NaN, double.NaN, double.NaN);

    private ArrayBuilder<LayoutNodeRecord> _nodes = new();
    private ArrayBuilder<bool> _isVisible = new();
    private ArrayBuilder<LayoutNodeChildren> _children = new();
    private ArrayBuilder<Size> _desiredSize = new();

    /// <summary>
    /// Builds a snapshot of the subtree rooted at <paramref name="root"/> (assumed already
    /// prepared), running the prepare stage on each visited child in the same walk: a child is
    /// styled and templated right before its opt-in check and its input capture, so styled
    /// values are in effect when captured and template-created children are visited in turn.
    /// The snapshot includes only the controls providing a <see cref="LayoutAlgorithm"/>:
    /// children that don't opt in are excluded along with their whole subtree — they won't be
    /// measured, arranged or rendered. Invisible children are excluded (and left unprepared)
    /// too, matching the classic engine where they always measure to an empty size without
    /// being styled, and containers skip them (e.g. for spacing) — this way algorithms only
    /// ever see visible children.
    /// </summary>
    public LayoutTreeSnapshot Build(Layoutable root, LayoutAlgorithm rootAlgorithm, double scale)
    {
        AddNode(root, rootAlgorithm, -1, root.IsVisible);

        // Breadth-first: each dequeued node appends its opted-in children contiguously, so a
        // node's children are exactly the node range [FirstChild, FirstChild + Count) — no
        // child index mapping is needed as long as the tree is built this way.
        for (var node = 0; node < _nodes.Count; node++)
        {
            var firstChild = _nodes.Count;
            var count = 0;

            // An invisible node measures to an empty size without visiting its children, like
            // the classic MeasureCore: its subtree is pruned from the snapshot.
            if (_isVisible[node])
            {
                // Enumerate the exact collection the classic implementation lays out (e.g.
                // Panel.Children, Decorator.Child), which defaults to the visual children.
                var control = _nodes[node].Control;
                var layoutChildrenCount = control.GetLayoutChildrenCount();

                for (var i = 0; i < layoutChildrenCount; i++)
                {
                    if (control.GetLayoutChild(i) is not { LayoutAlgorithm: { } algorithm } child)
                        continue;

                    // Styling may itself change the visibility, apply before checking for IsVisible
                    child.ApplyStyling();

                    // Invisible children are included so that, like in the classic engine, they
                    // measure to an empty size and record the pass — but they aren't templated
                    // and their subtree isn't visited.
                    var isVisible = child.IsVisible;

                    if (isVisible)
                        child.ApplyTemplate();

                    var index = AddNode(child, algorithm, node, isVisible);
                    Debug.Assert(index == firstChild + count,
                        "Breadth-first construction must assign contiguous indices to a node's children.");
                    count++;
                }
            }

            // SubtreeSize is computed by the reverse pass below.
            _children.Add(new LayoutNodeChildren { FirstChild = firstChild, Count = count });
        }

        // Children always have larger indices than their parent, so a reverse scan accumulates
        // subtree sizes (used by the pipeline to decide whether forking is worth it) and
        // propagates the validity guard sentinels upwards: a node keeps its previous
        // measure/arrange value only when its whole subtree is valid, so the guard checked by
        // the measure and arrange stages is a single comparison per node.
        for (var node = _nodes.Count - 1; node >= 0; node--)
        {
            ref var children = ref _children.GetRef(node);
            var size = 1;
            var firstChild = children.FirstChild;
            var end = firstChild + children.Count;
            ref var record = ref _nodes.GetRef(node);
            var isSubtreeMeasureValid = record.IsMeasureValid;
            var isSubtreeArrangeValid = record.IsArrangeValid;

            for (var child = firstChild; child < end; child++)
            {
                size += _children.GetRef(child).SubtreeSize;

                ref readonly var childRecord = ref _nodes.GetRef(child);
                isSubtreeMeasureValid &= childRecord.IsMeasureValid;

                // An invisible child never records an arrange (classic containers skip
                // arranging it), so it must not prevent its ancestors from skipping.
                if (_isVisible[child])
                    isSubtreeArrangeValid &= childRecord.IsArrangeValid;
            }

            children.SubtreeSize = size;

            if (!isSubtreeMeasureValid)
            {
                record.IsMeasureValid = false;
                record.PreviousMeasureSize = s_invalidSize;
            }

            if (!isSubtreeArrangeValid)
            {
                record.IsArrangeValid = false;
                record.PreviousArrangeRect = s_invalidRect;
            }
        }

        return new LayoutTreeSnapshot(
            _nodes.GetAndClear(),
            _isVisible.GetAndClear(),
            _children.GetAndClear(),
            _desiredSize.GetAndClear(),
            scale);

        int AddNode(Layoutable control, LayoutAlgorithm algorithm, int parentNode, bool isVisible)
        {
            var index = _nodes.Count;
            _isVisible.Add(isVisible);

            // Prefill the snapshot's desired sizes with the previously published value, so that
            // subtrees skipped by the classic validity guard expose correct sizes to parent
            // combines, arrange and publish.
            _desiredSize.Add(control.DesiredSize);

            _nodes.Add(new LayoutNodeRecord
            {
                Control = control,
                Algorithm = algorithm,
                IsMeasureValid = control.IsMeasureValid,
                IsArrangeValid = control.IsArrangeValid,
                PreviousMeasureSize = control.PreviousMeasure.GetValueOrDefault(s_invalidSize),
                PreviousArrangeRect = control.PreviousArrange.GetValueOrDefault(s_invalidRect),
                Parent = parentNode
            });

            return index;
        }
    }

    private struct ArrayBuilder<T>
    {
        private const int DefaultCapacity = 4096;

        private T[] _items;
        private int _size;

        public ArrayBuilder()
        {
            _items = [];
            _size = 0;
        }

        public int Count
            => _size;

        public T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Debug.Assert(index < _size);
                return _items[index];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetRef(int index)
        {
            Debug.Assert(index < _size);
            return ref _items[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item)
        {
            var size = _size;
            var items = _items;
            if ((uint)size < (uint)items.Length)
            {
                _size = size + 1;
                items[size] = item;
            }
            else
                AddWithResize(item);

        }

        // Non-inline List.Add to improve its code quality as uncommon path
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AddWithResize(T item)
        {
            var size = _size;
            Grow(size + 1);
            _size = size + 1;
            _items[size] = item;
        }

        private void Grow(int min)
        {
            var newCapacity = _items.Length == 0 ? DefaultCapacity : _items.Length * 2;

            if ((uint)newCapacity > Array.MaxLength)
                newCapacity = Array.MaxLength;

            if (newCapacity < min)
                newCapacity = min;

            var newItems = ArrayPool<T>.Shared.Rent(newCapacity);
            if (_size > 0)
                Array.Copy(_items, newItems, _size);

            ArrayPool<T>.Shared.Return(_items, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            _items = newItems;
        }

        public ArraySegment<T> GetAndClear()
        {
            var result = new ArraySegment<T>(_items, 0, _size);
            _items = [];
            _size = 0;
            return result;
        }
    }
}
