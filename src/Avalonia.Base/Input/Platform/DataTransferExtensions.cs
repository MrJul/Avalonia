using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Avalonia.Input.Platform;

/// <summary>
/// Contains extension methods for <see cref="IDataTransfer"/> and <see cref="IAsyncDataTransfer"/>.
/// </summary>
public static class DataTransferExtensions
{
    internal static IDataTransfer ToSynchronous(this IAsyncDataTransfer dataTransfer)
        => dataTransfer as IDataTransfer ?? new AsyncToSyncDataTransfer(dataTransfer);

    // TODO12: remove
    internal static IDataObject ToLegacyDataObject(this IDataTransfer dataTransfer)
        => (dataTransfer as DataObjectToDataTransferWrapper)?.DataObject ?? new DataTransferToDataObjectWrapper(dataTransfer);

    /// <summary>
    /// Returns a text, if available, from a <see cref="IDataTransfer"/> instance.
    /// </summary>
    /// <param name="dataTransfer">The data transfer instance.</param>
    /// <returns>A string, or null if the format isn't available.</returns>
    /// <seealso cref="DataFormat.Text"/>.
    public static string? TryGetText(this IDataTransfer dataTransfer)
        => dataTransfer.TryGet(DataFormat.Text) as string;

    /// <summary>
    /// Returns a text, if available, from a <see cref="IAsyncDataTransfer"/> instance.
    /// </summary>
    /// <param name="dataTransfer">The data transfer instance.</param>
    /// <returns>A string, or null if the format isn't available.</returns>
    /// <seealso cref="DataFormat.Text"/>.
    public static async Task<string?> TryGetTextAsync(this IAsyncDataTransfer dataTransfer)
        => await dataTransfer.TryGetAsync(DataFormat.Text).ConfigureAwait(false) as string;

    /// <summary>
    /// Returns a list of files, if available, from a <see cref="IDataTransfer"/> instance.
    /// </summary>
    /// <param name="dataTransfer">The data transfer instance.</param>
    /// <returns>An array of <see cref="IStorageItem"/> (files or folders), or null if the format isn't available.</returns>
    /// <seealso cref="DataFormat.Files"/>.
    public static IStorageItem[]? TryGetFiles(this IDataTransfer dataTransfer)
        => dataTransfer.TryGet(DataFormat.Files) as IStorageItem[];

    /// <summary>
    /// Returns a list of files, if available, from a <see cref="IAsyncDataTransfer"/> instance.
    /// </summary>
    /// <param name="dataTransfer">The data transfer instance.</param>
    /// <returns>An array of <see cref="IStorageItem"/> (files or folders), or null if the format isn't available.</returns>
    /// <seealso cref="DataFormat.Files"/>.
    public static async Task<IStorageItem[]?> TryGetFilesAsync(this IAsyncDataTransfer dataTransfer)
        => await dataTransfer.TryGetAsync(DataFormat.Files).ConfigureAwait(false) as IStorageItem[];
}
