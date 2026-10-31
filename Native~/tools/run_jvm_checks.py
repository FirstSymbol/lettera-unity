"""Compiles the part of the Android layer that needs no Android, with Unity's OpenJDK, and runs its checks.

usage: python run_jvm_checks.py [editor folder]
"""
import glob
import os
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
EDITOR = sys.argv[1] if len(sys.argv) > 1 else r"D:\General\Other\Unity\Editors\6000.4.8f1"
SOURCE = os.path.join(ROOT, "Plugins", "Android", "LetteraPlatform.androidlib", "src", "com", "firstsymbol", "lettera", "LetteraMirrorCore.java")


def tool(name):
    found = glob.glob(os.path.join(EDITOR, "Editor", "Data", "PlaybackEngines", "AndroidPlayer", "OpenJDK", "bin", name + ".exe"))
    if not found:
        raise SystemExit("no " + name + ": install Unity's Android module with its OpenJDK")
    return found[0]


with tempfile.TemporaryDirectory() as out:
    subprocess.run([tool("javac"), "-encoding", "UTF-8", "-d", out, SOURCE], check=True)
    print(subprocess.run([tool("java"), "-cp", out, "com.firstsymbol.lettera.LetteraMirrorCore"], check=True, capture_output=True, text=True).stdout.strip())
