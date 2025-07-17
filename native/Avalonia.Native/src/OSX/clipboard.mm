#include "common.h"
#include "AvnString.h"

@interface ClipboardItem : NSObject <NSPasteboardWriting>
- (instancetype) initWithManagedItem:(nonnull IAvnManagedClipboardItem*) managedItem;
@end

class Clipboard : public ComSingleObject<IAvnClipboard, &IID_IAvnClipboard>
{
private:
    NSPasteboard* _pb;
    NSMutableArray* _pendingItems;
public:
    FORWARD_IUNKNOWN()
    
    Clipboard(NSPasteboard* pasteboard)
    {
        if (pasteboard == nil)
            pasteboard = [NSPasteboard generalPasteboard];

        _pb = pasteboard;
        _pendingItems = nil;
    }
    
    virtual HRESULT GetFormats(int64_t changeCount, IAvnStringArray** ret) override
    {
        START_COM_ARP_CALL;
        
        if (ret == nullptr)
            return E_POINTER;
        
        if (changeCount != [_pb changeCount])
            return COR_E_OBJECTDISPOSED;
        
        auto types = [_pb types];
        *ret = types == nil ? nullptr : CreateAvnStringArray(types);
        return S_OK;
    }
    
    virtual HRESULT GetItemCount(int64_t changeCount, int* ret) override
    {
        START_COM_ARP_CALL;
        
        if (ret == nullptr)
            return E_POINTER;
        
        if (changeCount != [_pb changeCount])
            return COR_E_OBJECTDISPOSED;
        
        auto items = [_pb pasteboardItems];
        *ret = items == nil ? 0 : (int)[items count];
        return S_OK;
    }
    
    virtual HRESULT GetItemFormats(int index, int64_t changeCount, IAvnStringArray** ret) override
    {
        START_COM_ARP_CALL;
        
        if (ret == nullptr)
            return E_POINTER;
        
        if (changeCount != [_pb changeCount])
            return COR_E_OBJECTDISPOSED;
        
        auto item = [[_pb pasteboardItems] objectAtIndex:index];
        auto types = [item types];
        *ret = types == nil ? nullptr : CreateAvnStringArray(types);
        return S_OK;
    }
    
    virtual HRESULT GetItemValueAsString(int index, int64_t changeCount, const char* format, IAvnString** ret) override
    {
        START_COM_ARP_CALL;
        
        if (ret == nullptr)
            return E_POINTER;
        
        if (changeCount != [_pb changeCount])
            return COR_E_OBJECTDISPOSED;
        
        auto item = [[_pb pasteboardItems] objectAtIndex:index];
        auto value = [item stringForType:[NSString stringWithUTF8String:format]];
        *ret = value == nil ? nullptr : CreateAvnString(value);
        return S_OK;
    }
    
    virtual HRESULT GetItemValueAsBytes(int index, int64_t changeCount, const char* format, IAvnString** ret) override
    {
        START_COM_ARP_CALL;
        
        if (ret == nullptr)
            return E_POINTER;
        
        if (changeCount != [_pb changeCount])
            return COR_E_OBJECTDISPOSED;
        
        auto item = [[_pb pasteboardItems] objectAtIndex:index];
        auto value = [item dataForType:[NSString stringWithUTF8String:format]];
        
        *ret = value == nil || [value length] == 0
            ? nullptr
            : CreateByteArray((void*)[value bytes], (int)[value length]);
        return S_OK;
    }

    virtual HRESULT Clear(int64_t* ret) override
    {
        START_COM_ARP_CALL;
        
        *ret = [_pb clearContents];
        return S_OK;
    }
    
    virtual HRESULT GetChangeCount(int64_t* ret) override
    {
        START_COM_ARP_CALL;
        
        *ret = [_pb changeCount];
        return S_OK;
    }
    
    virtual HRESULT AddPendingItem(IAvnManagedClipboardItem* item) override
    {
        START_COM_ARP_CALL;
    
        if (_pendingItems == nil)
            _pendingItems = [NSMutableArray array];
        
        auto clipboardItem = [[ClipboardItem alloc] initWithManagedItem:item];
        [_pendingItems addObject:clipboardItem];
        return S_OK;
    }
    
    virtual HRESULT WritePendingItems() override
    {
        START_COM_ARP_CALL;
        
        if (_pendingItems != nil)
        {
            [_pb writeObjects:_pendingItems];
            [_pendingItems removeAllObjects];
        }
        
        return S_OK;
    }
};


extern IAvnClipboard* CreateClipboard(NSPasteboard* pb)
{
    return new Clipboard(pb);
}


@implementation ClipboardItem
{
    IAvnManagedClipboardItem* _managedItem;
}
    
- (ClipboardItem*) initWithManagedItem:(nonnull IAvnManagedClipboardItem*) managedItem
{
    self = [super init];
    _managedItem = managedItem;
    return self;
}

- (nonnull NSArray<NSPasteboardType>*)writableTypesForPasteboard:(nonnull NSPasteboard*)pasteboard
{
    return GetNSArrayOfStringsAndRelease(_managedItem->ProvideFormats());
}

- (NSPasteboardWritingOptions)writingOptionsForType:(NSPasteboardType)type pasteboard:(NSPasteboard *)pasteboard
{
    return [type isEqualToString:NSPasteboardTypeString] ? 0 : NSPasteboardWritingPromised;
}

- (nullable id)pasteboardPropertyListForType:(nonnull NSPasteboardType)type
{
    ComPtr<IAvnManagedClipboardValue> value(_managedItem->GetValue([type UTF8String]), true);
    if (value == nullptr)
        return nil;
    
    if (value->IsString())
        return GetNSStringAndRelease(value->AsString());
    
    auto length = value->GetByteLength();
    auto buffer = malloc(length);
    value->CopyBytesTo(buffer);
    return [NSData dataWithBytesNoCopy:buffer length:length];
}

- (void)dealloc
{
    if (_managedItem != nullptr)
    {
        _managedItem->Release();
        _managedItem = nullptr;
    }
}

@end
