# Platforms

What has run where, said plainly. Lettera ships a native library for every platform below, but a library that builds is not a feature that works: this page keeps the two apart.

[Русская версия](Platforms.ru.md) · Other guides: [Getting Started](GettingStarted.md), [Fonts](Fonts.md), [Fields](Fields.md), [Tools](Tools.md)

In the tables:

- **Built**: a player for that platform was made with no errors.
- **Run**: a player ran there, under a script that checks what a script can see: that text shows, that a field takes what a driver types, that a test passes. Run does not mean anyone looked.
- **Seen by a person**: someone looked at it running there, or used it by hand.

Where a cell says *no*, nobody knows yet. The [Device Check](#check-a-device-yourself) sample is how you find out on your own device.

## Windows

| | Built | Run | Seen by a person |
|---|---|---|---|
| The editor | — | Yes, with the whole test suite | Yes |
| Player, x64, Direct3D 11 and 12, Mono and IL2CPP | Yes | Yes: self-tests, every sample, the performance figures | Pictures of every sample were checked; not used by hand |
| Keys, mouse and an input method in a player | Yes | Through the player's real window with a stand-in for the input method; real keys were never sent | No |
| Clipboard with formats in a player | Yes | Yes, read and written by another process: plain text, HTML, a picture | Not with Word or Chrome |
| Formulas and 3D text in a player | Yes | Yes | Pictures checked |
| HDRP | Yes | Direct3D 12 only | Pictures checked |
| A project with only the Input System package | Yes | The self-test, with no input errors, in an earlier release; not repeated since | No |
| ARM64 | The library only | No | No |

## The web

| | Built | Run | Seen by a person |
|---|---|---|---|
| WebGL 2 in Chromium | Yes | Self-tests and every sample, also with managed stripping at High | Pictures checked |
| WebKit, Safari's engine, on a desktop, and its iPhone emulation | Yes | Self-tests and every sample | Pictures checked |
| Keys, paste, cut, focus and menu items by real clicks | Yes | In Chromium and WebKit, by a driver that reads the field after every step | No |
| Composition from an input method | Yes | In Chromium, through the browser's debugging protocol | No real input method |
| WebGPU | Yes | Chromium 156 on one desktop GPU | No |
| Safari on a real iPhone or iPad | — | No | No |
| Chrome on a real Android phone | — | No | No |
| Firefox | — | No | No |
| A link opening a new tab | — | No: the self-test uses a stand-in | No |

**Browsers expose no font files**, so on the web:

- A text with no font uses a bundled Noto Sans, which covers Latin, Cyrillic and Greek. **Other scripts show missing-glyph boxes until you assign a font** that covers them, through **Font** or **Font Stack**. Shaping then works as everywhere else.
- Formulas need a math font assigned too: the web has no system one. See [Formulas](Math.md).
- Emoji are drawn by the browser's own canvas, so they look like that device's emoji. Colour and alpha apply as usual.
- `LetteraSettings.WebGLFallbackFont` replaces the bundled font.
- Glyph memory is capped lower than elsewhere: the least recently used atlas page is recycled when the limit is reached.
- There are no threads, so work that other platforms spread across threads runs on the main thread.

## Android

| | Built | Run | Seen by a person |
|---|---|---|---|
| ARM64, ARMv7, x86-64; Activity and GameActivity | Yes, Gradle builds checked | No | No |
| System fonts, read from the device's font configuration | Yes | From sample configuration files in tests, not on a device | No |
| The system's text control, clipboard, menu and loupe | Yes, off by default | No | No |

Unity 6 builds no 32-bit x86 Android, so there is no library for it.

## iOS

| | Built | Run | Seen by a person |
|---|---|---|---|
| Xcode project, device and simulator | Exported; never compiled by Xcode | No | No |
| System fonts and emoji through CoreText | In the library | No | No |
| The system's text control, clipboard, menu and loupe | Off by default, and a build carries a stub unless you ask for the real code | No | No |

## macOS, Linux, tvOS, visionOS and UWP

| | Built | Run | Seen by a person |
|---|---|---|---|
| macOS player, universal | Yes | No | No |
| macOS clipboard with formats | Compiled only, off by default | No | No |
| Linux player | Yes | No | No |
| Linux ARM64 | The library only | No | No |
| tvOS and visionOS | The library only, for device and simulator; no player | No | No |
| UWP | Not built | No | No |

## Native routes and their gates

Fields, clipboards and menus can go through the platform's own controls. Each piece has a gate in **Project Settings > Lettera > Native Platform**, set to **Auto**, **On** or **Off**, and the page says why it is on or off for the platform you are building for. **Auto** takes the shipped default, and the default is off wherever a piece has not been seen to work:

| | Windows | Web | Android, iOS | macOS | Everything else |
|---|---|---|---|---|---|
| Text input | On in a player: composition through the input method | On: one hidden text box, composition inline | Off | Unity's own | Off |
| Clipboard with formats | On, editor and player | On | Off | Off | Off |
| Replica (`ReplicaFieldRule`) | Does nothing | On, when a field asks | Off | Does nothing | Off |
| System menu, loupe, paste control | Off | Off | Off | Off | Off |
| System preferences (reduced motion, a steady caret) | On in a player | On | Off | Off | Off |

- **iOS:** a build carries a stub in place of the real native code unless **Ios Native In Build** is on, so code that has never been compiled by Xcode cannot break your Xcode build.
- **A guard.** A native crash cannot be caught, so each use of a route leaves a mark that a clean finish removes. Two uses in a row that did not finish turn that route off for this version of the app, and the fallback takes over. A route that reports a failure is off for the rest of the run.
- From a script:

```csharp
if (!LetteraPlatform.IsActive(LetteraNativeFeature.Clipboard))
    Debug.Log(LetteraPlatform.Why(LetteraNativeFeature.Clipboard));

LetteraPlatform.Reported += (feature, message) => Debug.Log($"{feature}: {message}");
```

**Fonts packed beside the build:** on Windows, macOS and Linux a font is carried in a file of its own beside the build and mapped from disk the first time a text needs it, so an unused font costs nothing. On Android and iOS it stays inside the asset. The web has no such files.

## Check a device yourself

The **Device Check** sample walks through a device one screen at a time and ends with one block of text to send back.

1. Import it: **Window > Package Manager > Lettera > Samples > Device Check**.
2. **Lettera > Device Check > Build for Android**, **iOS**, **Web**, **Windows** or **macOS**. Every native route is turned on for that build only; your project's settings come back as they were. The build lands in `Builds/` in the project.
3. On the device, follow the screens: text and emoji, the keyboard (does it rise on the first tap?), typing with autocorrect and dictation, composition, keyboard kinds, the replica, the clipboard, the menu and loupe, fonts after a restart, colour glyphs, formulas, the system math font, 3D text and 500 solids, motion, scrolling a long document, handles, and a comparison build if you have one.
4. **Share** sends the result with no second device: the share sheet on Android, a mail elsewhere. Or copy the block.

## Known risks

- **Safari on an iPhone:** whether the keyboard rises on the first tap into a field. Lettera's web fields rely on a hidden text box focused inside the touch itself; emulation cannot prove that it works. This is the largest open question.
- **iOS native code** was compiled only against hand-written declarations of Apple's interfaces, which proves its syntax and not that the calls exist. Hence the stub by default.
- **macOS:** the clipboard's calls were compiled and never run, and their ARM64 marshalling is unchecked. Fonts packed beside the build are on by default there and have never run.
- **Android:** fonts were read from sample configuration files only. Firmware themes that replace system fonts while the app runs, fonts a vendor keeps elsewhere, and whether CJK text can still turn to empty boxes on such firmware were not checked. A change of the device's language or bold-text setting takes effect at the app's next start.
- **No real input method** (Japanese, Korean, Chinese) has been used on any platform.
- **COLRv1 colour fonts** are on everywhere and have not been seen on Android, iOS or macOS. On every platform 10 of the 200 glyphs of the test font draw nothing: sweep gradients HarfBuzz's raster code skips.
- **Canvas text with a stroke and a shadow** draws through a shader variant of its own, which has not run on Metal, Vulkan, desktop OpenGL or a real iOS device.
- **UWP:** the Windows library calls file mapping functions that a UWP app may call, but that the Store's certification kit lists as outside UWP. An app for the Store may need a library built for it.
- **Windows, Direct3D 12:** a player can crash as it quits, inside Unity's `D3D12Core` with no Lettera code on the stack, when a run adds shader states to the cache file left by an earlier run. Nothing is lost, since it happens after the player has shut down. It has not been seen with a fresh cache or on Direct3D 11.
- **Performance** was measured on Windows players only. Nothing on a phone, a Mac or Linux has been timed, and neither have the start-up thread a player uses to prepare the first text, the release of memory on a low-memory warning, or threads on a phone.
- **HDRP** was seen on Direct3D 12 only, not on Vulkan, Metal or a console.
