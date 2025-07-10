using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace Avalonia.Input.Platform;

// TODO12: remove
/// <summary>
/// Wraps a legacy <see cref="IDataObject"/> into a <see cref="IDataTransfer3"/>.
/// </summary>
internal sealed class DataObjectToDataTransferWrapper(IDataObject dataObject)
    : IDataTransfer3, IDataTransferItem
{
    public IDataObject DataObject { get; } = dataObject;

    IEnumerable<IDataTransferItem> IDataTransfer3.GetItems()
        => [this];

    public IEnumerable<DataFormat> GetFormats()
        => DataObject.GetDataFormats().Select(DataFormat.Parse);

    public bool Contains(DataFormat format)
        => DataObject.Contains(format.SystemName);

    public object? TryGet(DataFormat format)
    {
        var formatString = format.SystemName;
        return DataObject.Contains(formatString) ? DataObject.Get(formatString) : null;
    }

    public Task<object?> TryGetAsync(DataFormat format)
        => Task.FromResult(TryGet(format));

    [SuppressMessage("ReSharper", "SuspiciousTypeConversion.Global", Justification = "IDataObject may be implemented externally.")]
    public void Dispose()
        => (DataObject as IDisposable)?.Dispose();
}
