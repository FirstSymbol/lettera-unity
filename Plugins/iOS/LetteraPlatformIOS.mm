// The system's own text control, pasteboard, menu and loupe for Lettera, on iOS. It maps system calls to one line per event, and decides nothing.
// Compiled only when a project turns IosNativeInBuild on: LetteraPlatformIOSStub.mm takes its place otherwise.
#import <UIKit/UIKit.h>
#include <string.h>

extern "C" UIView* UnityGetGLView(void);

static NSMutableArray<NSString*>* s_Lines;
static int s_Revision;
static NSString* s_Reported = @"";
static BOOL s_Pushing;

static NSString* Escape(NSString* text)
{
    if (text == nil) return @"";
    NSString* out = [text stringByReplacingOccurrencesOfString:@"\\" withString:@"\\\\"];
    out = [out stringByReplacingOccurrencesOfString:@"\t" withString:@"\\t"];
    out = [out stringByReplacingOccurrencesOfString:@"\n" withString:@"\\n"];
    return [out stringByReplacingOccurrencesOfString:@"\r" withString:@"\\r"];
}

static void Line(char kind, int a, int b, int caret, int flag, int key, int modifiers, NSString* text)
{
    if (s_Lines == nil) s_Lines = [NSMutableArray array];
    [s_Lines addObject:[NSString stringWithFormat:@"%c\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%@", kind, s_Revision, a, b, caret, flag, key, modifiers, Escape(text)]];
}

static NSString* Text(const char* utf8) { return utf8 != NULL ? [NSString stringWithUTF8String:utf8] : @""; }

@interface LetteraMirrorTextView : UITextView <UITextViewDelegate>
@property (nonatomic) BOOL actionReturn;
@end

@implementation LetteraMirrorTextView

- (BOOL)textView:(UITextView*)textView shouldChangeTextInRange:(NSRange)range replacementText:(NSString*)text
{
    if (s_Pushing) return YES;
    if (self.actionReturn && [text isEqualToString:@"\n"])
    {
        Line('V', -1, -1, -1, 0, 0, 0, @"return");
        return NO;
    }
    if (self.markedTextRange == nil)
    {
        s_Reported = [s_Reported stringByReplacingCharactersInRange:NSIntersectionRange(range, NSMakeRange(0, s_Reported.length)) withString:text];
        Line('R', (int)range.location, (int)(range.location + range.length), -1, 0, 0, 0, text);
    }
    return YES;
}

- (void)textViewDidChange:(UITextView*)textView
{
    if (s_Pushing) return;
    UITextRange* marked = self.markedTextRange;
    if (marked != nil)
    {
        NSInteger start = [self offsetFromPosition:self.beginningOfDocument toPosition:marked.start];
        NSInteger end = [self offsetFromPosition:self.beginningOfDocument toPosition:marked.end];
        NSString* composing = [self textInRange:marked];
        Line('C', (int)start, (int)end, (int)(self.selectedRange.location - start), 0, 0, 0, composing);
        return;
    }
    if (![self.text isEqualToString:s_Reported])
    {
        s_Reported = [self.text copy];
        Line('W', -1, -1, (int)self.selectedRange.location, 0, 0, 0, self.text);
    }
}

- (void)textViewDidChangeSelection:(UITextView*)textView
{
    if (s_Pushing || self.markedTextRange != nil) return;
    NSRange range = self.selectedRange;
    Line('S', (int)range.location, (int)(range.location + range.length), -1, 0, 0, 0, @"");
}

- (void)copy:(id)sender { Line('V', -1, -1, -1, 0, 0, 0, @"copy"); }
- (void)cut:(id)sender { Line('V', -1, -1, -1, 0, 0, 0, @"cut"); }
- (void)paste:(id)sender { Line('V', -1, -1, -1, 0, 0, 0, @"paste"); }
- (void)selectAll:(id)sender { Line('V', -1, -1, -1, 0, 0, 0, @"selectAll"); }
- (void)delete:(id)sender { Line('K', -1, -1, -1, 0, 2, 0, @""); }

