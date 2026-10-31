// Hand-written declarations of exactly the Apple API that LetteraPlatformIOS.mm uses, transcribed from Apple's documentation, for a syntax check on a machine with no SDK.
// This proves syntax and self-consistency. A wrong selector still compiles as far as the real SDK is concerned: only Xcode and a device can tell.
#pragma once
#include <stddef.h>
#include <stdint.h>

#define NS_ASSUME_NONNULL_BEGIN
#define NS_ASSUME_NONNULL_END
#define API_AVAILABLE(...)

typedef signed char BOOL;
#define YES ((BOOL)1)
#define NO ((BOOL)0)
#define nil nullptr
typedef long NSInteger;
typedef unsigned long NSUInteger;
typedef double CGFloat;
struct CGPoint { CGFloat x, y; };
struct CGSize { CGFloat width, height; };
struct CGRect { CGPoint origin; CGSize size; };
struct NSRange { NSUInteger location, length; };
inline CGRect CGRectMake(CGFloat x, CGFloat y, CGFloat w, CGFloat h) { return CGRect{ { x, y }, { w, h } }; }
inline CGPoint CGPointMake(CGFloat x, CGFloat y) { return CGPoint{ x, y }; }
inline NSRange NSMakeRange(NSUInteger a, NSUInteger b) { return NSRange{ a, b }; }
inline NSRange NSIntersectionRange(NSRange a, NSRange b) { return a; }
inline CGFloat CGRectGetMidX(CGRect r) { return r.origin.x + r.size.width / 2; }
inline CGFloat CGRectGetMinY(CGRect r) { return r.origin.y; }
#define MAX(a, b) ((a) > (b) ? (a) : (b))

@class NSString, NSData, NSNotification, NSNotificationCenter, UIResponder, UIView, UIPress, UIPressesEvent, UITextRange, UITextPosition, UIImage, UIAction, UIMenu, UIMenuElement, UIEditMenuConfiguration, UIEditMenuInteraction;

__attribute__((objc_root_class))
@interface NSObject
+ (instancetype)alloc;
+ (instancetype)new;
- (instancetype)init;
@end
@protocol NSObject
@end

@interface NSNumber : NSObject
@property (readonly) int intValue;
+ (NSNumber*)numberWithInt:(int)value;
+ (NSNumber*)numberWithLong:(long)value;
+ (NSNumber*)numberWithInteger:(NSInteger)value;
@end

@interface NSArray<__covariant ObjectType> : NSObject
+ (instancetype)arrayWithObjects:(const id _Nonnull [])objects count:(NSUInteger)count;
@property (readonly) NSUInteger count;
- (ObjectType)objectAtIndexedSubscript:(NSUInteger)index;
@end
@interface NSMutableArray<ObjectType> : NSArray<ObjectType>
+ (instancetype)array;
- (void)addObject:(ObjectType)value;
- (void)removeObjectAtIndex:(NSUInteger)index;
- (void)removeAllObjects;
@end
@interface NSSet<ObjectType> : NSObject
- (NSUInteger)countByEnumeratingWithState:(void*)state objects:(id __unsafe_unretained*)buffer count:(NSUInteger)length;
@end
@interface NSDictionary<KeyType, ObjectType> : NSObject
- (ObjectType)objectForKeyedSubscript:(KeyType)key;
@end
@interface NSMutableDictionary<KeyType, ObjectType> : NSDictionary<KeyType, ObjectType>
+ (instancetype)dictionary;
- (void)setObject:(ObjectType)value forKeyedSubscript:(KeyType)key;
@end

