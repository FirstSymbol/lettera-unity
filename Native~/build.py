"""Builds the Lettera native plugin for every platform into ../Plugins.

usage: python build.py [--zig PATH] [--ndk PATH] [target ...]

Targets: win-x64 win-arm64 linux-x64 linux-arm64 macos ios webgl android
Default is every target whose toolchain is available. Android needs the NDK
that Unity's Android module installs (or --ndk). The compilers are zig and,
for Android, the NDK's clang.
"""
import argparse
import concurrent.futures
import glob
import os
import shutil
import struct
import subprocess
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
PLUGINS = os.path.normpath(os.path.join(ROOT, "..", "Plugins"))
OUT = os.path.join(ROOT, "out")
HB = os.path.join("third_party", "harfbuzz", "src")
FLAGS = ["-O2", "-std=c++17", "-fno-exceptions", "-fno-rtti", "-fvisibility=hidden",
         "-fvisibility-inlines-hidden", "-DHB_EXTERN=extern", f"-I{HB}"]
# Static targets link into a player that holds its own HarfBuzz: see tools/gen_hb_prefix.py.
STATIC = ["-include", os.path.join("src", "lt_hb_prefix.h")]
POSIX = ["-DHAVE_MMAP", "-DHAVE_SYS_MMAN_H", "-DHAVE_UNISTD_H"]  # lets hb_blob map font files
SOURCES = [os.path.join(HB, "harfbuzz.cc"), os.path.join("src", "lettera_native.cpp")]
DEFAULT_ZIG = r"D:\General\Other\Unity\Tools\zig\zig.exe"
DEFAULT_EDITOR = r"D:\General\Other\Unity\Editors\6000.4.8f1"


def run(args):
    print(">", " ".join(os.path.basename(a) if i == 0 else a for i, a in enumerate(args)), flush=True)
    subprocess.run(args, cwd=ROOT, check=True)


def place(built, *relative):
    target = os.path.join(PLUGINS, *relative)
    if os.path.getsize(built) < 500_000:
        raise RuntimeError(f"{built} is only {os.path.getsize(built)} bytes; HarfBuzz alone is over 1 MB")
    os.makedirs(os.path.dirname(target), exist_ok=True)
    shutil.copyfile(built, target)
    print("  ->", os.path.relpath(target, PLUGINS), os.path.getsize(target), flush=True)


def shared(zig, triple, name, extra=()):
    out_dir = os.path.join(OUT, triple)
    os.makedirs(out_dir, exist_ok=True)
    output = os.path.join(out_dir, name)
    run([zig, "c++", "-target", triple, "-shared", "-fPIC", *FLAGS, *extra, *SOURCES, "-o", output])
    return output


def fat(slices, output):
    """Merges thin Mach-O files or static archives into one universal file (what lipo does)."""
    cpu = {"x86_64": (0x01000007, 3), "arm64": (0x0100000C, 0)}
    align = 14
    offset = 8 + 20 * len(slices)
    header = struct.pack(">II", 0xCAFEBABE, len(slices))
    body = b""
    for arch, path in slices:
        with open(path, "rb") as f:
            data = f.read()
        padding = (-(offset + len(body))) % (1 << align)
        body += b"\0" * padding
        header += struct.pack(">IIIII", cpu[arch][0], cpu[arch][1], offset + len(body), len(data), align)
        body += data
    with open(output, "wb") as f:
        f.write(header + body)


def build_win(zig, triple, folder):
    place(shared(zig, triple, "lettera_native.dll", ["-s"]), "Windows", folder, "lettera_native.dll")


def build_linux(zig, triple, folder):
    place(shared(zig, triple, "liblettera_native.so", ["-s", *POSIX]), "Linux", folder, "liblettera_native.so")