- (void)pressesBegan:(NSSet<UIPress*>*)presses withEvent:(UIPressesEvent*)event
{
    for (UIPress* press in presses)
    {
        int key = 0;
        switch (press.key.keyCode)
        {
            case UIKeyboardHIDUsageKeyboardLeftArrow: key = 7; break;
            case UIKeyboardHIDUsageKeyboardRightArrow: key = 8; break;
            case UIKeyboardHIDUsageKeyboardUpArrow: key = 9; break;
            case UIKeyboardHIDUsageKeyboardDownArrow: key = 10; break;
            default: break;
        }
        if (key != 0)
        {
            UIKeyModifierFlags flags = press.key.modifierFlags;
            int modifiers = ((flags & UIKeyModifierShift) ? 1 : 0) | ((flags & UIKeyModifierControl) ? 2 : 0) | ((flags & UIKeyModifierAlternate) ? 4 : 0) | ((flags & UIKeyModifierCommand) ? 8 : 0);
            Line('K', -1, -1, -1, 0, key, modifiers, @"");
            return;
        }
    }
    [super pressesBegan:presses withEvent:event];
}
@end

static LetteraMirrorTextView* s_View;
static id s_KeyboardObserver;

extern "C" int LetteraIos_Available(void) { return UnityGetGLView() != nil ? 1 : 0; }

extern "C" int LetteraIos_ReduceMotion(void) { return UIAccessibilityIsReduceMotionEnabled() ? 1 : 0; }

static UITextAutocapitalizationType Caps(int value)
{
    switch (value)
    {
        case 1: return UITextAutocapitalizationTypeNone;
        case 3: return UITextAutocapitalizationTypeWords;
        case 4: return UITextAutocapitalizationTypeAllCharacters;
        default: return UITextAutocapitalizationTypeSentences;
    }
}

static UIKeyboardType KeyboardKind(int kind)
{
    switch (kind)
    {
        case 1: return UIKeyboardTypeASCIICapable;
        case 2: return UIKeyboardTypeURL;
        case 3: return UIKeyboardTypeNumberPad;
        case 4: return UIKeyboardTypeDecimalPad;
        case 5: return UIKeyboardTypePhonePad;
        case 6: return UIKeyboardTypeNamePhonePad;
        case 7: return UIKeyboardTypeEmailAddress;
        case 8: return UIKeyboardTypeWebSearch;
        default: return UIKeyboardTypeDefault;
    }
}

static UIReturnKeyType ReturnKind(int key)
{
    switch (key)
    {
        case 1: return UIReturnKeyDone;
        case 2: return UIReturnKeyGo;
        case 3: return UIReturnKeyNext;
        case 4: return UIReturnKeySearch;
        case 5: return UIReturnKeySend;
        default: return UIReturnKeyDefault;
    }
}

static UITextContentType ContentKind(NSString* hint)
{
    if ([hint isEqualToString:@"username"]) return UITextContentTypeUsername;
    if ([hint isEqualToString:@"current-password"]) return UITextContentTypePassword;
    if ([hint isEqualToString:@"new-password"]) return UITextContentTypeNewPassword;
    if ([hint isEqualToString:@"one-time-code"]) return UITextContentTypeOneTimeCode;
    if ([hint isEqualToString:@"email"]) return UITextContentTypeEmailAddress;
    if ([hint isEqualToString:@"name"]) return UITextContentTypeName;
    if ([hint isEqualToString:@"given-name"]) return UITextContentTypeGivenName;
    if ([hint isEqualToString:@"family-name"]) return UITextContentTypeFamilyName;
    if ([hint isEqualToString:@"tel"]) return UITextContentTypeTelephoneNumber;
    if ([hint isEqualToString:@"street-address"]) return UITextContentTypeFullStreetAddress;
    if ([hint isEqualToString:@"postal-code"]) return UITextContentTypePostalCode;
    if ([hint isEqualToString:@"country"]) return UITextContentTypeCountryName;
    if ([hint isEqualToString:@"cc-number"]) return UITextContentTypeCreditCardNumber;
    return nil;
}

