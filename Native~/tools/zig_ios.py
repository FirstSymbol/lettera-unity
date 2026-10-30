"""Compile one C++ file into an iOS object with zig.

zig ships Darwin libc headers but refuses iOS targets without an SDK. The
headers are shared by macOS and iOS, so this asks zig for its macOS clang -cc1
command line, swaps the target triple and runs it through `zig clang`.

usage: python zig_ios.py <zig.exe> <triple> <output.o> <compiler args...>
       triple e.g. arm64-apple-ios13.0.0, arm64-apple-ios13.0.0-simulator,
       x86_64-apple-ios13.0.0-simulator
"""
import shlex
import subprocess
import sys


def main():
    zig, triple, output, args = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4:]
    arch = "x86_64" if triple.startswith("x86_64") else "aarch64"
    probe = subprocess.run([zig, "c++", "-target", f"{arch}-macos.11.0", "-c", *args, "-o", output, "-###"],
                           capture_output=True, text=True)
    cc1 = next((line for line in probe.stderr.splitlines() if '"-cc1"' in line), None)
    if cc1 is None:
        sys.stderr.write(probe.stderr)
        sys.exit(1)
    argv = shlex.split(cc1.replace("\\", "/"))
    argv = argv[argv.index("-cc1"):]
    argv = [a for a in argv if not a.startswith(("-debug-info-kind", "-dwarf-version", "-debugger-tuning"))]
    for i, a in enumerate(argv):
        if a == "-triple":
            argv[i + 1] = triple
        elif a == "-o":
            argv[i + 1] = output  # zig's own command writes into its cache.
        elif a in ("-dependency-file", "-MT"):
            argv[i + 1] = output + ".d"
        elif a.startswith("-target-sdk-version="):
            argv[i] = "-target-sdk-version=13.0"
    result = subprocess.run([zig, "clang", *argv])
    sys.exit(result.returncode)


if __name__ == "__main__":
    main()