#define NSUTF8StringEncoding 4
@interface NSString : NSObject
@property (readonly) NSUInteger length;
@property (readonly) const char* UTF8String;
+ (instancetype)stringWithUTF8String:(const char*)text;
+ (instancetype)stringWithFormat:(NSString*)format, ...;
- (NSString*)stringByReplacingOccurrencesOfString:(NSString*)a withString:(NSString*)b;
- (NSString*)stringByReplacingCharactersInRange:(NSRange)range withString:(NSString*)b;
- (BOOL)isEqualToString:(NSString*)other;
- (id)copy;
- (NSData*)dataUsingEncoding:(NSUInteger)encoding;
- (NSArray<NSString*>*)componentsSeparatedByString:(NSString*)separator;
- (unsigned short)characterAtIndex:(NSUInteger)index;
@end
@interface NSData : NSObject
@property (readonly) NSUInteger length;
@property (readonly) const void* bytes;
@end
@interface NSValue : NSObject
@property (readonly) CGRect CGRectValue;
@end
@interface NSNotification : NSObject
@property (readonly) NSDictionary* userInfo;
@end
@interface NSNotificationCenter : NSObject
+ (NSNotificationCenter*)defaultCenter;
- (id)addObserverForName:(NSString*)name object:(id)object queue:(id)queue usingBlock:(void (^)(NSNotification* note))block;
- (void)removeObserver:(id)observer;
@end

extern NSString* const UIKeyboardWillChangeFrameNotification;
extern NSString* const UIKeyboardFrameEndUserInfoKey;

typedef NSString* UITextContentType;
extern UITextContentType const UITextContentTypeUsername, UITextContentTypePassword, UITextContentTypeNewPassword, UITextContentTypeOneTimeCode, UITextContentTypeEmailAddress, UITextContentTypeName, UITextContentTypeGivenName, UITextContentTypeFamilyName, UITextContentTypeTelephoneNumber, UITextContentTypeFullStreetAddress, UITextContentTypePostalCode, UITextContentTypeCountryName, UITextContentTypeCreditCardNumber;

typedef enum : NSInteger { UITextAutocapitalizationTypeNone, UITextAutocapitalizationTypeWords, UITextAutocapitalizationTypeSentences, UITextAutocapitalizationTypeAllCharacters } UITextAutocapitalizationType;
typedef enum : NSInteger { UITextAutocorrectionTypeDefault, UITextAutocorrectionTypeNo, UITextAutocorrectionTypeYes } UITextAutocorrectionType;
typedef enum : NSInteger { UITextSpellCheckingTypeDefault, UITextSpellCheckingTypeNo, UITextSpellCheckingTypeYes } UITextSpellCheckingType;
typedef enum : NSInteger { UIKeyboardAppearanceDefault, UIKeyboardAppearanceDark, UIKeyboardAppearanceLight } UIKeyboardAppearance;
typedef enum : NSInteger { UITextSmartQuotesTypeDefault, UITextSmartQuotesTypeNo, UITextSmartQuotesTypeYes } UITextSmartQuotesType;
typedef enum : NSInteger { UITextSmartDashesTypeDefault, UITextSmartDashesTypeNo, UITextSmartDashesTypeYes } UITextSmartDashesType;
typedef enum : NSInteger { UITextSmartInsertDeleteTypeDefault, UITextSmartInsertDeleteTypeNo, UITextSmartInsertDeleteTypeYes } UITextSmartInsertDeleteType;
typedef enum : NSInteger { UIKeyboardTypeDefault, UIKeyboardTypeASCIICapable, UIKeyboardTypeURL, UIKeyboardTypeNumberPad, UIKeyboardTypeDecimalPad, UIKeyboardTypePhonePad, UIKeyboardTypeNamePhonePad, UIKeyboardTypeEmailAddress, UIKeyboardTypeWebSearch } UIKeyboardType;
typedef enum : NSInteger { UIReturnKeyDefault, UIReturnKeyGo, UIReturnKeySearch, UIReturnKeySend, UIReturnKeyNext, UIReturnKeyDone } UIReturnKeyType;
typedef enum : NSInteger { UIKeyboardHIDUsageKeyboardRightArrow = 79, UIKeyboardHIDUsageKeyboardLeftArrow = 80, UIKeyboardHIDUsageKeyboardDownArrow = 81, UIKeyboardHIDUsageKeyboardUpArrow = 82 } UIKeyboardHIDUsage;
typedef enum : NSInteger { UIKeyModifierShift = 1 << 17, UIKeyModifierControl = 1 << 18, UIKeyModifierAlternate = 1 << 19, UIKeyModifierCommand = 1 << 20 } UIKeyModifierFlags;
typedef enum : NSUInteger { UIMenuElementAttributesDisabled = 1 } UIMenuElementAttributes;

