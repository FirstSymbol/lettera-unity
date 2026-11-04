// Hand-written declarations of exactly the CoreText and CoreFoundation API that LetteraFontsIOS.mm uses, transcribed from Apple's documentation, for a syntax check on a machine with no SDK.
// It proves syntax and self-consistency, not that Apple's headers say the same.
#pragma once
#include <stdint.h>

typedef unsigned char Boolean;
typedef unsigned char UInt8;
typedef unsigned short UniChar;
typedef long CFIndex;
typedef uint32_t CFStringEncoding;
typedef double CGFloat;
typedef const void* CFTypeRef;
typedef const struct __CFAllocator* CFAllocatorRef;
typedef const struct __CFString* CFStringRef;
typedef const struct __CFURL* CFURLRef;
typedef const struct __CTFont* CTFontRef;
typedef struct { CFIndex location; CFIndex length; } CFRange;
typedef struct { CGFloat a, b, c, d, tx, ty; } CGAffineTransform;
typedef uint32_t CTFontUIFontType;

enum { kCFStringEncodingUTF8 = 0x08000100 };
enum { kCTFontUIFontSystem = 2 };

extern "C" {
extern const CFAllocatorRef kCFAllocatorDefault;
extern const CFStringRef kCTFontURLAttribute;
void CFRelease(CFTypeRef cf);
Boolean CFURLGetFileSystemRepresentation(CFURLRef url, Boolean resolveAgainstBase, UInt8* buffer, CFIndex maxBufLen);
CFStringRef CFStringCreateWithCString(CFAllocatorRef alloc, const char* cStr, CFStringEncoding encoding);
CFStringRef CFStringCreateWithCharacters(CFAllocatorRef alloc, const UniChar* chars, CFIndex numChars);
CFTypeRef CTFontCopyAttribute(CTFontRef font, CFStringRef attribute);
CTFontRef CTFontCreateUIFontForLanguage(CTFontUIFontType uiType, CGFloat size, CFStringRef language);
CTFontRef CTFontCreateWithName(CFStringRef name, CGFloat size, const CGAffineTransform* matrix);
CTFontRef CTFontCreateForString(CTFontRef currentFont, CFStringRef string, CFRange range);
}

static inline CFRange CFRangeMake(CFIndex location, CFIndex length) { CFRange r = { location, length }; return r; }