extern "C" void LetteraIos_Open(const char* text, int start, int end, int revision, int multiline, int secure, int kind, int returnKey, int autocapitalize, int autocorrect, int spellCheck, int appearance, int smartQuotes, int smartDashes, int smartInsert, int returnWhenEmpty, const char* autofill)
{
    s_Revision = revision;
    s_Reported = Text(text);
    if (s_View == nil)
    {
        s_View = [[LetteraMirrorTextView alloc] initWithFrame:CGRectMake(0, 0, 1, 1)];
        s_View.alpha = 0;
        s_View.delegate = s_View;
        [UnityGetGLView() addSubview:s_View];
        s_KeyboardObserver = [[NSNotificationCenter defaultCenter] addObserverForName:UIKeyboardWillChangeFrameNotification object:nil queue:nil usingBlock:^(NSNotification* note) {
            CGRect frame = [note.userInfo[UIKeyboardFrameEndUserInfoKey] CGRectValue];
            CGFloat scale = UnityGetGLView().contentScaleFactor;
            Line('F', (int)(frame.origin.x * scale), (int)(frame.origin.y * scale), (int)(frame.size.width * scale), (int)(frame.size.height * scale), 0, 0, @"");
        }];
    }
    s_View.keyboardType = KeyboardKind(kind);
    s_View.returnKeyType = ReturnKind(returnKey);
    s_View.autocapitalizationType = Caps(autocapitalize);
    s_View.autocorrectionType = autocorrect ? UITextAutocorrectionTypeYes : UITextAutocorrectionTypeNo;
    s_View.spellCheckingType = spellCheck == 1 ? UITextSpellCheckingTypeYes : spellCheck == 2 ? UITextSpellCheckingTypeNo : UITextSpellCheckingTypeDefault;
    s_View.keyboardAppearance = appearance == 1 ? UIKeyboardAppearanceLight : appearance == 2 ? UIKeyboardAppearanceDark : UIKeyboardAppearanceDefault;
    s_View.smartQuotesType = smartQuotes == 1 ? UITextSmartQuotesTypeYes : smartQuotes == 2 ? UITextSmartQuotesTypeNo : UITextSmartQuotesTypeDefault;
    s_View.smartDashesType = smartDashes == 1 ? UITextSmartDashesTypeYes : smartDashes == 2 ? UITextSmartDashesTypeNo : UITextSmartDashesTypeDefault;
    s_View.smartInsertDeleteType = smartInsert == 1 ? UITextSmartInsertDeleteTypeYes : smartInsert == 2 ? UITextSmartInsertDeleteTypeNo : UITextSmartInsertDeleteTypeDefault;
    s_View.enablesReturnKeyAutomatically = returnWhenEmpty == 1;
    s_View.secureTextEntry = secure != 0;
    s_View.textContentType = ContentKind(Text(autofill));
    s_View.actionReturn = multiline == 0 || returnKey != 0;
    s_Pushing = YES;
    s_View.text = s_Reported;
    s_View.selectedRange = NSMakeRange((NSUInteger)start, (NSUInteger)(end - start));
    s_Pushing = NO;
    [s_View becomeFirstResponder];
}

extern "C" void LetteraIos_Push(const char* text, int start, int end, int revision)
{
    if (s_View == nil) return;
    s_Revision = revision;
    s_Reported = Text(text);
    s_Pushing = YES;
    if (![s_View.text isEqualToString:s_Reported]) s_View.text = s_Reported;
    s_View.selectedRange = NSMakeRange((NSUInteger)start, (NSUInteger)(end - start));
    s_Pushing = NO;
}

