using System.Threading.Tasks;

namespace Avalonia.Input.Platform;

public interface IAsyncDataTransfer
{
    Task<DataFormat[]> GetFormatsAsync();
    Task<bool> ContainsAsync(DataFormat format);
    Task<object?> TryGetAsync(DataFormat format);
}
