#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Native.Interop;

namespace Avalonia.Native;

internal sealed class ClipboardDataTransfer(IAvnClipboard clipboard)
    : IDataTransfer3, IDataTransferItem
{
    private ClipboardImpl? _clipboard = new(clipboard);
    private DataFormat[]? _formats;

    private ClipboardImpl Clipboard
        => _clipboard ?? throw new ObjectDisposedException(nameof(ClipboardDataTransfer));

    private DataFormat[] Formats
        => _formats ??= Clipboard.GetFormats();

    IEnumerable<IDataTransferItem> IDataTransfer3.GetItems()
        => [this];

    public IEnumerable<DataFormat> GetFormats()
        => Formats;

    public bool Contains(DataFormat format)
        => Array.IndexOf(Formats, format) >= 0;

    public object? TryGet(DataFormat format)
        => Clipboard.TryGetData(format);

    Task<object?> IDataTransferItem.TryGetAsync(DataFormat format)
        => Task.FromResult(TryGet(format));

    public void Dispose()
    {
        _clipboard?.Dispose();
        _clipboard = null;
    }
}
