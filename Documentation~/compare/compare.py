"""Runs the comparison with TextMesh Pro and writes its tables. usage: python compare.py <unity.exe> <lettera package path> <work folder> [rounds]
Makes a project of its own, builds a Windows player, and runs each engine in turns, a cold start each. Nothing else should be running."""
import json, os, shutil, statistics, subprocess, sys

EDITOR, PACKAGE, WORK = sys.argv[1:4]
ROUNDS = int(sys.argv[4]) if len(sys.argv) > 4 else 5
HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.join(WORK, "Project")
OUT = os.path.join(WORK, "Out")
os.makedirs(OUT, exist_ok=True)
FONTS = os.path.join(PACKAGE, "Tests", "Fonts~")
ENGINES = ["lettera", "tmp-static", "tmp-dynamic"]
sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def build(path, dev):
    if not os.path.exists(os.path.join(PROJECT, "Packages", "manifest.json")):
        subprocess.call([EDITOR, "-batchmode", "-createProject", PROJECT, "-quit", "-logFile", os.path.join(OUT, "create.log")])
    with open(os.path.join(PROJECT, "Packages", "manifest.json"), "w", encoding="utf-8") as f:
        json.dump({"dependencies": {
            "com.firstsymbol.lettera": "file:" + PACKAGE.replace("\\", "/"),
            "com.unity.ugui": "2.0.0",
            "com.unity.modules.imageconversion": "1.0.0", "com.unity.modules.assetbundle": "1.0.0", "com.unity.modules.ui": "1.0.0",
            "com.unity.modules.uielements": "1.0.0", "com.unity.modules.imgui": "1.0.0", "com.unity.modules.jsonserialize": "1.0.0",
            "com.unity.modules.androidjni": "1.0.0", "com.unity.modules.screencapture": "1.0.0", "com.unity.modules.unitywebrequest": "1.0.0",
        }}, f, indent=2)
    lock = os.path.join(PROJECT, "Packages", "packages-lock.json")
    if os.path.exists(lock):
        os.remove(lock)
    for sub, name in (("Assets/Compare", "LetteraCompare.cs"), ("Assets/Editor", "LetteraCompareBuild.cs")):
        folder = os.path.join(PROJECT, sub)
        os.makedirs(folder, exist_ok=True)
        shutil.copyfile(os.path.join(HERE, name), os.path.join(folder, name))
    resources = os.path.join(PROJECT, "Assets", "Compare", "Resources")
    os.makedirs(resources, exist_ok=True)
    shutil.copyfile(os.path.join(FONTS, "NotoSans-Regular.ttf"), os.path.join(resources, "NotoSans-Regular.ttf"))
    env = dict(os.environ)
    env["LETTERA_DEV"] = "1" if dev else "0"
    log = os.path.join(OUT, "build_dev.log" if dev else "build.log")
    # Twice, the first time: essentials imported in one pass are compiled in the next.
    for attempt in range(2):
        code = subprocess.run([EDITOR, "-batchmode", "-quit", "-projectPath", PROJECT, "-buildTarget", "Win64", "-executeMethod", "LetteraCompareBuild.Run", "-out", path, "-logFile", log], env=env).returncode
        text = open(log, encoding="utf-8", errors="replace").read()
        if "LETTERA_COMPARE_BUILD Succeeded" in text:
            print("built", "development" if dev else "release", flush=True)
            return
        print("build attempt", attempt, "exit", code, flush=True)
    for line in text.splitlines():
        if "error CS" in line or "LETTERA_COMPARE_BUILD" in line:
            print("  ", line[:220])
    sys.exit(1)


def run(exe, engine, name, timeout=300):
    out = os.path.join(OUT, name + ".json")
    if os.path.exists(out):
        os.remove(out)
    process = subprocess.Popen([exe, "-screen-width", "1200", "-screen-height", "700", "-screen-fullscreen", "0", "-logFile", os.path.join(OUT, name + ".log"), "-compare", engine, "-fonts", FONTS, "-out", out])
    try:
        process.wait(timeout)
    except subprocess.TimeoutExpired:
        process.kill()
        print("  timed out", name, flush=True)
    if not os.path.exists(out):
        return None
    return json.load(open(out, encoding="utf-8"))


