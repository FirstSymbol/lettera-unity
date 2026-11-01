"""Runs the comparison with TextMesh Pro and writes its tables.
usage: python compare.py <unity.exe> <lettera package path> <work folder> [rounds] [--backends mono,il2cpp] [--engines a,b,c] [--extra folder] [--no-sheets] [--fresh]
Makes a project of its own, builds Windows players, and runs each engine in turns, a cold start each. Nothing else should be running.
Results land per run in <work>/Out/<backend>/runs, so that a run stopped halfway goes on from where it was. --fresh starts over.
--extra copies that folder's scripts into the project: another engine can add itself to LetteraCompare.External and be named in --engines."""
import argparse, ctypes, ctypes.wintypes as wt, json, os, shutil, statistics, subprocess, sys, time

parser = argparse.ArgumentParser()
parser.add_argument("editor")
parser.add_argument("package")
parser.add_argument("work")
parser.add_argument("rounds", nargs="?", type=int, default=5)
parser.add_argument("--backends", default="mono")
parser.add_argument("--engines", default="lettera,tmp-static,tmp-dynamic")
parser.add_argument("--extra")
parser.add_argument("--no-sheets", action="store_true")
parser.add_argument("--fresh", action="store_true")
parser.add_argument("--build-only", action="store_true")
parser.add_argument("--dev-rounds", type=int, default=None)
parser.add_argument("--watch-pid", type=int, default=0, help="a process whose CPU use is noted beside each run, such as an editor left open")
args = parser.parse_args()
EDITOR, PACKAGE, WORK, ROUNDS = args.editor, args.package, args.work, args.rounds
DEV_ROUNDS = ROUNDS if args.dev_rounds is None else args.dev_rounds
HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.join(WORK, "Project")
OUT = os.path.join(WORK, "Out")
os.makedirs(OUT, exist_ok=True)
FONTS = os.path.join(PACKAGE, "Tests", "Fonts~")
ENGINES = args.engines.split(",")
BACKENDS = args.backends.split(",")
SCENARIOS = ["main", "extra", "first", "multilang", "font", "memory"]
DEV_SCENARIOS = ["main", "extra", "multilang", "draw"]
sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def build(path, dev, backend):
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
    if args.extra:
        folder = os.path.join(PROJECT, "Assets", "Compare", "Extra")
        os.makedirs(folder, exist_ok=True)
        for name in os.listdir(args.extra):
            if name.endswith(".cs"):
                shutil.copyfile(os.path.join(args.extra, name), os.path.join(folder, name))
    resources = os.path.join(PROJECT, "Assets", "Compare", "Resources")
    os.makedirs(resources, exist_ok=True)
    shutil.copyfile(os.path.join(FONTS, "NotoSans-Regular.ttf"), os.path.join(resources, "NotoSans-Regular.ttf"))
    env = dict(os.environ)
    # Its essentials, once: shaders and settings it cannot run without.
    if not os.path.exists(os.path.join(PROJECT, "Assets", "TextMesh Pro")):
        subprocess.run([EDITOR, "-batchmode", "-quit", "-projectPath", PROJECT, "-logFile", os.path.join(OUT, "resolve.log")])
        cache = os.path.join(PROJECT, "Library", "PackageCache")
        ugui = next(d for d in os.listdir(cache) if d.startswith("com.unity.ugui"))
        subprocess.run([EDITOR, "-batchmode", "-quit", "-projectPath", PROJECT, "-importPackage", os.path.join(cache, ugui, "Package Resources", "TMP Essential Resources.unitypackage"), "-logFile", os.path.join(OUT, "essentials.log")])
        print("essentials", os.path.exists(os.path.join(PROJECT, "Assets", "TextMesh Pro")), flush=True)
    env["LETTERA_DEV"] = "1" if dev else "0"
    env["LETTERA_COMPARE_BACKEND"] = backend
    log = os.path.join(OUT, f"build_{backend}{'_dev' if dev else ''}.log")
    # Twice, the first time: essentials imported in one pass are compiled in the next.
    for attempt in range(2):
        code = subprocess.run([EDITOR, "-batchmode", "-quit", "-projectPath", PROJECT, "-buildTarget", "Win64", "-executeMethod", "LetteraCompareBuild.Run", "-out", path, "-logFile", log], env=env).returncode
        text = open(log, encoding="utf-8", errors="replace").read()
        if "LETTERA_COMPARE_BUILD Succeeded" in text:
            print("built", backend, "development" if dev else "release", flush=True)
            return
        print("build attempt", attempt, "exit", code, flush=True)
    for line in text.splitlines():
        if "error CS" in line or "LETTERA_COMPARE_BUILD" in line:
            print("  ", line[:220])
    sys.exit(1)


class Counters(ctypes.Structure):
    _fields_ = [("cb", wt.DWORD), ("faults", wt.DWORD), ("peak", ctypes.c_size_t), ("working", ctypes.c_size_t), ("a", ctypes.c_size_t), ("b", ctypes.c_size_t), ("c", ctypes.c_size_t), ("d", ctypes.c_size_t), ("e", ctypes.c_size_t), ("f", ctypes.c_size_t)]


