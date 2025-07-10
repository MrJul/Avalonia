using System;
using System.Collections.Generic;

namespace Avalonia.Input.Platform;

/// <summary>
/// Represents an object providing a list of <see cref="IDataTransferItem"/> usable by the clipboard
/// or during a drag and drop operation.
/// </summary>
/// <seealso cref="DataTransfer"/>
/// <remarks>
/// <list type="bullet">
/// <item>
/// When an implementation of this interface is put into the clipboard using <see cref="IClipboard.SetDataAsync"/>,
/// it must NOT be disposed by the caller. The system will dispose of it automatically when it becomes unused.
/// </item>
/// <item>
/// When an implementation of this interface is used as a drag source using <see cref="DragDrop.DoDragDropAsync"/>,
/// it must NOT be disposed by the caller. The system will dispose of it automatically when the drag operation completes.
/// </item>
/// <item>
/// When an implementation of this interface is returned from the clipboard via <see cref="IClipboard.TryGetDataAsync"/>,
/// it MUST be disposed the caller.
/// </item>
/// </list>
/// </remarks>
public interface IDataTransfer3 : IDisposable
{
    /// <summary>
    /// Gets the list of <see cref="IDataTransferItem"/> contained in this object.
    /// </summary>
    /// <returns>A list of items.</returns>
    /// <remarks>
    /// Windows and X11 only support a single data item. If several items are specified for these platforms, they will
    /// be merged into a single one. If a format is supported by multiple items when that happens, the value from the
    /// first item providing the given format will be used.
    /// </remarks>
    IEnumerable<IDataTransferItem> GetItems();
}
