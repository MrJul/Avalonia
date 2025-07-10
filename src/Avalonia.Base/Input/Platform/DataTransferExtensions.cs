using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Avalonia.Input.Platform;

/// <summary>
/// Contains extension methods for <see cref="IDataTransfer3"/>.
/// </summary>
public static class DataTransferExtensions
{
    // TODO12: remove
    internal static IDataObject ToLegacyDataObject(this IDataTransfer3 dataTransfer)
        => (dataTransfer as DataObjectToDataTransferWrapper)?.DataObject
           ?? new DataTransferToDataObjectWrapper(dataTransfer);

    /// <summary>
    /// Gets the formats supported by a <see cref="IDataTransfer3"/>.
    /// </summary>
    /// <param name="dataTransfer">The <see cref="IDataTransfer3"/> instance.</param>
    /// <returns>A list of supported formats.</returns>
    public static IEnumerable<DataFormat> GetFormats(this IDataTransfer3 dataTransfer)
        => dataTransfer.GetItems().SelectMany(item => item.GetFormats()).Distinct();

    /// <summary>
    /// Gets whether a <see cref="IDataTransfer3"/> supports a specific format.
    /// </summary>
    /// <param name="dataTransfer">The <see cref="IDataTransfer3"/> instance.</param>
    /// <param name="format">The format to check.</param>
    /// <returns>true if <paramref name="format"/> is supported, false otherwise.</returns>
    public static bool Contains(this IDataTransfer3 dataTransfer, DataFormat format)
        => dataTransfer.GetItems().Any(item => item.Contains(format));

    /// <summary>
    /// Tries to get a value for a given format from a <see cref="IDataTransfer3"/>.
    /// </summary>
    /// <param name="dataTransfer">The <see cref="IDataTransfer3"/> instance.</param>
    /// <param name="format">The format to retrieve.</param>
    /// <returns>A value for <paramref name="format"/>, or null if the format is not supported.</returns>
    /// <remarks>
    /// If the <see cref="IDataTransfer3"/> contains several items supporting <paramref name="format"/>,
    /// the first matching one will be returned.
    /// </remarks>
    public static async Task<T?> TryGetAsync<T>(this IDataTransfer3 dataTransfer, DataFormat format)
    {
        foreach (var item in dataTransfer.GetItems())
        {
            if (item.Contains(format))
            {
                var result = await item.TryGetAsync(format).ConfigureAwait(false);
                return result is T typedResult ? typedResult : default;
            }
        }

        return default;
    }

    /// <summary>
    /// Returns a text, if available, from a <see cref="IDataTransfer3"/> instance.
    /// </summary>
    /// <param name="dataTransfer">The data transfer instance.</param>
    /// <returns>A string, or null if the format isn't available.</returns>
    /// <seealso cref="DataFormat.Text"/>.
    public static Task<string?> TryGetTextAsync(this IDataTransfer3 dataTransfer)
        => dataTransfer.TryGetAsync<string>(DataFormat.Text);

    /// <summary>
    /// Returns a list of files, if available, from a <see cref="IDataTransfer3"/> instance.
    /// </summary>
    /// <param name="dataTransfer">The data transfer instance.</param>
    /// <returns>An array of <see cref="IStorageItem"/> (files or folders), or null if the format isn't available.</returns>
    /// <seealso cref="DataFormat.Files"/>.
    public static async Task<IStorageItem[]?> TryGetFilesAsync(this IDataTransfer3 dataTransfer)
        => await dataTransfer.TryGetAsync<IStorageItem[]>(DataFormat.Files).ConfigureAwait(false);
}