extern "C" void LetteraIos_Place(float x, float y, float width, float height)
{
    if (s_View == nil) return;
    CGFloat scale = UnityGetGLView().contentScaleFactor;
    s_View.frame = CGRectMake(x / scale, y / scale, 1, MAX(height / scale, 1));
}

extern "C" void LetteraIos_Close(void)
{
    if (s_View == nil) return;
    [s_View resignFirstResponder];
    [s_View removeFromSuperview];
    s_View = nil;
    if (s_KeyboardObserver != nil) [[NSNotificationCenter defaultCenter] removeObserver:s_KeyboardObserver];
    s_KeyboardObserver = nil;
    [s_Lines removeAllObjects];
}

extern "C" int LetteraIos_Next(char* buffer, int size)
{
    if (s_Lines.count == 0) return 0;
    const char* utf8 = s_Lines[0].UTF8String;
    int length = (int)strlen(utf8);
    if (length > size) return -length;
    memcpy(buffer, utf8, (size_t)length);
    [s_Lines removeObjectAtIndex:0];
    return length;
}

// ------------------------------------------------------------- pasteboard

extern "C" int LetteraIos_ClipWrite(const char* plain, const char* html, const char* own)
{
    NSMutableDictionary<NSString*, id>* item = [NSMutableDictionary dictionary];
    item[@"public.utf8-plain-text"] = Text(plain);
    if (html != NULL && html[0] != 0) item[@"public.html"] = [Text(html) dataUsingEncoding:NSUTF8StringEncoding];
    if (own != NULL && own[0] != 0) item[@"com.firstsymbol.lettera.text+json"] = [Text(own) dataUsingEncoding:NSUTF8StringEncoding];
    [UIPasteboard generalPasteboard].items = @[item];
    return 1;
}

// These ask what is there with no read, so the system's notice stays away. 0 unknown, 1 empty, 2 text, 3 rich, 4 image.
extern "C" int LetteraIos_ClipProbe(void)
{
    UIPasteboard* board = [UIPasteboard generalPasteboard];
    if (board.hasStrings) return 2;
    if (board.hasImages) return 4;
    if (board.hasURLs) return 2;
    return 1;
}

// Only ever called for an explicit paste. 0 plain, 1 HTML, 2 Lettera's own. Negative is the room needed.
extern "C" int LetteraIos_ClipRead(int which, char* buffer, int size)
{
    UIPasteboard* board = [UIPasteboard generalPasteboard];
    NSData* data = nil;
    if (which == 0) data = [board.string dataUsingEncoding:NSUTF8StringEncoding];
    else data = [board dataForPasteboardType:which == 1 ? @"public.html" : @"com.firstsymbol.lettera.text+json"];
    if (data == nil) return 0;
    if ((int)data.length > size) return -(int)data.length;
    memcpy(buffer, data.bytes, data.length);
    return (int)data.length;
}

// ------------------------------------------------------------- the system's menu

static NSMutableArray<NSNumber*>* s_MenuChoices;
static NSArray<UIAction*>* s_MenuActions;
static CGRect s_MenuRect;

API_AVAILABLE(ios(16.0))
@interface LetteraMenuDelegate : NSObject <UIEditMenuInteractionDelegate>
@end

@implementation LetteraMenuDelegate
- (UIMenu*)editMenuInteraction:(UIEditMenuInteraction*)interaction menuForConfiguration:(UIEditMenuConfiguration*)configuration suggestedActions:(NSArray<UIMenuElement*>*)suggestedActions
{
    return [UIMenu menuWithChildren:s_MenuActions];
}

- (CGRect)editMenuInteraction:(UIEditMenuInteraction*)interaction targetRectForConfiguration:(UIEditMenuConfiguration*)configuration
{
    return s_MenuRect;
}

