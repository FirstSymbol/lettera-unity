#!/usr/bin/env python3
"""Generates Runtime/Unicode/UnicodeData.g.cs and Tests/Unicode/Conformance~/*.gz from the Unicode 16.0.0 UCD.

Usage: gen_unicode.py <cache-dir>
Downloaded files are cached in <cache-dir>; outputs are written relative to this script's package.
"""
import base64
import gzip
import io
import os
import re
import struct
import sys
import urllib.request

UCD = "https://www.unicode.org/Public/16.0.0/ucd/"
VERSION = "16.0.0"
FILES = [
    "UnicodeData.txt", "Scripts.txt", "ScriptExtensions.txt", "PropertyValueAliases.txt",
    "extracted/DerivedBidiClass.txt", "BidiMirroring.txt", "BidiBrackets.txt", "LineBreak.txt",
    "auxiliary/GraphemeBreakProperty.txt", "auxiliary/WordBreakProperty.txt", "DerivedCoreProperties.txt",
    "EastAsianWidth.txt", "emoji/emoji-data.txt", "PropList.txt",
]
CONFORMANCE = [
    "auxiliary/GraphemeBreakTest.txt", "auxiliary/LineBreakTest.txt", "auxiliary/WordBreakTest.txt",
    "BidiCharacterTest.txt", "BidiTest.txt",
]
N = 0x110000

GC = "Lu Ll Lt Lm Lo Mn Mc Me Nd Nl No Pc Pd Ps Pe Pi Pf Po Sm Sc Sk So Zs Zl Zp Cc Cf Cs Co Cn".split()
BC = "L R AL EN ES ET AN CS NSM BN B S WS ON LRE LRO RLE RLO PDF LRI RLI FSI PDI".split()
LB = ("XX AI AK AL AP AS B2 BA BB BK CB CJ CL CM CP CR EB EM EX GL H2 H3 HL HY ID IN IS JL JT JV LF NL NS NU OP "
      "PO PR QU RI SA SG SP SY VF VI WJ ZW ZWJ").split()
GCB = "Other CR LF Control Extend ZWJ Regional_Indicator Prepend SpacingMark L V T LV LVT".split()
WB = ("Other CR LF Newline Extend ZWJ Regional_Indicator Format Katakana Hebrew_Letter ALetter Single_Quote "
      "Double_Quote MidNumLet MidLetter MidNum Numeric ExtendNumLet WSegSpace").split()
EA = "N Na A W F H".split()
INCB = "None Linker Consonant Extend".split()
BRACKET = {"n": 0, "o": 1, "c": 2}

F_EXTPICT, F_EMOJI, F_EPRES, F_EMOD, F_EBASE, F_RI, F_DI, F_WS = (1 << i for i in range(8))


def fetch(cache, rel):
    path = os.path.join(cache, os.path.basename(rel))
    if not os.path.exists(path):
        print("download", rel)
        with urllib.request.urlopen(UCD + rel) as r:
            data = r.read()
        with open(path, "wb") as f:
            f.write(data)
    return path


def lines(path):
    with open(path, encoding="utf8") as f:
        for ln in f:
            ln = ln.split("#", 1)[0].strip()
            if ln:
                yield [x.strip() for x in ln.split(";")]


def rng(s):
    if ".." in s:
        a, b = s.split("..")
        return int(a, 16), int(b, 16) + 1
    return int(s, 16), int(s, 16) + 1


