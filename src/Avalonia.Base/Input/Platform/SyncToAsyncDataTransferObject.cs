namespace Avalonia.Input.Platform;

/// <summary>
/// Wraps a <see cref="IAsyncDataTransfer"/> into a <see cref="IDataTransfer"/>.
/// </summary>
/// <param name="asyncDataTransfer">The wrapped <see cref="IAsyncDataTransfer"/> instance.</param>
/// <remarks>
/// Use with caution, as this may cause deadlocks!
/// Unfortunately, some clipboard system implementations are synchronous and need this wrapper.
/// </remarks>
internal sealed class AsyncToSyncDataTransfer(IAsyncDataTransfer asyncDataTransfer) : IDataTransfer
{
    private readonly IAsyncDataTransfer _asyncDataTransfer = asyncDataTransfer;

    /// <inheritdoc />
    public DataFormat[] GetFormats()
        => _asyncDataTransfer.GetFormatsAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public bool Contains(DataFormat format)
        => _asyncDataTransfer.ContainsAsync(format).GetAwaiter().GetResult();

    /// <inheritdoc />
    public object? TryGet(DataFormat format)
        => _asyncDataTransfer.TryGetAsync(format).GetAwaiter().GetResult();
}
