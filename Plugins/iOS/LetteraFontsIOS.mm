// Asks CoreText which font files iOS uses, since system font locations are not public API.
#import <CoreText/CoreText.h>
#include <string.h>

static int CopyFontPath(CTFontRef font, char* output, int capacity)
{
    if (font == NULL) return 0;
    int length = 0;
    CFURLRef url = (CFURLRef)CTFontCopyAttribute(font, kCTFontURLAttribute);
    if (url != NULL)
    {
        if (CFURLGetFileSystemRepresentation(url, true, (UInt8*)output, capacity))
            length = (int)strlen(output);
        CFRelease(url);
    }
    return length;
}

extern "C" int lt_ios_default_path(char* output, int capacity)
{
    CTFontRef font = CTFontCreateUIFontForLanguage(kCTFontUIFontSystem, 0, NULL);
    int length = CopyFontPath(font, output, capacity);
    if (font != NULL) CFRelease(font);
    return length;
}

extern "C" int lt_ios_named_path(const char* name, char* output, int capacity)
{
    CFStringRef fontName = CFStringCreateWithCString(kCFAllocatorDefault, name, kCFStringEncodingUTF8);
    if (fontName == NULL) return 0;
    CTFontRef font = CTFontCreateWithName(fontName, 12, NULL);
    CFRelease(fontName);
    int length = CopyFontPath(font, output, capacity);
    if (font != NULL) CFRelease(font);
    return length;
}

extern "C" int lt_ios_fallback_path(int codepoint, const char* language, char* output, int capacity)
{
    CFStringRef lang = language != NULL ? CFStringCreateWithCString(kCFAllocatorDefault, language, kCFStringEncodingUTF8) : NULL;
    CTFontRef base = CTFontCreateUIFontForLanguage(kCTFontUIFontSystem, 12, lang);
    if (lang != NULL) CFRelease(lang);
    if (base == NULL) return 0;

    UniChar units[2];
    CFIndex count = 1;
    if (codepoint >= 0x10000)
    {
        int value = codepoint - 0x10000;
        units[0] = (UniChar)(0xD800 + (value >> 10));
        units[1] = (UniChar)(0xDC00 + (value & 0x3FF));
        count = 2;
    }
    else
    {
        units[0] = (UniChar)codepoint;
    }
    CFStringRef text = CFStringCreateWithCharacters(kCFAllocatorDefault, units, count);
    int length = 0;
    if (text != NULL)
    {
        CTFontRef font = CTFontCreateForString(base, text, CFRangeMake(0, count));
        length = CopyFontPath(font, output, capacity);
        if (font != NULL) CFRelease(font);
        CFRelease(text);
    }
    CFRelease(base);
    return length;
}
