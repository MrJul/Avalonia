#nullable enable

using System;
using System.IO;
using System.Linq;
using Avalonia.Input.Platform;
using Avalonia.Logging;
using Avalonia.Native.Interop;
using Avalonia.Platform.Storage;

namespace Avalonia.Native;

/// <summary>
/// Implementation of <see cref="IAvnManagedClipboardItem"/> using a <see cref="IDataTransferItem"/> as its source.
/// This class is called by native code.
/// </summary>
/// <param name="item">The item to use.</param>
internal sealed class ManagedClipboardItem(IDataTransferItem item) : NativeCallbackBase, IAvnManagedClipboardItem
{
    private readonly IDataTransferItem _item = item;

    IAvnStringArray IAvnManagedClipboardItem.ProvideFormats()
        => new AvnStringArray(_item.GetFormats().Select(ClipboardDataFormatHelper.ToNativeFormat));

    IAvnManagedClipboardValue? IAvnManagedClipboardItem.GetValue(string format)
    {
        var dataFormat = ClipboardDataFormatHelper.ToDataFormat(format);
        var data = _item.TryGetAsync(dataFormat).GetAwaiter().GetResult();

        if (DataFormat.Text.Equals(dataFormat))
            return new StringValue(Convert.ToString(data) ?? string.Empty);

        if (DataFormat.File.Equals(dataFormat))
        {
            var file = GetTypedData<IStorageItem>(data, dataFormat);
            return file is null ? null : new StringValue(file.Path.AbsoluteUri);
        }

        switch (data)
        {
            case null:
                return null;

            case byte[] bytes:
                return new BytesValue(bytes);

            case Memory<byte> bytes:
                return new BytesValue(bytes);

            case string str:
                return new StringValue(str);

            case Stream stream:
                var length = (int)(stream.Length - stream.Position);
                var buffer = new byte[length];
                stream.ReadExactly(buffer, 0, length);
                return new BytesValue(buffer.AsMemory(length));

            default:
                Logger.TryGet(LogEventLevel.Warning, LogArea.macOSPlatform)?.Log(
                this,
                "Unsupported value type {Type} for data format {Format}",
                data.GetType(),
                dataFormat);
                return null;
        }

        static T? GetTypedData<T>(object? data, DataFormat format) where T : class
            => data switch
            {
                null => null,
                T value => value,
                _ => throw new InvalidOperationException(
                    $"Expected a value of type {typeof(T)} for data format {format}, got {data.GetType()} instead.")
            };
    }

    protected override void Destroyed()
    {
        base.Destroyed();
    }

    private sealed class StringValue(string value) : NativeCallbackBase, IAvnManagedClipboardValue
    {
        private readonly string _value = value;

        int IAvnManagedClipboardValue.IsString()
            => true.AsComBool();

        IAvnString IAvnManagedClipboardValue.AsString()
            => new AvnString(_value);

        int IAvnManagedClipboardValue.ByteLength
            => throw new InvalidOperationException();

        unsafe void IAvnManagedClipboardValue.CopyBytesTo(void* buffer)
            => throw new InvalidOperationException();
    }

    private sealed class BytesValue(ReadOnlyMemory<byte> value) : NativeCallbackBase, IAvnManagedClipboardValue
    {
        private readonly ReadOnlyMemory<byte> _value = value;

        int IAvnManagedClipboardValue.IsString()
            => false.AsComBool();

        IAvnString IAvnManagedClipboardValue.AsString()
            => throw new InvalidOperationException();

        int IAvnManagedClipboardValue.ByteLength
            => _value.Length;

        unsafe void IAvnManagedClipboardValue.CopyBytesTo(void* buffer)
            => _value.Span.CopyTo(new Span<byte>(buffer, _value.Length));
    }
}
