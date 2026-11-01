"""
Writes the fonts the samples carry, into Samples~. Needs fontTools (pip install fonttools).

    python sample_fonts.py <path to Inter[opsz,wght].ttf from github.com/google/fonts/tree/main/ofl/inter>

Three are drawn here and belong to this package: an icon font, a pixel font and a COLRv1 font. The fourth is a subset of Inter, under the SIL Open Font License.
"""
import os
import sys

from fontTools.colorLib.builder import buildCOLR, buildCPAL
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont
from fontTools.ttLib.tables import otTables as ot
from fontTools.varLib import instancer

HERE = os.path.dirname(os.path.abspath(__file__))
SAMPLES = os.path.join(HERE, "..", "Samples~")
COPYRIGHT = "Drawn for the Lettera package."


def polygon(pen, points):
    pen.moveTo(points[0])
    for point in points[1:]:
        pen.lineTo(point)
    pen.closePath()


def circle(pen, cx, cy, r):
    # Eight quadratic arcs, clockwise as TrueType fills.
    k = 0.4142
    d = 0.7071
    pen.moveTo((cx, cy + r))
    for (qx, qy), (x, y) in [
        ((k, 1), (d, d)), ((1, k), (1, 0)), ((1, -k), (d, -d)), ((k, -1), (0, -1)),
        ((-k, -1), (-d, -d)), ((-1, -k), (-1, 0)), ((-1, k), (-d, d)), ((-k, 1), (0, 1)),
    ]:
        pen.qCurveTo((cx + qx * r, cy + qy * r), (cx + x * r, cy + y * r))
    pen.closePath()


def star(pen, cx, cy, outer, inner, points=5):
    import math
    corners = []
    for i in range(points * 2):
        r = outer if i % 2 == 0 else inner
        angle = math.pi / 2 - i * math.pi / points
        corners.append((round(cx + r * math.cos(angle)), round(cy + r * math.sin(angle))))
    polygon(pen, corners)


def heart(pen):
    pen.moveTo((500, 80))
    pen.qCurveTo((60, 400), (80, 620))
    pen.qCurveTo((100, 840), (290, 840))
    pen.qCurveTo((430, 840), (500, 700))
    pen.qCurveTo((570, 840), (710, 840))
    pen.qCurveTo((900, 840), (920, 620))
    pen.qCurveTo((940, 400), (500, 80))
    pen.closePath()


def flame(pen, scale=1.0, cx=500, base=60):
    def p(x, y):
        return (round(cx + (x - 500) * scale), round(base + (y - 60) * scale))
    pen.moveTo(p(500, 60))
    pen.qCurveTo(p(200, 80), p(200, 360))
    pen.qCurveTo(p(200, 540), p(360, 660))
    pen.qCurveTo(p(380, 520), p(460, 500))
    pen.qCurveTo(p(420, 700), p(560, 900))
    pen.qCurveTo(p(600, 700), p(720, 560))
    pen.qCurveTo(p(800, 460), p(800, 340))
    pen.qCurveTo(p(800, 80), p(500, 60))
    pen.closePath()


def glyph_of(draw):
    pen = TTGlyphPen(None)
    draw(pen)
    return pen.glyph()


def build(family, order, cmap, glyphs, advances, upem=1000, ascent=800, descent=-200):
    builder = FontBuilder(upem, isTTF=True)
    builder.setupGlyphOrder(order)
    builder.setupCharacterMap(cmap)
    builder.setupGlyf(glyphs)
    metrics = {}
    for name in order:
        g = glyphs[name]
        g.recalcBounds(builder.font["glyf"])
        metrics[name] = (advances.get(name, upem), getattr(g, "xMin", 0) or 0)
    builder.setupHorizontalMetrics(metrics)
    builder.setupHorizontalHeader(ascent=ascent, descent=descent)
    builder.setupNameTable({"familyName": family, "styleName": "Regular", "copyright": COPYRIGHT})
    builder.setupOS2(sTypoAscender=ascent, sTypoDescender=descent, usWinAscent=ascent, usWinDescent=-descent)
    builder.setupPost()
    return builder


