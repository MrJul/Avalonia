namespace Avalonia.Input.Platform;

public interface IDataTransfer
{
    DataFormat[] GetFormats();
    bool Contains(DataFormat format);
    object? TryGet(DataFormat format);
}
