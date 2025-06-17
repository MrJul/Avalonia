#nullable enable

using System;
using Avalonia.Input.Platform;
using Avalonia.Native.Interop;

namespace Avalonia.Native;

internal sealed class ClipboardDataTransfer(IAvnClipboard clipboard) : IDataTransfer, IDisposable
{
    private ClipboardImpl? _clipboard = new(clipboard);
    private DataFormat[]? _formats;

    private ClipboardImpl Clipboard
        => _clipboard ?? throw new ObjectDisposedException(nameof(ClipboardDataTransfer));

    private DataFormat[] Formats
        => _formats ??= Clipboard.GetFormats();

    public DataFormat[] GetFormats()
        => Formats;

    public bool Contains(DataFormat format)
        => Array.IndexOf(Formats, format) >= 0;

    public object? TryGet(DataFormat format)
        => Clipboard.TryGetData(format);

    public void Dispose()
    {
        _clipboard?.Dispose();
        _clipboard = null;
    }
}
