// The same functions as LetteraPlatformIOS.mm, saying "not available". A default iOS build compiles this one, so that code nothing here could compile cannot fail a project's Xcode build.
extern "C" {
int LetteraIos_Available(void) { return 0; }
int LetteraIos_ReduceMotion(void) { return 0; }
void LetteraIos_Open(const char* text, int start, int end, int revision, int multiline, int secure, int kind, int returnKey, int autocapitalize, int autocorrect, int spellCheck, int appearance, int smartQuotes, int smartDashes, int smartInsert, int returnWhenEmpty, const char* autofill) { }
void LetteraIos_Push(const char* text, int start, int end, int revision) { }
void LetteraIos_Place(float x, float y, float width, float height) { }
void LetteraIos_Close(void) { }
int LetteraIos_Next(char* buffer, int size) { return 0; }
int LetteraIos_ClipWrite(const char* plain, const char* html, const char* own) { return 0; }
int LetteraIos_ClipProbe(void) { return 0; }
int LetteraIos_ClipRead(int which, char* buffer, int size) { return 0; }
int LetteraIos_MenuSupported(void) { return 0; }
void LetteraIos_ShowMenu(float x, float y, float width, float height, const char* titles, const char* enabled) { }
void LetteraIos_HideMenu(void) { }
int LetteraIos_NextMenuChoice(void) { return -2; }
int LetteraIos_LoupeSupported(void) { return 0; }
void LetteraIos_ShowLoupe(float x, float y) { }
void LetteraIos_HideLoupe(void) { }
}
