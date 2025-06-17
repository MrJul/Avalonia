using System.Collections.Generic;
using System.Threading.Tasks;

namespace Avalonia.Input.Platform;

/// <summary>
/// Contains extension methods related to <see cref="IClipboard"/>.
/// </summary>
public static class ClipboardExtensions
{
    /// <summary>
    /// Places data on the clipboard in the specified formats.
    /// </summary>
    /// <param name="clipboard">The clipboard instance.</param>
    /// <param name="values">A collection of (format, value) pairs to place on the clipboard.</param>
    /// <remarks>
    /// <para>By calling this method, the clipboard will get cleared of any possible previous data.</para>
    /// <para>
    /// If <paramref name="values"/> is empty, nothing will get placed on the clipboard and this method
    /// will be equivalent to <see cref="IClipboard.ClearAsync"/>.
    /// </para>
    /// </remarks>
    public static Task SetDataAsync(this IClipboard clipboard, IEnumerable<KeyValuePair<DataFormat, object>> values)
    {
        var dataObject = new DataTransfer();

        foreach (var pair in values)
            dataObject.Set(pair.Key, pair.Value);

        return clipboard.SetDataTransferAsync(dataObject);
    }

    /// <summary>
    /// Places data on the clipboard in the specified format.
    /// </summary>
    /// <param name="clipboard">The clipboard instance.</param>
    /// <param name="format">The data format.</param>
    /// <param name="value">The value to place on the clipboard.</param>
    /// <remarks>
    /// <para>By calling this method, the clipboard will get cleared of any possible previous data.</para>
    /// <para>
    /// If <paramref name="value"/> is null, nothing will get placed on the clipboard and this method
    /// will be equivalent to <see cref="IClipboard.ClearAsync"/>.
    /// </para>
    /// </remarks>
    public static Task SetDataAsync(this IClipboard clipboard, DataFormat format, object? value)
    {
        if (value is null)
            return clipboard.ClearAsync();

        var dataObject = new DataTransfer();
        dataObject.Set(format, value);

        return clipboard.SetDataTransferAsync(dataObject);
    }
}
