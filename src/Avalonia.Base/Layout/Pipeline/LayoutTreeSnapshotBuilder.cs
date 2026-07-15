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

    private ArrayBuilder<Layoutable> _controls = new();
    private ArrayBuilder<LayoutAlgorithm> _algorithms = new();
    private ArrayBuilder<LayoutNodeRecord> _nodes = new();
    private ArrayBuilder<int> _childrenStart = new();
    private ArrayBuilder<int> _childrenCount = new();
    private ArrayBuilder<int> _childrenFlat = new();

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
        AddNode(root, rootAlgorithm, -1, -1, true);

        // Breadth-first: each dequeued node appends its opted-in children contiguously.
        for (var node = 0; node < _controls.Count; node++)
        {
            _childrenStart.Add(_childrenFlat.Count);

            // Enumerate the exact collection the classic implementation lays out (e.g.
            // Panel.Children, Decorator.Child), which defaults to the visual children.
            var control = _controls[node];
            var layoutChildrenCount = control.GetLayoutChildrenCount();
            var count = 0;

            for (var i = 0; i < layoutChildrenCount; i++)
            {
                if (control.GetLayoutChild(i) is not { LayoutAlgorithm: { } algorithm } child)
                    continue;

                // Styling may itself change the visibility, apply before checking for IsVisible
                child.ApplyStyling();

                var isVisible = child.IsVisible;

                if (isVisible)
                    child.ApplyTemplate();

                var flatIndex = _childrenFlat.Count;
                _childrenFlat.Add(AddNode(child, algorithm, node, flatIndex, isVisible));
                count++;
            }

            _childrenCount.Add(count);
        }

        // Children always have larger indices than their parent, so a reverse scan accumulates
        // subtree sizes (used by the pipeline to decide whether forking is worth it) and
        // propagates the validity guard sentinels upwards: a node keeps its previous
        // measure/arrange value only when its whole subtree is valid, so the guard checked by
        // the measure and arrange stages is a single comparison per node.
        var subtreeSizeArray = ArrayPool<int>.Shared.Rent(_controls.Count);
        var subtreeSize = new ArraySegment<int>(subtreeSizeArray, 0, _controls.Count);

        for (var node = _controls.Count - 1; node >= 0; node--)
        {
            var size = 1;
            var start = _childrenStart[node];
            var end = start + _childrenCount[node];
            ref var record = ref _nodes.GetRef(node);
            var isSubtreeMeasureValid = record.IsMeasureValid;
            var isSubtreeArrangeValid = record.IsArrangeValid;

            for (var i = start; i < end; i++)
            {
                var child = _childrenFlat[i];
                size += subtreeSize[child];

                ref readonly var childRecord = ref _nodes.GetRef(child);
                isSubtreeMeasureValid &= childRecord.IsMeasureValid;
                isSubtreeArrangeValid &= childRecord.IsArrangeValid;
            }

            subtreeSize[node] = size;

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

        var snapshot = new LayoutTreeSnapshot(
            _controls.GetAndClear(),
            _algorithms.GetAndClear(),
            _nodes.GetAndClear(),
            _childrenStart.GetAndClear(),
            _childrenCount.GetAndClear(),
            _childrenFlat.GetAndClear(),
            subtreeSize,
            scale);

        snapshot.PrefillPreviousDesiredSizes();
        return snapshot;

        int AddNode(Layoutable control, LayoutAlgorithm algorithm, int parentNode, int flatIndex, bool isVisible)
        {
            var index = _controls.Count;
            _controls.Add(control);
            _algorithms.Add(algorithm);

            _nodes.Add(new LayoutNodeRecord
            {
                IsMeasureValid = control.IsMeasureValid,
                IsArrangeValid = control.IsArrangeValid,
                PreviousMeasureSize = control.PreviousMeasure.GetValueOrDefault(s_invalidSize),
                PreviousArrangeRect = control.PreviousArrange.GetValueOrDefault(s_invalidRect),
                Parent = parentNode,
                IndexInParent = flatIndex,
                IsVisible = isVisible
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