- (void)editMenuInteraction:(UIEditMenuInteraction*)interaction willDismissMenuForConfiguration:(UIEditMenuConfiguration*)configuration animator:(id<UIEditMenuInteractionAnimating>)animator
{
    [s_MenuChoices addObject:@(-1)];
}
@end

static id s_MenuDelegate;
static id s_MenuInteraction;

extern "C" int LetteraIos_MenuSupported(void)
{
    if (@available(iOS 16.0, *)) return 1;
    return 0;
}

extern "C" void LetteraIos_ShowMenu(float x, float y, float width, float height, const char* titles, const char* enabled)
{
    if (@available(iOS 16.0, *))
    {
        if (s_MenuChoices == nil) s_MenuChoices = [NSMutableArray array];
        UIView* view = UnityGetGLView();
        CGFloat scale = view.contentScaleFactor;
        s_MenuRect = CGRectMake(x / scale, y / scale, width / scale, height / scale);
        NSArray<NSString*>* names = [Text(titles) componentsSeparatedByString:@"\t"];
        NSString* flags = Text(enabled);
        NSMutableArray<UIAction*>* actions = [NSMutableArray array];
        for (NSUInteger i = 0; i < names.count; i++)
        {
            NSInteger index = (NSInteger)i;
            UIAction* action = [UIAction actionWithTitle:names[i] image:nil identifier:nil handler:^(__kindof UIAction* unused) { [s_MenuChoices addObject:@(index)]; }];
            if (i < flags.length && [flags characterAtIndex:i] == '0') action.attributes = UIMenuElementAttributesDisabled;
            [actions addObject:action];
        }
        s_MenuActions = actions;
        if (s_MenuInteraction == nil)
        {
            s_MenuDelegate = [LetteraMenuDelegate new];
            s_MenuInteraction = [[UIEditMenuInteraction alloc] initWithDelegate:s_MenuDelegate];
            [view addInteraction:s_MenuInteraction];
        }
        CGPoint source = CGPointMake(CGRectGetMidX(s_MenuRect), CGRectGetMinY(s_MenuRect));
        UIEditMenuConfiguration* configuration = [UIEditMenuConfiguration configurationWithIdentifier:nil sourcePoint:source];
        [(UIEditMenuInteraction*)s_MenuInteraction presentEditMenuWithConfiguration:configuration];
    }
}

extern "C" void LetteraIos_HideMenu(void)
{
    if (@available(iOS 16.0, *))
    {
        if (s_MenuInteraction != nil) [(UIEditMenuInteraction*)s_MenuInteraction dismissMenu];
    }
}

extern "C" int LetteraIos_NextMenuChoice(void)
{
    if (s_MenuChoices.count == 0) return -2;
    int choice = s_MenuChoices[0].intValue;
    [s_MenuChoices removeObjectAtIndex:0];
    return choice;
}

// ------------------------------------------------------------- the system's loupe

static id s_Loupe;

extern "C" int LetteraIos_LoupeSupported(void)
{
    if (@available(iOS 17.0, *)) return 1;
    return 0;
}

extern "C" void LetteraIos_ShowLoupe(float x, float y)
{
    if (@available(iOS 17.0, *))
    {
        UIView* view = UnityGetGLView();
        CGFloat scale = view.contentScaleFactor;
        CGPoint point = CGPointMake(x / scale, y / scale);
        if (s_Loupe == nil) s_Loupe = [UITextLoupeSession beginLoupeSessionAtPoint:point fromSelectionWidgetView:nil inView:view];
        else [(UITextLoupeSession*)s_Loupe moveToPoint:point withCaretRect:CGRectMake(point.x, point.y, 2, 20) trackingCaret:NO];
    }
}

extern "C" void LetteraIos_HideLoupe(void)
{
    if (@available(iOS 17.0, *))
    {
        if (s_Loupe != nil) [(UITextLoupeSession*)s_Loupe invalidate];
        s_Loupe = nil;
    }
}