def pascal(name):
    return name.replace("_", "")


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    cache = sys.argv[1]
    os.makedirs(cache, exist_ok=True)
    p = {os.path.basename(f): fetch(cache, f) for f in FILES + CONFORMANCE}
    root = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))

    # Aliases
    sc_short = {}
    bc_alias = {}
    for f in lines(p["PropertyValueAliases.txt"]):
        if f[0] == "sc":
            sc_short[f[2]] = f[1]
        elif f[0] == "bc":
            bc_alias[f[2]] = f[1]
            bc_alias[f[1]] = f[1]

    # Scripts: Unknown=0, Common=1, Inherited=2, then alphabetical.
    names = set()
    for f in lines(p["Scripts.txt"]):
        names.add(f[1])
    names -= {"Common", "Inherited", "Unknown"}
    scripts = ["Unknown", "Common", "Inherited"] + sorted(names)
    sc_id = {n: i for i, n in enumerate(scripts)}
    short_id = {sc_short[n]: i for n, i in sc_id.items()}
    tags = [sc_short[n] for n in scripts]
    assert len(scripts) <= 256

    gc = bytearray([GC.index("Cn")]) * N
    upper, lower, title = {}, {}, {}
    first = None
    for f in lines(p["UnicodeData.txt"]):
        cp = int(f[0], 16)
        cat = GC.index(f[2])
        if f[1].endswith(", First>"):
            first = cp
            continue
        if f[1].endswith(", Last>"):
            for c in range(first, cp + 1):
                gc[c] = cat
            continue
        gc[cp] = cat
        if f[12]:
            upper[cp] = int(f[12], 16)
        if f[13]:
            lower[cp] = int(f[13], 16)
        # UCD: an empty Simple_Titlecase_Mapping equals the Simple_Uppercase_Mapping.
        if f[14]:
            title[cp] = int(f[14], 16)
        elif f[12]:
            title[cp] = int(f[12], 16)

    sc = bytearray(N)
    for f in lines(p["Scripts.txt"]):
        a, b = rng(f[0])
        sc[a:b] = bytes([sc_id[f[1]]]) * (b - a)

    scx = bytearray(N)
    sets = [()]
    set_ids = {(): 0}
    for f in lines(p["ScriptExtensions.txt"]):
        a, b = rng(f[0])
        members = tuple(short_id[x] for x in f[1].split())
        i = set_ids.get(members)
        if i is None:
            i = set_ids[members] = len(sets)
            sets.append(members)
        scx[a:b] = bytes([i]) * (b - a)
    assert len(sets) <= 256, len(sets)

    bc = bytearray(N)
    # @missing lines give the defaults, later lines override earlier ones.
    with open(p["DerivedBidiClass.txt"], encoding="utf8") as f:
        for ln in f:
            m = re.match(r"# @missing: ([0-9A-F.]+); (\w+)", ln)
            if m:
                a, b = rng(m.group(1))
                bc[a:b] = bytes([BC.index(bc_alias[m.group(2)])]) * (b - a)
    for f in lines(p["DerivedBidiClass.txt"]):
        a, b = rng(f[0])
        bc[a:b] = bytes([BC.index(bc_alias[f[1]])]) * (b - a)

    mirror = {}
    for f in lines(p["BidiMirroring.txt"]):
        mirror[int(f[0], 16)] = int(f[1], 16)
    bracket = bytearray(N)
    for f in lines(p["BidiBrackets.txt"]):
        cp = int(f[0], 16)
        bracket[cp] = BRACKET[f[2]]
        # Accessors reuse the mirror glyph as the paired bracket; true for all of Unicode 16.
        assert mirror.get(cp) == int(f[1], 16), f

    lb = bytearray([LB.index("XX")]) * N
    for f in lines(p["LineBreak.txt"]):
        a, b = rng(f[0])
        lb[a:b] = bytes([LB.index(f[1])]) * (b - a)

    gcb = bytearray(N)
    for f in lines(p["GraphemeBreakProperty.txt"]):
        a, b = rng(f[0])
        gcb[a:b] = bytes([GCB.index(f[1])]) * (b - a)

    wb = bytearray(N)
    for f in lines(p["WordBreakProperty.txt"]):
        a, b = rng(f[0])
        wb[a:b] = bytes([WB.index(f[1])]) * (b - a)

    ea = bytearray(N)
    for f in lines(p["EastAsianWidth.txt"]):
        a, b = rng(f[0])
        ea[a:b] = bytes([EA.index(f[1])]) * (b - a)

    incb = bytearray(N)
    flags = bytearray(N)

    def setf(a, b, bit):
        for c in range(a, b):
            flags[c] |= bit

    for f in lines(p["DerivedCoreProperties.txt"]):
        a, b = rng(f[0])
        if f[1] == "InCB":
            incb[a:b] = bytes([INCB.index(f[2])]) * (b - a)
        elif f[1] == "Default_Ignorable_Code_Point":
            setf(a, b, F_DI)
    for f in lines(p["PropList.txt"]):
        a, b = rng(f[0])
        if f[1] == "White_Space":
            setf(a, b, F_WS)
        elif f[1] == "Regional_Indicator":
            setf(a, b, F_RI)
    emoji_bits = {"Emoji": F_EMOJI, "Emoji_Presentation": F_EPRES, "Emoji_Modifier": F_EMOD,
                  "Emoji_Modifier_Base": F_EBASE, "Extended_Pictographic": F_EXTPICT}
    for f in lines(p["emoji-data.txt"]):
        if f[1] in emoji_bits:
            a, b = rng(f[0])
            setf(a, b, emoji_bits[f[1]])

    # Packed records.
    mirror_deltas = [0]
    mirror_ids = {0: 0}
    case_triples = [(0, 0, 0)]
    case_ids = {(0, 0, 0): 0}
    records = []
    rec_ids = {}
    idx = [0] * N

    def intern(key):
        i = rec_ids.get(key)
        if i is None:
            i = rec_ids[key] = len(records)
            records.append(key)
        return i

    for c in range(N):
        d = mirror.get(c, c) - c
        mi = mirror_ids.get(d)
        if mi is None:
            mi = mirror_ids[d] = len(mirror_deltas)
            mirror_deltas.append(d)
        t = (upper.get(c, c) - c, lower.get(c, c) - c, title.get(c, c) - c)
        ci = case_ids.get(t)
        if ci is None:
            ci = case_ids[t] = len(case_triples)
            case_triples.append(t)
        ra = gc[c] | sc[c] << 5 | scx[c] << 13 | bc[c] << 21 | bracket[c] << 26 | incb[c] << 28
        rb = lb[c] | gcb[c] << 6 | wb[c] << 10 | ea[c] << 15 | flags[c] << 18
        rc = mi | ci << 8
        idx[c] = intern((ra, rb, rc))
    assert len(mirror_deltas) <= 256 and len(case_triples) <= 65536

    # Out-of-range code points: unassigned, Unknown script, L, XX.
    default_rec = intern((GC.index("Cn"), LB.index("XX"), 0))
    assert len(records) <= 65536

    # Pick the block size with the smallest two-stage size.
    best = None
    u16 = struct.pack("<%dH" % N, *idx)
    for shift in range(3, 12):
        size = 1 << shift
        blocks = {}
        stage1 = []
        stage2 = bytearray()
        for i in range(0, N, size):
            blk = u16[i * 2:(i + size) * 2]
            bi = blocks.get(blk)
            if bi is None:
                bi = blocks[blk] = len(blocks)
                stage2 += blk
            stage1.append(bi)
        total = len(stage1) * 2 + len(stage2)
        if len(blocks) <= 65536 and (best is None or total < best[0]):
            best = (total, shift, stage1, bytes(stage2))
    _, shift, stage1, stage2 = best

    sets_off = [0]
    sets_data = bytearray()
    for s in sets:
        sets_data += bytes(s)
        sets_off.append(len(sets_data))

    tables = [
        ("Stage1", "U16", struct.pack("<%dH" % len(stage1), *stage1)),
        ("Stage2", "U16", stage2),
        ("RecA", "U32", struct.pack("<%dI" % len(records), *[r[0] for r in records])),
        ("RecB", "U32", struct.pack("<%dI" % len(records), *[r[1] for r in records])),
        ("RecC", "U32", struct.pack("<%dI" % len(records), *[r[2] for r in records])),
        ("MirrorDeltas", "I32", struct.pack("<%di" % len(mirror_deltas), *mirror_deltas)),
        ("CaseDeltas", "I32", struct.pack("<%di" % (len(case_triples) * 3), *[v for t in case_triples for v in t])),
        ("ScxOffsets", "U16", struct.pack("<%dH" % len(sets_off), *sets_off)),
        ("ScxData", "U8", bytes(sets_data)),
        ("ScriptTags", "U32", struct.pack("<%dI" % len(tags), *[int.from_bytes(t.encode(), "big") for t in tags])),
    ]

    total = sum(len(t[2]) for t in tables)
    print("shift=%d stage1=%d stage2=%d records=%d mirrors=%d cases=%d scx=%d decoded=%d bytes" % (
        shift, len(stage1), len(stage2) // 2, len(records), len(mirror_deltas), len(case_triples), len(sets), total))

    out = io.StringIO()
    w = out.write
    w("// <auto-generated>Generated by Native~/tools/gen_unicode.py from the Unicode %s UCD. Do not edit.</auto-generated>\n" % VERSION)
    w("using System;\n\nnamespace FirstSymbol.Lettera.Unicode\n{\n")

    def enum(name, base, values):
        w("    public enum %s : %s\n    {\n" % (name, base))
        for i in range(0, len(values), 12):
            w("        " + " ".join(v + "," for v in values[i:i + 12]) + "\n")
        w("    }\n\n")

    enum("GeneralCategory", "byte", GC)
    enum("BidiClass", "byte", BC)
    enum("BidiPairedBracketType", "byte", ["None", "Open", "Close"])
    enum("LineBreakClass", "byte", LB)
    enum("GraphemeBreakClass", "byte", [pascal(x) for x in GCB])
    enum("WordBreakClass", "byte", [pascal(x) for x in WB])
    enum("EastAsianWidth", "byte", EA)
    enum("IndicConjunctBreak", "byte", INCB)
    enum("UnicodeScript", "ushort", [pascal(x) for x in scripts])

    w("    internal static class UnicodeData\n    {\n")
    w("        const int Shift = %d, Mask = %d, DefaultRecord = %d;\n\n" % (shift, (1 << shift) - 1, default_rec))
    # As array initializers: loading those is one copy from the binary, where decoding text cost the first text of a session 3 ms.
    cs_type = {"U8": ("byte", "B", 1, ""), "U16": ("ushort", "H", 2, ""), "U32": ("uint", "I", 4, "u"), "I32": ("int", "i", 4, "")}
    for name, kind, data in tables:
        type_name, code, width, suffix = cs_type[kind]
        values = struct.unpack("<%d%s" % (len(data) // width, code), data)
        w("        internal static readonly %s[] %s =
        {
" % (type_name, name))
        for i in range(0, len(values), 32):
            w("            " + ", ".join(str(v) + suffix for v in values[i:i + 32]) + ",
")
        w("        };
")
    w("""
        internal const uint FExtPict = 1u << 18, FEmoji = 1u << 19, FEmojiPres = 1u << 20, FEmojiMod = 1u << 21,
            FEmojiBase = 1u << 22, FRegional = 1u << 23, FDefaultIgnorable = 1u << 24, FWhiteSpace = 1u << 25;

        internal static int Rec(int cp) => (uint)cp > 0x10FFFF ? DefaultRecord : Stage2[(Stage1[cp >> Shift] << Shift) | (cp & Mask)];

        internal static GeneralCategory Gc(int cp) => (GeneralCategory)(RecA[Rec(cp)] & 31);
        internal static int Script(int cp) => (int)(RecA[Rec(cp)] >> 5) & 255;
        internal static int ScxSet(int cp) => (int)(RecA[Rec(cp)] >> 13) & 255;
        internal static BidiClass Bidi(int cp) => (BidiClass)((RecA[Rec(cp)] >> 21) & 31);
        internal static BidiPairedBracketType Bracket(int cp) => (BidiPairedBracketType)((RecA[Rec(cp)] >> 26) & 3);
        internal static IndicConjunctBreak InCB(int cp) => (IndicConjunctBreak)((RecA[Rec(cp)] >> 28) & 3);
        internal static LineBreakClass Lb(int cp) => (LineBreakClass)(RecB[Rec(cp)] & 63);
        internal static GraphemeBreakClass Gcb(int cp) => (GraphemeBreakClass)((RecB[Rec(cp)] >> 6) & 15);
        internal static WordBreakClass Wb(int cp) => (WordBreakClass)((RecB[Rec(cp)] >> 10) & 31);
        internal static EastAsianWidth Ea(int cp) => (EastAsianWidth)((RecB[Rec(cp)] >> 15) & 7);
        internal static bool Flag(int cp, uint flag) => (RecB[Rec(cp)] & flag) != 0;
        internal static int Mirror(int cp) => cp + MirrorDeltas[RecC[Rec(cp)] & 255];
        internal static int Case(int cp, int kind) => cp + CaseDeltas[3 * (int)(RecC[Rec(cp)] >> 8) + kind];

        internal static bool ScxContains(int set, int script)
        {
            for (int i = ScxOffsets[set], e = ScxOffsets[set + 1]; i < e; i++)
                if (ScxData[i] == script) return true;
            return false;
        }
    }
}
""")

    path = os.path.join(root, "Runtime", "Unicode", "UnicodeData.g.cs")
    with open(path, "w", encoding="utf8", newline="\n") as f:
        f.write(out.getvalue())
    print("wrote", path, os.path.getsize(path))

    conf = os.path.join(root, "Tests", "Unicode", "Conformance~")
    os.makedirs(conf, exist_ok=True)
    for rel in CONFORMANCE:
        name = os.path.basename(rel)
        with open(p[name], "rb") as f:
            data = f.read()
        dst = os.path.join(conf, name + ".gz")
        with open(dst, "wb") as f:
            with gzip.GzipFile(filename="", mode="wb", fileobj=f, mtime=0, compresslevel=9) as z:
                z.write(data)
        print("wrote", dst, os.path.getsize(dst))


if __name__ == "__main__":
    main()