kernel = ctypes.WinDLL("kernel32", use_last_error=True)
kernel.OpenProcess.restype = wt.HANDLE
kernel.K32GetProcessMemoryInfo.argtypes = [wt.HANDLE, ctypes.POINTER(Counters), wt.DWORD]
kernel.GetProcessTimes.argtypes = [wt.HANDLE] + [ctypes.POINTER(wt.FILETIME)] * 4


def memory_mb(handle):
    # Asked of the system directly: a helper process each second is itself load on what is being timed.
    counters = Counters()
    counters.cb = ctypes.sizeof(Counters)
    if not kernel.K32GetProcessMemoryInfo(handle, ctypes.byref(counters), counters.cb):
        return 0, 0
    return counters.working // 1048576, counters.peak // 1048576


def cpu_seconds(pid):
    if not pid:
        return 0.0
    handle = kernel.OpenProcess(0x1000, False, pid)
    if not handle:
        return 0.0
    a, b, kernel_time, user = wt.FILETIME(), wt.FILETIME(), wt.FILETIME(), wt.FILETIME()
    kernel.GetProcessTimes(handle, ctypes.byref(a), ctypes.byref(b), ctypes.byref(kernel_time), ctypes.byref(user))
    kernel.CloseHandle(handle)
    return sum((t.dwHighDateTime << 32 | t.dwLowDateTime) / 1e7 for t in (kernel_time, user))


def run(exe, engine, name, folder, scenario="main", extra=(), timeout=420, limit_mb=10240):
    out = os.path.join(folder, name + ".json")
    if os.path.exists(out):
        return json.load(open(out, encoding="utf-8"))
    partial = out + ".part"
    if os.path.exists(partial):
        os.remove(partial)
    before = cpu_seconds(args.watch_pid)
    process = subprocess.Popen([exe, "-screen-width", "1200", "-screen-height", "700", "-screen-fullscreen", "0", "-logFile", os.path.join(folder, name + ".log"), "-compare", engine, "-scenario", scenario, "-fonts", FONTS, "-out", partial, *extra])
    handle = kernel.OpenProcess(0x1000, False, process.pid)
    started = time.time()
    peak = 0
    # A run that eats the machine stops here, and not when everything else on it has crashed.
    while process.poll() is None:
        time.sleep(1)
        used, peak_now = memory_mb(handle)
        peak = max(peak, peak_now)
        if used > limit_mb:
            process.kill()
            print(f"  stopped at {used} MB", name, flush=True)
            break
        if time.time() - started > timeout:
            process.kill()
            print("  timed out", name, flush=True)
            break
    took = time.time() - started
    kernel.CloseHandle(handle)
    watched = cpu_seconds(args.watch_pid) - before
    print(f"  {name}: peak {peak} MB, {took:.0f} s" + (f", watched process used {watched:.1f} s of CPU" if args.watch_pid else ""), flush=True)
    if not os.path.exists(partial):
        return None
    data = json.load(open(partial, encoding="utf-8"))
    data["peak_mb"] = peak
    data["seconds"] = took
    data["watched_cpu"] = watched
    json.dump(data, open(out, "w", encoding="utf-8"))
    os.remove(partial)
    return data


def note_value(note, key):
    return note.split(key + "=")[1].split()[0] if key + "=" in note else None


