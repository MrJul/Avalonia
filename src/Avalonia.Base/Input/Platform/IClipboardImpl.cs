using System.Threading.Tasks;
using Avalonia.Metadata;

namespace Avalonia.Input.Platform;

[PrivateApi]
public interface IClipboardImpl
{
    Task<DataFormat[]> GetFormatsAsync();
    Task<object?> TryGetDataAsync(DataFormat format);
    Task SetDataTransferAsync(IAsyncDataTransfer dataTransfer);
    Task ClearAsync();
}
