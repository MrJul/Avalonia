using System.Collections.Generic;
using System.Linq;

namespace Avalonia.Input.Platform;

/// <summary>
/// Wraps a <see cref="IDataTransferItem"/> into a legacy <see cref="IDataObject"/>.
/// </summary>
internal sealed class DataTransferToDataObjectWrapper(IDataTransfer3 dataTransfer) : IDataObject
{
    public IDataTransfer3 DataTransfer { get; } = dataTransfer;

    public IEnumerable<string> GetDataFormats()
        => DataTransfer.GetFormats().Select(format => format.SystemName);

    public bool Contains(string dataFormat)
        => DataTransfer.Contains(DataFormat.Parse(dataFormat));

    public object? Get(string dataFormat)
        => DataTransfer.TryGetAsync<object?>(DataFormat.Parse(dataFormat)).GetAwaiter().GetResult();
}
