using System;
using System.Collections.Generic;

namespace Avalonia.Input.Platform;

/// <summary>
/// A mutable implementation of <see cref="IDataTransfer3"/>.
/// </summary>
public sealed class DataTransfer : IDataTransfer3
{
    /// <summary>
    /// Gets the list of <see cref="IDataTransferItem"/> contained in this object.
    /// </summary>
    public List<IDataTransferItem> Items { get; } = [];

    IEnumerable<IDataTransferItem> IDataTransfer3.GetItems()
        => Items;

    void IDisposable.Dispose()
    {
    }
}