release = os.path.join(OUT, "Release", "Compare.exe")
dev = os.path.join(OUT, "Dev", "Compare.exe")
if not os.path.exists(release):
    build(release, False)
if not os.path.exists(dev):
    build(dev, True)

results = {e: {} for e in ENGINES}
notes = {e: {} for e in ENGINES}
meta = None
for r in range(ROUNDS):
    for engine in ENGINES:
        data = run(release, engine, f"{engine}_{r}")
        if data is None:
            print("no result", engine, r, flush=True)
            continue
        meta = meta or data
        for row in data["rows"]:
            results[engine].setdefault(row["row"], []).append(row["ms"])
            if row["note"]:
                notes[engine][row["row"]] = row["note"]
    print("round", r, "done", flush=True)

bytes_per = {e: {} for e in ENGINES}
for engine in ENGINES:
    data = run(dev, engine, f"{engine}_dev")
    if data:
        for row in data["rows"]:
            bytes_per[engine][row["row"]] = row["bytes"]
            if row["row"] == "control_gc_counter":
                print("gc control", engine, row["bytes"], row["note"], flush=True)

if meta is None:
    print("No engine gave a result: read the logs in", OUT)
    sys.exit(1)
summary = {"meta": {k: meta[k] for k in ("unity", "backend", "gpu", "device")}, "rounds": ROUNDS, "rows": {}}
for engine in ENGINES:
    for row, values in results[engine].items():
        summary["rows"].setdefault(row, {})[engine] = {"ms": statistics.median(values), "runs": len(values), "bytes": bytes_per[engine].get(row, -1), "note": notes[engine].get(row, "")}
json.dump(summary, open(os.path.join(WORK, "compare.json"), "w", encoding="utf-8"), indent=1)

lines = ["| Row | Lettera | TextMesh Pro, static atlas | TextMesh Pro, dynamic atlas |", "|---|---|---|---|"]
for row in summary["rows"]:
    cells = []
    for engine in ENGINES:
        cell = summary["rows"][row].get(engine)
        if not cell:
            cells.append("no result")
            continue
        text = f"{cell['ms']:.2f} ms"
        if cell["bytes"] >= 0 and row.startswith(("R3", "R4", "R5", "R6", "R7", "R9")):
            text += f", {cell['bytes']} B"
        if cell["bytes"] >= 0 and row.startswith("R15"):
            text = f"{cell['bytes'] / 1048576:.1f} MB"
        if "failed" in cell["note"]:
            text += " " + cell["note"].split(" frame")[0]
        cells.append(text)
    lines.append(f"| {row} | " + " | ".join(cells) + " |")
open(os.path.join(WORK, "compare_table.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("\n".join(lines))
for engine in ENGINES:
    process = subprocess.run([release, "-screen-width", "1200", "-screen-height", "700", "-screen-fullscreen", "0", "-logFile", os.path.join(OUT, engine + "_sheets.log"), "-compare", engine, "-fonts", FONTS, "-sheets", os.path.join(WORK, "sheets")], timeout=200)
    print("sheets", engine, process.returncode, flush=True)
for name in ("Managed/FirstSymbol.Lettera.dll", "Managed/FirstSymbol.Lettera.Unicode.dll", "Managed/FirstSymbol.Lettera.Text3D.dll", "Managed/Unity.TextMeshPro.dll"):
    path = os.path.join(OUT, "Release", "Compare_Data", name)
    print(name, os.path.getsize(path) // 1024 if os.path.exists(path) else "absent", "KB")
native = os.path.join(OUT, "Release", "Compare_Data", "Plugins", "x86_64", "lettera_native.dll")
print("lettera_native.dll", os.path.getsize(native) // 1024 if os.path.exists(native) else "absent", "KB")
