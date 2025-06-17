using System.Linq;
using System.Threading.Tasks;

namespace Avalonia.Input.Platform;

// TODO12: remove
/// <summary>
/// Wraps a legacy <see cref="IDataObject"/> into a <see cref="IDataTransfer"/>.
/// </summary>
internal sealed class DataObjectToDataTransferWrapper(IDataObject dataObject) : IAsyncDataTransfer, IDataTransfer
{
    public IDataObject DataObject { get; } = dataObject;

    public DataFormat[] GetFormats()
        => DataObject.GetDataFormats().Select(DataFormat.Parse).ToArray();

    public Task<DataFormat[]> GetFormatsAsync()
        => Task.FromResult(GetFormats());

    public bool Contains(DataFormat format)
        => DataObject.Contains(format.SystemName);

    public Task<bool> ContainsAsync(DataFormat format)
        => Task.FromResult(Contains(format));

    public object? TryGet(DataFormat format)
    {
        var formatString = format.SystemName;
        return DataObject.Contains(formatString) ? DataObject.Get(formatString) : null;
    }

    public Task<object?> TryGetAsync(DataFormat format)
        => Task.FromResult(TryGet(format));
}