def write(builder_or_font, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    font = getattr(builder_or_font, "font", builder_or_font)
    font.save(path)
    print(os.path.relpath(path, SAMPLES), os.path.getsize(path))


def icons():
    def check(pen):
        polygon(pen, [(120, 440), (240, 560), (400, 400), (780, 780), (900, 660), (400, 160)])

    def play(pen):
        polygon(pen, [(240, 100), (240, 800), (820, 450)])

    def pause(pen):
        polygon(pen, [(220, 120), (220, 780), (420, 780), (420, 120)])
        polygon(pen, [(580, 120), (580, 780), (780, 780), (780, 120)])

    def arrow(pen):
        polygon(pen, [(100, 370), (100, 530), (560, 530), (560, 720), (900, 450), (560, 180), (560, 370)])

    def cross(pen):
        polygon(pen, [(180, 280), (370, 450), (180, 620), (300, 740), (500, 560), (700, 740), (820, 620), (630, 450), (820, 280), (700, 160), (500, 340), (300, 160)])

    def dot(pen):
        circle(pen, 500, 450, 300)

    draws = {".notdef": lambda pen: None, "star": lambda pen: star(pen, 500, 450, 430, 180), "heart": heart, "check": check, "play": play, "pause": pause, "arrow": arrow, "cross": cross, "dot": dot}
    order = list(draws)
    cmap = {0xE000 + i: name for i, name in enumerate(order[1:])}
    glyphs = {name: glyph_of(draw) for name, draw in draws.items()}
    write(build("Lettera Sample Icons", order, cmap, glyphs, {}), os.path.join(SAMPLES, "Fonts", "Resources", "LetteraSampleIcons.bytes"))


PIXELS = {
    "A": "01110 10001 10001 11111 10001 10001 10001", "B": "11110 10001 10001 11110 10001 10001 11110", "C": "01110 10001 10000 10000 10000 10001 01110",
    "D": "11110 10001 10001 10001 10001 10001 11110", "E": "11111 10000 10000 11110 10000 10000 11111", "F": "11111 10000 10000 11110 10000 10000 10000",
    "G": "01110 10001 10000 10111 10001 10001 01111", "H": "10001 10001 10001 11111 10001 10001 10001", "I": "01110 00100 00100 00100 00100 00100 01110",
    "J": "00111 00010 00010 00010 00010 10010 01100", "K": "10001 10010 10100 11000 10100 10010 10001", "L": "10000 10000 10000 10000 10000 10000 11111",
    "M": "10001 11011 10101 10101 10001 10001 10001", "N": "10001 11001 10101 10011 10001 10001 10001", "O": "01110 10001 10001 10001 10001 10001 01110",
    "P": "11110 10001 10001 11110 10000 10000 10000", "Q": "01110 10001 10001 10001 10101 10010 01101", "R": "11110 10001 10001 11110 10100 10010 10001",
    "S": "01111 10000 10000 01110 00001 00001 11110", "T": "11111 00100 00100 00100 00100 00100 00100", "U": "10001 10001 10001 10001 10001 10001 01110",
    "V": "10001 10001 10001 10001 10001 01010 00100", "W": "10001 10001 10001 10101 10101 11011 10001", "X": "10001 10001 01010 00100 01010 10001 10001",
    "Y": "10001 10001 01010 00100 00100 00100 00100", "Z": "11111 00001 00010 00100 01000 10000 11111",
    "0": "01110 10011 10101 10101 10101 11001 01110", "1": "00100 01100 00100 00100 00100 00100 01110", "2": "01110 10001 00001 00010 00100 01000 11111",
    "3": "11110 00001 00001 01110 00001 00001 11110", "4": "00010 00110 01010 10010 11111 00010 00010", "5": "11111 10000 11110 00001 00001 10001 01110",
    "6": "00110 01000 10000 11110 10001 10001 01110", "7": "11111 00001 00010 00100 01000 01000 01000", "8": "01110 10001 10001 01110 10001 10001 01110",
    "9": "01110 10001 10001 01111 00001 00010 01100",
    "period": "00000 00000 00000 00000 00000 01100 01100", "comma": "00000 00000 00000 00000 01100 00100 01000", "colon": "00000 01100 01100 00000 01100 01100 00000",
    "exclam": "00100 00100 00100 00100 00100 00000 00100", "question": "01110 10001 00001 00010 00100 00000 00100", "hyphen": "00000 00000 00000 11111 00000 00000 00000",
    "space": "00000 00000 00000 00000 00000 00000 00000",
}
PIXEL_CODES = {"period": ".", "comma": ",", "colon": ":", "exclam": "!", "question": "?", "hyphen": "-", "space": " "}


def pixel():
    unit = 100

    def draw_of(rows):
        def draw(pen):
            for y, row in enumerate(rows.split()):
                for x, bit in enumerate(row):
                    if bit == "1":
                        left, bottom = x * unit, (6 - y) * unit
                        polygon(pen, [(left, bottom), (left, bottom + unit), (left + unit, bottom + unit), (left + unit, bottom)])
        return draw

    order = [".notdef"] + list(PIXELS)
    glyphs = {".notdef": glyph_of(lambda pen: None)}
    cmap = {}
    for name, rows in PIXELS.items():
        glyphs[name] = glyph_of(draw_of(rows))
        code = PIXEL_CODES.get(name, name)
        cmap[ord(code)] = name
        if code.isalpha():
            cmap[ord(code.lower())] = name
    advances = {name: 6 * unit for name in order}
    write(build("Lettera Sample Pixel", order, cmap, glyphs, advances, upem=800, ascent=700, descent=-100), os.path.join(SAMPLES, "Fonts", "Resources", "LetteraSamplePixel.bytes"))


def color():
    draws = {
        ".notdef": lambda pen: None,
        "base": lambda pen: circle(pen, 500, 450, 10),
        "star": lambda pen: star(pen, 500, 450, 430, 180),
        "star_core": lambda pen: star(pen, 500, 450, 210, 90),
        "heart": heart,
        "flame": flame,
        "flame_core": lambda pen: flame(pen, 0.5, 500, 60),
        "ring": lambda pen: circle(pen, 500, 450, 420),
        "ring_mid": lambda pen: circle(pen, 500, 450, 280),
        "ring_core": lambda pen: circle(pen, 500, 450, 140),
    }
    order = list(draws)
    glyphs = {name: glyph_of(draw) for name, draw in draws.items()}
    # Each colour glyph is a base glyph of its own, so that it has an outline where a colour font is not understood.
    glyphs["star_base"], glyphs["heart_base"], glyphs["flame_base"], glyphs["target_base"] = glyphs["star"], glyphs["heart"], glyphs["flame"], glyphs["ring"]
    order += ["star_base", "heart_base", "flame_base", "target_base"]
    cmap = {0x1F31F: "star_base", 0x2764: "heart_base", 0x1F525: "flame_base", 0x1F3AF: "target_base"}
    builder = build("Lettera Sample Color", order, cmap, glyphs, {})
    font = builder.font
    palette = [(1.0, 0.82, 0.2, 1.0), (1.0, 0.45, 0.1, 1.0), (0.95, 0.15, 0.35, 1.0), (0.55, 0.05, 0.45, 1.0), (1.0, 1.0, 1.0, 1.0), (0.1, 0.5, 0.95, 1.0)]
    font["CPAL"] = buildCPAL([palette])

    def linear(c0, c1, x0=500, y0=900, x1=500, y1=60):
        return {
            "Format": ot.PaintFormat.PaintLinearGradient,
            "ColorLine": {"ColorStop": [{"StopOffset": 0.0, "PaletteIndex": c0}, {"StopOffset": 1.0, "PaletteIndex": c1}], "Extend": "pad"},
            "x0": x0, "y0": y0, "x1": x1, "y1": y1, "x2": x0 + 100, "y2": y0,
        }

    def radial(c0, c1, r=420):
        return {
            "Format": ot.PaintFormat.PaintRadialGradient,
            "ColorLine": {"ColorStop": [{"StopOffset": 0.0, "PaletteIndex": c0}, {"StopOffset": 1.0, "PaletteIndex": c1}], "Extend": "pad"},
            "x0": 500, "y0": 450, "r0": 0, "x1": 500, "y1": 450, "r1": r,
        }

    def on(glyph, paint):
        return {"Format": ot.PaintFormat.PaintGlyph, "Paint": paint, "Glyph": glyph}

    def solid(index):
        return {"Format": ot.PaintFormat.PaintSolid, "PaletteIndex": index, "Alpha": 1.0}

    layers = {
        "star_base": {"Format": ot.PaintFormat.PaintColrLayers, "Layers": [on("star", linear(0, 1)), on("star_core", solid(4))]},
        "heart_base": on("heart", linear(2, 3)),
        "flame_base": {"Format": ot.PaintFormat.PaintColrLayers, "Layers": [on("flame", linear(0, 2)), on("flame_core", radial(4, 0, 300))]},
        "target_base": {"Format": ot.PaintFormat.PaintColrLayers, "Layers": [on("ring", radial(2, 3)), on("ring_mid", solid(4)), on("ring_core", linear(5, 3, 500, 590, 500, 310))]},
    }
    font["COLR"] = buildCOLR(layers, version=1, glyphMap=font.getReverseGlyphMap())
    write(builder, os.path.join(SAMPLES, "Emoji", "Resources", "LetteraSampleColor.bytes"))


def inter(path):
    font = TTFont(path)
    options = Options()
    options.layout_features = ["*"]
    options.name_IDs = ["*"]
    options.notdef_outline = True
    options.glyph_names = False
    options.hinting = False
    subsetter = Subsetter(options)
    subsetter.populate(unicodes=list(range(0x20, 0x7F)) + list(range(0xA0, 0x100)) + [0x2013, 0x2014, 0x2018, 0x2019, 0x201C, 0x201D, 0x2026, 0x20AC, 0x2192])
    subsetter.subset(font)
    # One axis left, so that a slider has one thing to do. Optical size stays at its text setting.
    font = instancer.instantiateVariableFont(font, {"opsz": 14})
    write(font, os.path.join(SAMPLES, "Fonts", "Resources", "LetteraSampleInter.bytes"))


if __name__ == "__main__":
    icons()
    pixel()
    color()
    if len(sys.argv) > 1:
        inter(sys.argv[1])
