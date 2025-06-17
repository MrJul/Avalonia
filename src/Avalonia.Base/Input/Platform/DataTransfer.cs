using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Avalonia.Input.Platform;

/// <summary>
/// Mutable implementation of <see cref="IDataTransfer"/> and <see cref="IAsyncDataTransfer"/>.
/// </summary>
/// <remarks>The <see cref="IAsyncDataTransfer"/> implementations in this class are synchronous.</remarks>
public sealed class DataTransfer : IDataTransfer, IAsyncDataTransfer
{
    private readonly Dictionary<DataFormat, object> _items = new();

    /// <inheritdoc />
    public DataFormat[] GetFormats()
        => _items.Keys.ToArray();

    /// <inheritdoc />
    public Task<DataFormat[]> GetFormatsAsync()
        => Task.FromResult(GetFormats());

    /// <inheritdoc />
    public bool Contains(DataFormat format)
        => _items.ContainsKey(format);

    /// <inheritdoc />
    public Task<bool> ContainsAsync(DataFormat format)
        => Task.FromResult(TryGet(format) is not null);

    /// <inheritdoc />
    public object? TryGet(DataFormat format)
    {
        _items.TryGetValue(format, out var item);
        return item;
    }

    /// <inheritdoc />
    public Task<object?> TryGetAsync(DataFormat format)
        => Task.FromResult(TryGet(format));

    public void Set(DataFormat format, object value)
        => _items[format] = value;
}
