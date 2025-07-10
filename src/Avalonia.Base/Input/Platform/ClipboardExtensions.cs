using System.Threading.Tasks;

namespace Avalonia.Input.Platform;

/// <summary>
/// Contains extension methods related to <see cref="IClipboard"/>.
/// </summary>
public static class ClipboardExtensions
{

    /// <summary>
    /// Tries to get a value for a given format from a <see cref="IClipboard"/>.
    /// </summary>
    /// <param name="clipboard">The <see cref="IClipboard"/> instance.</param>
    /// <param name="format">The format to retrieve.</param>
    /// <returns>A value for <paramref name="format"/>, or null if the format is not supported.</returns>
    /// <remarks>
    /// If the <see cref="IClipboard"/> contains several items supporting <paramref name="format"/>,
    /// the first matching one will be returned.
    /// </remarks>
    public static async Task<T?> TryGetDataAsync<T>(this IClipboard clipboard, DataFormat format)
    {
        // No ConfigureAwait(false) here: we want TryGetAsync() below to be called on the initial thread.
        using var dataTransfer = await clipboard.TryGetDataAsync([format]);
        if (dataTransfer is null)
            return default;

        // However, ConfigureAwait(false) is fine here: we're not doing anything after.
        return await dataTransfer.TryGetAsync<T>(format).ConfigureAwait(false);
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
    public static Task SetDataAsync<T>(this IClipboard clipboard, DataFormat format, T? value)
    {
        if (value is null)
            return clipboard.ClearAsync();

        var dataTransfer = new DataTransfer();
        dataTransfer.Items.Add(DataTransferItem3.Create(format, value));
        return clipboard.SetDataAsync(dataTransfer);
    }

    /// <summary>
    /// Returns a text, if available, from the clipboard.
    /// </summary>
    /// <param name="clipboard">The clipboard instance.</param>
    /// <returns>A string, or null if the format isn't available.</returns>
    /// <seealso cref="DataFormat.Text"/>
    public static Task<string?> TryGetTextAsync(this IClipboard clipboard)
        => clipboard.TryGetDataAsync<string>(DataFormat.Text);

    /// <summary>
    /// Places a text on the clipboard.
    /// </summary>
    /// <param name="clipboard">The clipboard instance.</param>
    /// <param name="value">The value to place on the clipboard.</param>
    /// <remarks>
    /// <para>By calling this method, the clipboard will get cleared of any possible previous data.</para>
    /// <para>
    /// If <paramref name="value"/> is null, nothing will get placed on the clipboard and this method
    /// will be equivalent to <see cref="IClipboard.ClearAsync"/>.
    /// </para>
    /// </remarks>
    /// <seealso cref="DataFormat.Text"/>
    public static Task SetTextAsync(this IClipboard clipboard, string? value)
        => clipboard.SetDataAsync(DataFormat.Text, value);
}