def measure(backend):
    folder = os.path.join(OUT, backend)
    # Unity refuses to build another backend into an old folder, and Mono keeps the paths this tool has always used.
    release = os.path.join(OUT, "Release" if backend == "mono" else "IL2CPP_Release", "Compare.exe")
    dev = os.path.join(OUT, "Dev" if backend == "mono" else "IL2CPP_Dev", "Compare.exe")
    runs = os.path.join(folder, "runs")
    if args.fresh and os.path.exists(runs):
        shutil.rmtree(runs)
    os.makedirs(runs, exist_ok=True)
    if not os.path.exists(release):
        build(release, False, backend)
    if not os.path.exists(dev):
        build(dev, True, backend)
    if args.build_only:
        return None, release

    results = {e: {} for e in ENGINES}
    bytes_values = {e: {} for e in ENGINES}
    largest = {e: {} for e in ENGINES}
    gpu = {e: {} for e in ENGINES}
    notes = {e: {} for e in ENGINES}
    peaks = {e: [] for e in ENGINES}
    watched = []
    meta = None
    for r in range(ROUNDS):
        for engine in ENGINES:
            for scenario in SCENARIOS:
                data = run(release, engine, f"{engine}_{scenario}_{r}", runs, scenario)
                if data is None:
                    print("no result", engine, scenario, r, flush=True)
                    continue
                meta = meta or data
                watched.append(data.get("watched_cpu", 0))
                if scenario == "main":
                    peaks[engine].append(data.get("peak_mb", 0))
                for row in data["rows"]:
                    results[engine].setdefault(row["row"], []).append(row["ms"])
                    if row["note"]:
                        notes[engine][row["row"]] = row["note"]
                    value = note_value(row["note"], "gpu")
                    if value:
                        gpu[engine].setdefault(row["row"], []).append(float(value))
        print("round", r, "done", backend, flush=True)
    for r in range(DEV_ROUNDS):
        for engine in ENGINES:
            for scenario in DEV_SCENARIOS:
                data = run(dev, engine, f"{engine}_{scenario}_dev_{r}", runs, scenario, ("-light",))
                if data is None:
                    print("no result", engine, scenario, "dev", r, flush=True)
                    continue
                for row in data["rows"]:
                    if row["bytes"] >= 0:
                        bytes_values[engine].setdefault(row["row"], []).append(row["bytes"])
                    value = note_value(row["note"], "largest")
                    if value:
                        largest[engine].setdefault(row["row"], []).append(int(value))
                    if row["row"].startswith("D_"):
                        results[engine].setdefault(row["row"], []).append(row["ms"])
                    if row["row"] == "control_gc_counter" and r == 0 and scenario == "main":
                        print("gc control", engine, row["bytes"], row["note"], flush=True)
    if meta is None:
        print("No engine gave a result: read the logs in", runs)
        sys.exit(1)
    summary = {"meta": {k: meta[k] for k in ("unity", "backend", "gpu", "device")}, "rounds": ROUNDS, "dev_rounds": DEV_ROUNDS, "watched_cpu": watched, "peaks": peaks, "rows": {}}
    for engine in ENGINES:
        rows = set(results[engine]) | set(bytes_values[engine])
        for row in rows:
            values = results[engine].get(row, [])
            summary["rows"].setdefault(row, {})[engine] = {
                "ms": statistics.median(values) if values else -1,
                "runs": len(values),
                "values": values,
                "spread": max(values) - min(values) if values else 0,
                "bytes": statistics.median(bytes_values[engine][row]) if row in bytes_values[engine] else -1,
                "bytes_values": bytes_values[engine].get(row, []),
                "largest": max(largest[engine][row]) if row in largest[engine] else -1,
                "gpu": statistics.median(gpu[engine][row]) if row in gpu[engine] else -1,
                "gpu_values": gpu[engine].get(row, []),
                "note": notes[engine].get(row, ""),
            }
    name = "compare.json" if backend == "mono" else f"compare_{backend}.json"
    json.dump(summary, open(os.path.join(WORK, name), "w", encoding="utf-8"), indent=1)

    lines = ["| Row | " + " | ".join(ENGINES) + " |", "|---|" + "---|" * len(ENGINES)]
    for row in summary["rows"]:
        cells = []
        for engine in ENGINES:
            cell = summary["rows"][row].get(engine)
            if not cell:
                cells.append("no result")
                continue
            text = f"{cell['ms']:.2f} ± {cell['spread']:.2f}"
            if cell["bytes"] >= 0:
                text += f", {cell['bytes']:.0f} B"
            if "failed" in cell["note"]:
                text += " " + cell["note"].split(" frame")[0]
            cells.append(text)
        lines.append(f"| {row} | " + " | ".join(cells) + " |")
    open(os.path.join(WORK, f"compare_table_{backend}.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("\n".join(lines))
    return summary, release


SHEETS = ENGINES + (["tmp-fallback"] if "tmp-dynamic" in ENGINES else [])


def sheets(release):
    for engine in SHEETS:
        process = subprocess.run([release, "-screen-width", "1200", "-screen-height", "700", "-screen-fullscreen", "0", "-logFile", os.path.join(OUT, engine + "_sheets.log"), "-compare", engine, "-fonts", FONTS, "-sheets", os.path.join(WORK, "sheets")], timeout=300)
        print("sheets", engine, process.returncode, flush=True)


if os.environ.get("COMPARE_SHEETS_ONLY"):
    sheets(os.path.join(OUT, "Release", "Compare.exe"))
    sys.exit(0)

for backend in BACKENDS:
    summary, release = measure(backend)
    if args.build_only:
        continue
    if not args.no_sheets and backend == BACKENDS[0]:
        sheets(release)
    root = os.path.dirname(release)
    data = os.path.join(root, "Compare_Data")
    for name in ("Managed/FirstSymbol.Lettera.dll", "Managed/FirstSymbol.Lettera.Unicode.dll", "Managed/FirstSymbol.Lettera.Text3D.dll", "Managed/Unity.TextMeshPro.dll"):
        path = os.path.join(data, name)
        print(backend, name, os.path.getsize(path) // 1024 if os.path.exists(path) else "absent", "KB")
    native = os.path.join(data, "Plugins", "x86_64", "lettera_native.dll")
    print(backend, "lettera_native.dll", os.path.getsize(native) // 1024 if os.path.exists(native) else "absent", "KB")