extern "C" BOOL UIAccessibilityIsReduceMotionEnabled(void);

@interface UIKey : NSObject
@property (readonly) UIKeyboardHIDUsage keyCode;
@property (readonly) UIKeyModifierFlags modifierFlags;
@end
@interface UIPress : NSObject
@property (readonly) UIKey* key;
@end
@interface UIPressesEvent : NSObject
@end
@interface UITextPosition : NSObject
@end
@interface UITextRange : NSObject
@property (readonly) UITextPosition* start;
@property (readonly) UITextPosition* end;
@end
@interface UIResponder : NSObject
- (BOOL)becomeFirstResponder;
- (BOOL)resignFirstResponder;
- (void)pressesBegan:(NSSet<UIPress*>*)presses withEvent:(UIPressesEvent*)event;
@end
@protocol UIInteraction
@end
@interface UIView : UIResponder
- (instancetype)initWithFrame:(CGRect)frame;
@property CGFloat alpha;
@property CGRect frame;
@property CGFloat contentScaleFactor;
- (void)addSubview:(UIView*)view;
- (void)removeFromSuperview;
- (void)addInteraction:(id<UIInteraction>)interaction;
@end
@protocol UITextViewDelegate
@end
@interface UITextView : UIView
@property (weak) id<UITextViewDelegate> delegate;
@property (copy) NSString* text;
@property NSRange selectedRange;
@property (readonly) UITextRange* markedTextRange;
@property (readonly) UITextPosition* beginningOfDocument;
@property UIKeyboardType keyboardType;
@property UIReturnKeyType returnKeyType;
@property UITextAutocapitalizationType autocapitalizationType;
@property UITextAutocorrectionType autocorrectionType;
@property UITextSpellCheckingType spellCheckingType;
@property UIKeyboardAppearance keyboardAppearance;
@property UITextSmartQuotesType smartQuotesType;
@property UITextSmartDashesType smartDashesType;
@property UITextSmartInsertDeleteType smartInsertDeleteType;
@property BOOL enablesReturnKeyAutomatically;
@property BOOL secureTextEntry;
@property UITextContentType textContentType;
- (NSInteger)offsetFromPosition:(UITextPosition*)a toPosition:(UITextPosition*)b;
- (NSString*)textInRange:(UITextRange*)range;
@end
@interface UIPasteboard : NSObject
+ (UIPasteboard*)generalPasteboard;
@property (copy) NSArray* items;
@property (readonly) BOOL hasStrings;
@property (readonly) BOOL hasImages;
@property (readonly) BOOL hasURLs;
@property (copy) NSString* string;
- (NSData*)dataForPasteboardType:(NSString*)type;
@end
@interface UIMenuElement : NSObject
@end
@interface UIAction : UIMenuElement
@property UIMenuElementAttributes attributes;
+ (instancetype)actionWithTitle:(NSString*)title image:(UIImage*)image identifier:(NSString*)identifier handler:(void (^)(__kindof UIAction* action))handler;
@end
@interface UIMenu : UIMenuElement
+ (UIMenu*)menuWithChildren:(NSArray<UIMenuElement*>*)children;
@end
@interface UIEditMenuConfiguration : NSObject
+ (instancetype)configurationWithIdentifier:(id)identifier sourcePoint:(CGPoint)point;
@end
@protocol UIEditMenuInteractionAnimating
@end
@protocol UIEditMenuInteractionDelegate
@end
@interface UIEditMenuInteraction : NSObject <UIInteraction>
- (instancetype)initWithDelegate:(id<UIEditMenuInteractionDelegate>)delegate;
- (void)presentEditMenuWithConfiguration:(UIEditMenuConfiguration*)configuration;
- (void)dismissMenu;
@end
@interface UITextLoupeSession : NSObject
+ (instancetype)beginLoupeSessionAtPoint:(CGPoint)point fromSelectionWidgetView:(UIView*)widget inView:(UIView*)view;
- (void)moveToPoint:(CGPoint)point withCaretRect:(CGRect)caret trackingCaret:(BOOL)tracking;
- (void)invalidate;
@end
