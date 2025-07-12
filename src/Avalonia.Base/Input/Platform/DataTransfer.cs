using System;
using System.Collections.Generic;

namespace Avalonia.Input.Platform;

/// <summary>
/// A mutable implementation of <see cref="IDataTransfer"/>.
/// </summary>
public sealed class DataTransfer : IDataTransfer
{
    /// <summary>
    /// Gets the list of <see cref="IDataTransferItem"/> contained in this object.
    /// </summary>
    public List<IDataTransferItem> Items { get; } = [];

    IEnumerable<IDataTransferItem> IDataTransfer.GetItems()
        => Items;

    void IDisposable.Dispose()
    {
    }
}
