using System;

namespace Avalonia.Layout.Pipeline;

internal static class ArraySegmentExtensions
{
    extension<T>(ArraySegment<T> source)
    {
        public ref T GetRef(int index)
            => ref source.AsSpan()[index];
    }
}
