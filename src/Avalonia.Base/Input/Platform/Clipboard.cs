using System.Linq;
using System.Threading.Tasks;

namespace Avalonia.Input.Platform;

/// <summary>
/// Implementation of <see cref="IClipboard"/>
/// </summary>
internal sealed class Clipboard(IClipboardImpl clipboardImpl) : IClipboard
{
    private readonly IClipboardImpl _clipboardImpl = clipboardImpl;
    private IAsyncDataTransfer? _lastDataObject;

    public async Task<string?> GetTextAsync()
    {
        var result = await _clipboardImpl.TryGetDataAsync(DataFormat.Text).ConfigureAwait(false);
        return result as string;
    }

    public Task SetTextAsync(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return ClearAsync();

        var dataObject = new DataTransfer();
        dataObject.Set(DataFormat.Text, text);
        return _clipboardImpl.SetDataTransferAsync(dataObject);
    }

    public Task ClearAsync()
    {
        _lastDataObject = null;
        return _clipboardImpl.ClearAsync();
    }

    Task IClipboard.SetDataObjectAsync(IDataObject data)
        => SetDataTransferAsync(new DataObjectToDataTransferWrapper(data));

    public Task SetDataTransferAsync(IAsyncDataTransfer? dataTransfer)
    {
        if (dataTransfer is null)
            return ClearAsync();

        if (_clipboardImpl is IOwnedClipboardImpl)
            _lastDataObject = dataTransfer;

        return _clipboardImpl.SetDataTransferAsync(dataTransfer);
    }

    public Task FlushAsync()
        => _clipboardImpl is IFlushableClipboardImpl flushable ? flushable.FlushAsync() : Task.CompletedTask;

    async Task<string[]> IClipboard.GetFormatsAsync()
    {
        var formats = await GetDataFormatsAsync().ConfigureAwait(false);
        return formats.Select(format => format.SystemName).ToArray();
    }

    public Task<DataFormat[]> GetDataFormatsAsync()
        => _clipboardImpl.GetFormatsAsync();

    Task<object?> IClipboard.GetDataAsync(string format)
        => TryGetDataAsync(DataFormat.Parse(format));

    public Task<object?> TryGetDataAsync(DataFormat format)
        => _clipboardImpl.TryGetDataAsync(format);

    async Task<IDataObject?> IClipboard.TryGetInProcessDataObjectAsync()
    {
        var dataObject = await TryGetInProcessDataTransferAsync().ConfigureAwait(false);
        return (dataObject as DataObjectToDataTransferWrapper)?.DataObject;
    }

    public async Task<IAsyncDataTransfer?> TryGetInProcessDataTransferAsync()
    {
        if (_lastDataObject is null || _clipboardImpl is not IOwnedClipboardImpl ownedClipboardImpl)
            return null;

        if (!await ownedClipboardImpl.IsCurrentOwnerAsync())
            _lastDataObject = null;

        return _lastDataObject;
    }
}