def build_macos(zig):
    x64 = shared(zig, "x86_64-macos.11.0", "liblettera_native.dylib", POSIX)
    arm = shared(zig, "aarch64-macos.11.0", "liblettera_native.dylib", POSIX)
    output = os.path.join(OUT, "liblettera_native.dylib")
    fat([("x86_64", x64), ("arm64", arm)], output)
    place(output, "macOS", "liblettera_native.dylib")


def ios_archive(zig, triple, arch):
    out_dir = os.path.join(OUT, triple)
    os.makedirs(out_dir, exist_ok=True)
    objects = []
    for source in SOURCES:
        obj = os.path.join(out_dir, os.path.splitext(os.path.basename(source))[0] + ".o")
        run([sys.executable, "-I", os.path.join("tools", "zig_ios.py"), zig, triple, obj, *FLAGS, *POSIX, *STATIC, source])
        objects.append(obj)
    archive = os.path.join(out_dir, "liblettera_native.a")
    if os.path.exists(archive):
        os.remove(archive)
    run([zig, "ar", "rcs", archive, *objects])
    return arch, archive


XCFRAMEWORK_PLIST = """<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>AvailableLibraries</key>
	<array>
		<dict>
			<key>LibraryIdentifier</key>
			<string>ios-arm64</string>
			<key>LibraryPath</key>
			<string>liblettera_native.a</string>
			<key>SupportedArchitectures</key>
			<array>
				<string>arm64</string>
			</array>
			<key>SupportedPlatform</key>
			<string>ios</string>
		</dict>
		<dict>
			<key>LibraryIdentifier</key>
			<string>ios-arm64_x86_64-simulator</string>
			<key>LibraryPath</key>
			<string>liblettera_native.a</string>
			<key>SupportedArchitectures</key>
			<array>
				<string>arm64</string>
				<string>x86_64</string>
			</array>
			<key>SupportedPlatform</key>
			<string>ios</string>
			<key>SupportedPlatformVariant</key>
			<string>simulator</string>
		</dict>
	</array>
	<key>CFBundlePackageType</key>
	<string>XFWK</string>
	<key>XCFrameworkFormatVersion</key>
	<string>1.0</string>
</dict>
</plist>
"""


def build_ios(zig):
    """An XCFramework, because device and simulator arm64 cannot share one universal library."""
    _, device = ios_archive(zig, "arm64-apple-ios13.0.0", "arm64")
    place(device, "iOS", "lettera_native.xcframework", "ios-arm64", "liblettera_native.a")
    simulator = [ios_archive(zig, "arm64-apple-ios13.0.0-simulator", "arm64"),
                 ios_archive(zig, "x86_64-apple-ios13.0.0-simulator", "x86_64")]
    output = os.path.join(OUT, "liblettera_native_sim.a")
    fat(simulator, output)
    place(output, "iOS", "lettera_native.xcframework", "ios-arm64_x86_64-simulator", "liblettera_native.a")
    with open(os.path.join(PLUGINS, "iOS", "lettera_native.xcframework", "Info.plist"), "w", encoding="utf-8", newline="\n") as f:
        f.write(XCFRAMEWORK_PLIST)


