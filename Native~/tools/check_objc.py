"""Syntax check of the package's Objective-C++ with no Apple SDK, against hand-written declarations in apple_stubs.

It proves syntax and self-consistency, and that a planted error fails. A wrong selector still passes: only Xcode and a device can tell.

usage: python check_objc.py [zig.exe]
"""
import glob
import os
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ZIG = sys.argv[1] if len(sys.argv) > 1 else r"D:\General\Other\Unity\Tools\zig\zig.exe"
STUBS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "apple_stubs")
CHECKED = ["LetteraPlatformIOS.mm", "LetteraPlatformIOSStub.mm"]
# The font lookup also builds for tvOS and visionOS. zig has no headers for those, so it goes through zig_ios.py's swapped triple.
FONTS = "LetteraFontsIOS.mm"
FONT_TRIPLES = ["arm64-apple-ios13.0.0", "arm64-apple-tvos13.0.0", "arm64-apple-tvos13.0.0-simulator", "arm64-apple-xros1.0.0", "arm64-apple-xros1.0.0-simulator"]


def check(path):
    with tempfile.TemporaryDirectory() as out:
        result = subprocess.run([ZIG, "c++", "-target", "aarch64-macos", "-c", "-o", os.path.join(out, "check.o"), "-x", "objective-c++", "-fobjc-arc", "-Wno-everything", f"-I{STUBS}", path], capture_output=True, text=True)
    return result.returncode, result.stderr


def check_triple(path, triple):
    with tempfile.TemporaryDirectory() as out:
        result = subprocess.run([sys.executable, "-I", os.path.join(os.path.dirname(os.path.abspath(__file__)), "zig_ios.py"), ZIG, triple, os.path.join(out, "check.o"),
                                 "-x", "objective-c++", "-fobjc-arc", "-Wno-everything", f"-I{STUBS}", path], capture_output=True, text=True)
    return result.returncode, result.stderr


failed = 0
for name in CHECKED:
    path = os.path.join(ROOT, "Plugins", "iOS", name)
    code, errors = check(path)
    print(name, "ok" if code == 0 else "FAILED")
    if code != 0:
        print(errors[:3000])
        failed += 1

for triple in FONT_TRIPLES:
    code, errors = check_triple(os.path.join(ROOT, "Plugins", "iOS", FONTS), triple)
    print(FONTS, triple, "ok" if code == 0 else "FAILED")
    if code != 0:
        print(errors[:3000])
        failed += 1

with tempfile.TemporaryDirectory() as folder:
    planted = os.path.join(folder, "planted.mm")
    with open(planted, "w", encoding="utf-8") as f:
        f.write(open(os.path.join(ROOT, "Plugins", "iOS", CHECKED[0]), encoding="utf-8").read().replace("return choice;", "return choice"))
    code, _ = check(planted)
    print("planted error", "caught" if code != 0 else "NOT CAUGHT")
    failed += code == 0
    planted = os.path.join(folder, "planted_fonts.mm")
    with open(planted, "w", encoding="utf-8") as f:
        f.write(open(os.path.join(ROOT, "Plugins", "iOS", FONTS), encoding="utf-8").read().replace("CFRelease(base);", "CFRelease(base)"))
    code, _ = check_triple(planted, FONT_TRIPLES[1])
    print("planted font error", "caught" if code != 0 else "NOT CAUGHT")
    failed += code == 0

sys.exit(1 if failed else 0)
