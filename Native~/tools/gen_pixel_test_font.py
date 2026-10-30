"""Writes Tests/Fonts~/LetteraPixelTest.ttf: a tiny font of our own drawn on an 8 pixel grid, for testing pixel font support.

usage: python gen_pixel_test_font.py
"""
import os
import struct

UPEM = 800
CELL = UPEM // 8

GLYPHS = {
    "A": [".XXXXX..", "XX...XX.", "XX...XX.", "XXXXXXX.", "XX...XX.", "XX...XX.", "XX...XX.", "........"],
    "H": ["XX...XX.", "XX...XX.", "XX...XX.", "XXXXXXX.", "XX...XX.", "XX...XX.", "XX...XX.", "........"],
    "O": [".XXXXX..", "XX...XX.", "XX...XX.", "XX...XX.", "XX...XX.", "XX...XX.", ".XXXXX..", "........"],
    "I": ["XXXXXX..", "..XX....", "..XX....", "..XX....", "..XX....", "..XX....", "XXXXXX..", "........"],
    "x": ["........", "........", "XX...XX.", ".XX.XX..", "..XXX...", ".XX.XX..", "XX...XX.", "........"],
    ".": ["........", "........", "........", "........", "........", "XX......", "XX......", "........"],
    " ": ["........"] * 8,
}


def contours(rows):
    """One rectangle for each run of filled cells in a row. Row 0 is the top, and the baseline is under row 6."""
    result = []
    for r, row in enumerate(rows):
        c = 0
        while c < 8:
            if row[c] != "X":
                c += 1
                continue
            start = c
            while c < 8 and row[c] == "X":
                c += 1
            x0, x1 = start * CELL, c * CELL
            y1 = (7 - r) * CELL
            y0 = y1 - CELL
            result.append([(x0, y0), (x0, y1), (x1, y1), (x1, y0)])  # Clockwise, as TrueType fills.
    return result


def glyph_data(rows):
    shapes = contours(rows)
    if not shapes:
        return b""
    points = [p for shape in shapes for p in shape]
    xs, ys = [p[0] for p in points], [p[1] for p in points]
    data = struct.pack(">hhhhh", len(shapes), min(xs), min(ys), max(xs), max(ys))
    end = -1
    for shape in shapes:
        end += len(shape)
        data += struct.pack(">H", end)
    data += struct.pack(">H", 0)
    data += bytes([1] * len(points))  # On-curve, with two-byte deltas.
    last = 0
    for x, _ in points:
        data += struct.pack(">h", x - last)
        last = x
    last = 0
    for _, y in points:
        data += struct.pack(">h", y - last)
        last = y
    if len(data) % 4:
        data += bytes(4 - len(data) % 4)
    return data


order = [".notdef"] + sorted(GLYPHS)
glyf, loca = b"", []
for name in order:
    loca.append(len(glyf))
    glyf += glyph_data(GLYPHS.get(name, ["........"] * 8))
loca.append(len(glyf))

all_points = [p for name in GLYPHS for shape in contours(GLYPHS[name]) for p in shape]
x_max, y_min, y_max = max(p[0] for p in all_points), min(p[1] for p in all_points), max(p[1] for p in all_points)

head = struct.pack(">IIIIHHQQhhhhHHhhh", 0x00010000, 0x00010000, 0, 0x5F0F3CF5, 0x000B, UPEM, 0, 0, 0, y_min, x_max, y_max, 0, 8, 2, 1, 0)
hhea = struct.pack(">IhhhHhhhhhhhhhhhH", 0x00010000, 700, -100, 0, UPEM, 0, 0, x_max, 1, 0, 0, 0, 0, 0, 0, 0, len(order))
maxp = struct.pack(">IHHHHHHHHHHHHHH", 0x00010000, len(order), 64, 16, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0)
hmtx = b"".join(struct.pack(">Hh", UPEM, 0) for _ in order)
loca_table = b"".join(struct.pack(">I", v) for v in loca)
post = struct.pack(">IIhhIIIII", 0x00030000, 0, -100, 50, 0, 0, 0, 0, 0)

# A cmap with one format 4 subtable.
codes = sorted((ord(n), order.index(n)) for n in GLYPHS)
segments = len(codes) + 1
end_codes = [c for c, _ in codes] + [0xFFFF]
start_codes = end_codes
deltas = [(g - c) & 0xFFFF for c, g in codes] + [1]
fmt4 = struct.pack(">HHHH", segments * 2, 0, 0, 0)
fmt4 += b"".join(struct.pack(">H", v) for v in end_codes) + struct.pack(">H", 0)
fmt4 += b"".join(struct.pack(">H", v) for v in start_codes)
fmt4 += b"".join(struct.pack(">H", v) for v in deltas)
fmt4 += b"".join(struct.pack(">H", 0) for _ in range(segments))
fmt4 = struct.pack(">HHH", 4, len(fmt4) + 6, 0) + fmt4
cmap = struct.pack(">HHHHI", 0, 1, 3, 1, 12) + fmt4

family = "Lettera Pixel Test".encode("utf-16-be")
style = "Regular".encode("utf-16-be")
name = struct.pack(">HHH", 0, 2, 6 + 24)
name += struct.pack(">HHHHHH", 3, 1, 0x409, 1, len(family), 0) + struct.pack(">HHHHHH", 3, 1, 0x409, 2, len(style), len(family))
name += family + style

tables = {"cmap": cmap, "glyf": glyf, "head": head, "hhea": hhea, "hmtx": hmtx, "loca": loca_table, "maxp": maxp, "name": name, "post": post}
offset = 12 + 16 * len(tables)
directory, body = b"", b""
for tag in sorted(tables):
    data = tables[tag]
    directory += struct.pack(">4sIII", tag.encode(), 0, offset + len(body), len(data))
    body += data + bytes((4 - len(data) % 4) % 4)
font = struct.pack(">IHHHH", 0x00010000, len(tables), 128, 3, 16) + directory + body

here = os.path.dirname(os.path.abspath(__file__))
path = os.path.join(here, "..", "..", "Tests", "Fonts~", "LetteraPixelTest.ttf")
with open(path, "wb") as f:
    f.write(font)
print("wrote", os.path.normpath(path), len(font))
