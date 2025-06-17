using System;
using System.Threading.Tasks;
using Avalonia.Metadata;

namespace Avalonia.Input.Platform
{
    /// <summary>
    /// Represents the system clipboard.
    /// </summary>
    [NotClientImplementable]
    public interface IClipboard
    {
        // TODO12: remove and convert to a TryGetTextAsync extension method based on TryGetDataAsync()
        /// <summary>
        /// Returns a string containing the text data on the clipboard.
        /// </summary>
        /// <returns>A string containing text data, or null if no corresponding text data is available.</returns>
        Task<string?> GetTextAsync();

        // TODO12: remove and convert to a SetGetTextAsync extension method based on SetDataTransferAsync()
        /// <summary>
        /// Places a text on the clipboard.
        /// </summary>
        /// <param name="text">The text data to set.</param>
        /// <remarks>
        /// <para>By calling this method, the clipboard will get cleared of any possible previous data.</para>
        /// <para>
        /// If <paramref name="text"/> is null or empty, nothing will get placed on the clipboard and this method
        /// will be equivalent to <see cref="ClearAsync"/>.
        /// </para>
        /// </remarks>
        Task SetTextAsync(string? text);

        /// <summary>
        /// Clears any data from the system clipboard.
        /// </summary>
        Task ClearAsync();

        /// <summary>
        /// Places a specified non-persistent data object on the system Clipboard.
        /// </summary>
        /// <param name="data">A data object (an object that implements <see cref="IDataObject"/>) to place on the system Clipboard.</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="data"/> is null.</exception>
        [Obsolete($"Use {nameof(SetDataTransferAsync)} instead.")]
        Task SetDataObjectAsync(IDataObject data);

        /// <summary>
        /// Places a data object on the clipboard.
        /// The data object is responsible for providing supported formats and data upon request.
        /// </summary>
        /// <param name="dataTransfer">The data object to set on the clipboard.</param>
        /// <remarks>
        /// If <paramref name="dataTransfer"/> is null, nothing will get placed on the clipboard and this method
        /// will be equivalent to <see cref="ClearAsync"/>.
        /// </remarks>
        Task SetDataTransferAsync(IAsyncDataTransfer? dataTransfer);

        /// <summary>
        /// Permanently adds the data that is on the Clipboard so that it is available after the data's original application closes.
        /// </summary>
        /// <returns></returns>
        /// <remarks>This method is only supported on the Windows platform. This method will do nothing on other platforms.</remarks>
        Task FlushAsync();

        /// <summary>
        /// Get list of available Clipboard format.
        /// </summary>
        [Obsolete($"Use {nameof(GetDataFormatsAsync)} instead.")]
        Task<string[]> GetFormatsAsync();

        /// <summary>
        /// Gets a list containing the formats currently available from the clipboard.
        /// </summary>
        /// <returns>A list of formats. It can be empty if the clipboard is empty.</returns>
        Task<DataFormat[]> GetDataFormatsAsync();

        /// <summary>
        /// Retrieves data in a specified format from the Clipboard.
        /// </summary>
        /// <param name="format">A string that specifies the format of the data to retrieve. For a set of predefined data formats, see the <see cref="DataFormats"/> class.</param>
        /// <returns></returns>
        [Obsolete($"Use {nameof(TryGetDataAsync)} instead.")]
        Task<object?> GetDataAsync(string format);

        /// <summary>
        /// Retrieves data from the clipboard in the specified format.
        /// </summary>
        /// <param name="format">The requested format.</param>
        /// <returns>A value corresponding to <paramref name="format"/>, or null if the clipboard doesn't contain the specified format.</returns>
        Task<object?> TryGetDataAsync(DataFormat format);
        
        /// <summary>
        /// If clipboard contains the IDataObject that was set by a previous call to <see cref="SetDataObjectAsync(Avalonia.Input.IDataObject)"/>,
        /// return said IDataObject instance. Otherwise, return null.
        /// Note that not every platform supports that method, on unsupported platforms this method will always return
        /// null
        /// </summary>
        /// <returns></returns>
        [Obsolete($"Use {nameof(TryGetInProcessDataTransferAsync)} instead.")]
        Task<IDataObject?> TryGetInProcessDataObjectAsync();

        /// <summary>
        /// Retrieves a <see cref="IAsyncDataTransfer"/> previously placed on the clipboard by <see cref="SetDataTransferAsync"/>, if any.
        /// </summary>
        /// <returns>The data object if present, null otherwise.</returns>
        /// <remarks>
        /// <para>This method cannot be used to retrieve a <see cref="IAsyncDataTransfer"/> set by another process.</para>
        /// <para>This method is only supported on Windows, macOS and X11 platforms. Other platforms will always return null.</para>
        /// </remarks>
        Task<IAsyncDataTransfer?> TryGetInProcessDataTransferAsync();
    }
}
