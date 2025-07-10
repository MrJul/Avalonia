using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using MicroCom.Runtime;
using static Avalonia.Win32.Interop.UnmanagedMethods;
using FORMATETC = Avalonia.Win32.Interop.UnmanagedMethods.FORMATETC;
using IEnumFORMATETC = Avalonia.Win32.Win32Com.IEnumFORMATETC;
using STGMEDIUM = Avalonia.Win32.Interop.UnmanagedMethods.STGMEDIUM;

namespace Avalonia.Win32;

/// <summary>
/// Wraps a Win32 <see cref="Win32Com.IDataObject"/> into a <see cref="IDataTransfer3"/>.
/// </summary>
/// <param name="oleDataObject">The wrapped OLE data object.</param>
internal sealed class OleDataObjectToDataTransferWrapper(Win32Com.IDataObject oleDataObject)
    : IDataTransfer3, IDataTransferItem
{
    private readonly Win32Com.IDataObject _oleDataObject = oleDataObject.CloneReference();

    IEnumerable<IDataTransferItem> IDataTransfer3.GetItems()
        => [this];

    public IEnumerable<DataFormat> GetFormats()
    {
        if (_oleDataObject.EnumFormatEtc((int)DATADIR.DATADIR_GET) is not { } enumFormat)
            yield break;

        enumFormat.Reset();

        while (Next(enumFormat) is { } format)
        {
            yield return format;
        }

        static unsafe DataFormat? Next(IEnumFORMATETC enumFormat)
        {
            var fetched = 1u;
            FORMATETC formatEtc;

            var result = enumFormat.Next(1, &formatEtc, &fetched);
            if (result != 0 || fetched == 0)
                return null;

            if (formatEtc.ptd != IntPtr.Zero)
                Marshal.FreeCoTaskMem(formatEtc.ptd);

            return ClipboardFormatRegistry.GetFormatById(formatEtc.cfFormat);
        }
    }

    public bool Contains(DataFormat format)
        => GetFormats().Contains(format);

    public unsafe object? TryGet(DataFormat format)
    {
        var formatEtc = format.ToFormatEtc();

        if (_oleDataObject.QueryGetData(&formatEtc) != (uint)HRESULT.S_OK)
            return null;

        var medium = new STGMEDIUM();
        if (_oleDataObject.GetData(&formatEtc, &medium) != (uint)HRESULT.S_OK)
            return null;

        try
        {
            if (medium.tymed == TYMED.TYMED_HGLOBAL && medium.unionmember != IntPtr.Zero)
            {
                var hGlobal = medium.unionmember;
                return OleDataObjectHelper.ReadDataFromHGlobal(format, hGlobal);
            }
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }

        return null;
    }

    Task<object?> IDataTransferItem.TryGetAsync(DataFormat format)
        => Task.FromResult(TryGet(format));

    public void Dispose()
        => _oleDataObject.Dispose();
}
