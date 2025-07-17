#include "common.h"
#include "AvnString.h"

@interface WritableClipboardItem : NSObject <NSPasteboardWriting>
- (instancetype) initWithItem:(nonnull IAvnClipboardDataItem*)item source:(nonnull IAvnClipboardDataSource*)source;
@end

class Clipboard : public ComSingleObject<IAvnClipboard, &IID_IAvnClipboard>
{
private:
    NSPasteboard* _pasteboard;
public:
    FORWARD_IUNKNOWN()
    
    Clipboard(NSPasteboard* pasteboard)
    {
        if (pasteboard == nil)
            pasteboard = [NSPasteboard generalPasteboard];

        _pasteboard = pasteboard;
    }
    
    virtual HRESULT GetFormats(int64_t changeCount, IAvnStringArray** ret) override
    {
        START_COM_ARP_CALL;
        
        if (ret == nullptr)
            return E_POINTER;
        
        if (changeCount != [_pasteboard changeCount])
            return COR_E_OBJECTDISPOSED;
        
        auto types = [_pasteboard types];
        *ret = types == nil ? nullptr : CreateAvnStringArray(types);
        return S_OK;
    }
    
    virtual HRESULT GetItemCount(int64_t changeCount, int* ret) override
    {
        START_COM_ARP_CALL;
        
        if (ret == nullptr)
            return E_POINTER;
        
        if (changeCount != [_pasteboard changeCount])
            return COR_E_OBJECTDISPOSED;
        
        auto items = [_pasteboard pasteboardItems];
        *ret = items == nil ? 0 : (int)[items count];
        return S_OK;
    }
    
    virtual HRESULT GetItemFormats(int index, int64_t changeCount, IAvnStringArray** ret) override
    {
        START_COM_ARP_CALL;
        
        if (ret == nullptr)
            return E_POINTER;
        
        if (changeCount != [_pasteboard changeCount])
            return COR_E_OBJECTDISPOSED;
        
        auto item = [[_pasteboard pasteboardItems] objectAtIndex:index];
        auto types = [item types];
        *ret = types == nil ? nullptr : CreateAvnStringArray(types);
        return S_OK;
    }
    
    virtual HRESULT GetItemValueAsString(int index, int64_t changeCount, const char* format, IAvnString** ret) override
    {
        START_COM_ARP_CALL;
        
        if (ret == nullptr)
            return E_POINTER;
        
        if (changeCount != [_pasteboard changeCount])
            return COR_E_OBJECTDISPOSED;
        
        auto item = [[_pasteboard pasteboardItems] objectAtIndex:index];
        auto value = [item stringForType:[NSString stringWithUTF8String:format]];
        *ret = value == nil ? nullptr : CreateAvnString(value);
        return S_OK;
    }
    
    virtual HRESULT GetItemValueAsBytes(int index, int64_t changeCount, const char* format, IAvnString** ret) override
    {
        START_COM_ARP_CALL;
        
        if (ret == nullptr)
            return E_POINTER;
        
        if (changeCount != [_pasteboard changeCount])
            return COR_E_OBJECTDISPOSED;
        
        auto item = [[_pasteboard pasteboardItems] objectAtIndex:index];
        auto value = [item dataForType:[NSString stringWithUTF8String:format]];
        
        *ret = value == nil || [value length] == 0
            ? nullptr
            : CreateByteArray((void*)[value bytes], (int)[value length]);
        return S_OK;
    }

    virtual HRESULT Clear(int64_t* ret) override
    {
        START_COM_ARP_CALL;
        
        *ret = [_pasteboard clearContents];
        return S_OK;
    }
    
    virtual HRESULT GetChangeCount(int64_t* ret) override
    {
        START_COM_ARP_CALL;
        
        *ret = [_pasteboard changeCount];
        return S_OK;
    }
    
    virtual HRESULT SetData(IAvnClipboardDataSource* source) override
    {
        START_COM_ARP_CALL;
        
        auto count = source->GetItemCount();
        auto writableItems = [NSMutableArray<WritableClipboardItem*> arrayWithCapacity:count];
        
        for (auto i = 0; i < count; ++i)
        {
            auto item = source->GetItem(i);
            auto writableItem = [[WritableClipboardItem alloc] initWithItem:item source:source];
            [writableItems addObject:writableItem];
        }
        
        [_pasteboard writeObjects:writableItems];
        return S_OK;
    }
};


extern IAvnClipboard* CreateClipboard(NSPasteboard* pb)
{
    return new Clipboard(pb);
}


@implementation WritableClipboardItem
{
    IAvnClipboardDataItem* _item;
    IAvnClipboardDataSource* _source;
}
    
- (WritableClipboardItem*) initWithItem:(nonnull IAvnClipboardDataItem*)item source:(nonnull IAvnClipboardDataSource*)source
{
    self = [super init];
    _item = item;
    _source = source;
    
    // Each item references its source so it doesn't get disposed too early.
    source->AddRef();
    
    return self;
}

- (nonnull NSArray<NSPasteboardType>*) writableTypesForPasteboard:(nonnull NSPasteboard*)pasteboard
{
    return GetNSArrayOfStringsAndRelease(_item->ProvideFormats());
}

- (NSPasteboardWritingOptions) writingOptionsForType:(NSPasteboardType)type pasteboard:(NSPasteboard*)pasteboard
{
    return [type isEqualToString:NSPasteboardTypeString] ? 0 : NSPasteboardWritingPromised;
}

- (nullable id) pasteboardPropertyListForType:(nonnull NSPasteboardType)type
{
    ComPtr<IAvnClipboardDataValue> value(_item->GetValue([type UTF8String]), true);
    if (value.getRaw() == nullptr)
        return nil;
    
    if (value->IsString())
        return GetNSStringAndRelease(value->AsString());
    
    auto length = value->GetByteLength();
    auto buffer = malloc(length);
    value->CopyBytesTo(buffer);
    return [NSData dataWithBytesNoCopy:buffer length:length];
}

- (void) dealloc
{
    if (_item != nullptr)
    {
        _item->Release();
        _item = nullptr;
    }
    
    if (_source != nullptr)
    {
        _source->Release();
        _source = nullptr;
    }
}

@end
