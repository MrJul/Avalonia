using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Avalonia.Layout.Pipeline;

internal sealed class LayoutTreeSnapshotBuilder
{
    private ArrayBuilder<Layoutable> _controls = new();
    private ArrayBuilder<LayoutAlgorithm> _algorithms = new();
    private ArrayBuilder<bool> _isVisible = new();
    private ArrayBuilder<int> _parent = new();
    private ArrayBuilder<int> _indexInParent = new();
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
        AddNode(root, rootAlgorithm, -1, -1);

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
                if (control.GetLayoutChild(i) is not { } child)
                    continue;

                // Styling may itself change the visibility, apply before checking for IsVisible
                child.ApplyStyling();

                if (child.IsVisible)
                {
                    child.ApplyTemplate();

                    if (child.LayoutAlgorithm is { } algorithm)
                    {
                        var flatIndex = _childrenFlat.Count;
                        _childrenFlat.Add(AddNode(child, algorithm, node, flatIndex));
                        count++;
                    }
                }
            }

            _childrenCount.Add(count);
        }

        // Children always have larger indices than their parent, so a reverse scan accumulates
        // subtree sizes (used by the pipeline to decide whether forking is worth it).
        var subtreeSizeArray = ArrayPool<int>.Shared.Rent(_controls.Count);
        var subtreeSize = new ArraySegment<int>(subtreeSizeArray, 0, _controls.Count);

        for (var node = _controls.Count - 1; node >= 0; node--)
        {
            var size = 1;
            var start = _childrenStart[node];
            var end = start + _childrenCount[node];

            for (var i = start; i < end; i++)
                size += subtreeSize[_childrenFlat[i]];

            subtreeSize[node] = size;
        }

        return new LayoutTreeSnapshot(
            _controls.GetAndClear(),
            _algorithms.GetAndClear(),
            _isVisible.GetAndClear(),
            _parent.GetAndClear(),
            _indexInParent.GetAndClear(),
            _childrenStart.GetAndClear(),
            _childrenCount.GetAndClear(),
            _childrenFlat.GetAndClear(),
            subtreeSize,
            scale);

        int AddNode(Layoutable control, LayoutAlgorithm algorithm, int parentNode, int flatIndex)
        {
            var index = _controls.Count;
            _controls.Add(control);
            _algorithms.Add(algorithm);
            _isVisible.Add(control.IsVisible);
            _parent.Add(parentNode);
            _indexInParent.Add(flatIndex);
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
            get
            {
                Debug.Assert(index < _size);
                return _items[index];
            }
        }

        public void Add(T item)
        {
            var size = _size;
            if ((uint)size < (uint)_items.Length)
            {
                _size = size + 1;
                _items[size] = item;
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