def build_webgl():
    """A static archive from the Emscripten in Unity's Web module, so its LLVM matches the player's."""
    root = os.path.join(DEFAULT_EDITOR, "Editor", "Data", "PlaybackEngines", "WebGLSupport", "BuildTools", "Emscripten")
    env = dict(os.environ)
    env["EMSDK_PYTHON"] = os.path.join(root, "python", "python.exe")
    env["EM_CONFIG"] = os.path.join(OUT, "emscripten_config")
    env["EM_CACHE"] = os.path.join(OUT, "emscripten_cache")
    os.makedirs(OUT, exist_ok=True)
    with open(env["EM_CONFIG"], "w", encoding="utf-8") as f:
        for key, value in (("LLVM_ROOT", "llvm"), ("BINARYEN_ROOT", "binaryen"), ("NODE_JS", os.path.join("node", "node.exe"))):
            f.write(f"{key} = {os.path.join(root, value)!r}\n")
    emcc = os.path.join(root, "emscripten", "em++.bat")
    emar = os.path.join(root, "emscripten", "emar.bat")
    out_dir = os.path.join(OUT, "webgl")
    os.makedirs(out_dir, exist_ok=True)
    objects = []
    for source in SOURCES:
        obj = os.path.join(out_dir, os.path.splitext(os.path.basename(source))[0] + ".o")
        print(">", "em++", source, flush=True)
        subprocess.run([emcc, "-c", "-Os", "-std=c++17", "-fno-exceptions", "-fno-rtti", "-fvisibility=hidden",
                        "-DHB_EXTERN=extern", "-DHB_NO_MT", f"-I{HB}", *STATIC, source, "-o", obj], cwd=ROOT, check=True, env=env)
        objects.append(obj)
    archive = os.path.join(out_dir, "liblettera_native.a")
    if os.path.exists(archive):
        os.remove(archive)
    subprocess.run([emar, "rcs", archive, *objects], cwd=ROOT, check=True, env=env)
    place(archive, "WebGL", "liblettera_native.a")


def find_ndk(explicit):
    if explicit:
        return explicit
    env = os.environ.get("ANDROID_NDK_ROOT") or os.environ.get("ANDROID_NDK_HOME")
    if env:
        return env
    candidates = glob.glob(os.path.join(DEFAULT_EDITOR, "Editor", "Data", "PlaybackEngines", "AndroidPlayer", "NDK*"))
    return candidates[0] if candidates else None


def build_android(ndk):
    toolchain = glob.glob(os.path.join(ndk, "toolchains", "llvm", "prebuilt", "*", "bin"))[0]
    clang = os.path.join(toolchain, "clang++.cmd" if os.name == "nt" else "clang++")
    if not os.path.exists(clang):
        clang = os.path.join(toolchain, "clang++.exe")
    for triple, abi in (("aarch64-linux-android24", "arm64-v8a"),
                        ("armv7a-linux-androideabi24", "armeabi-v7a"),
                        ("x86_64-linux-android24", "x86_64")):
        out_dir = os.path.join(OUT, triple)
        os.makedirs(out_dir, exist_ok=True)
        output = os.path.join(out_dir, "liblettera_native.so")
        # 16 KB pages: Google Play requires it for new Android 15+ builds.
        run([clang, f"--target={triple}", "-shared", "-fPIC", *FLAGS, *POSIX, "-static-libstdc++", "-s",
             "-Wl,-z,max-page-size=16384", *SOURCES, "-o", output])
        place(output, "Android", abi, "liblettera_native.so")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--zig", default=DEFAULT_ZIG)
    parser.add_argument("--ndk")
    parser.add_argument("targets", nargs="*")
    args = parser.parse_args()
    ndk = find_ndk(args.ndk)
    jobs = {
        "win-x64": lambda: build_win(args.zig, "x86_64-windows-gnu", "x86_64"),
        "win-arm64": lambda: build_win(args.zig, "aarch64-windows-gnu", "ARM64"),
        "linux-x64": lambda: build_linux(args.zig, "x86_64-linux-gnu.2.17", "x86_64"),
        "linux-arm64": lambda: build_linux(args.zig, "aarch64-linux-gnu.2.17", "ARM64"),
        "macos": lambda: build_macos(args.zig),
        "ios": lambda: build_ios(args.zig),
        "webgl": build_webgl,
    }
    if ndk:
        jobs["android"] = lambda: build_android(ndk)
    else:
        print("android skipped: no NDK found (install Unity's Android module or pass --ndk)")
    targets = args.targets or list(jobs)
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        futures = {name: pool.submit(jobs[name]) for name in targets}
    failed = [name for name, future in futures.items() if future.exception()]
    for name in failed:
        print("FAILED", name, futures[name].exception())
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
