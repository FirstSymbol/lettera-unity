# 3D text

`LetteraText3D` builds real geometry from the glyphs' outlines: lit, shadowed, with edges, depth and materials, drawn by the object's own `MeshRenderer`. Markup, fonts, fallback, bidi, Arabic and Indic shaping, wrapping, auto size and motion are the same as a world text's.

[Русская версия](Text3D.ru.md) · Other guides: [Getting Started](GettingStarted.md), [Fonts](Fonts.md), [Motion](Motion.md), [Formulas](Math.md), [Tools](Tools.md)

## Make one

**GameObject > Lettera > Text 3D**, or from code:

```csharp
LetteraText3D solid = go.AddComponent<LetteraText3D>();
solid.Text = "Solid <wave>Text</wave>";
solid.Depth = 0.25f;
solid.FrontBevel = new LetteraBevel { Shape = LetteraBevelShape.Round, Width = 0.03f, Height = 0.03f, Roundness = 0.5f, Segments = 4 };
```

A world text's right click has **Convert To Text 3D**, which keeps its text and styles.

- **Depth** is in local units. **Pivot** says where the text stands in it: front, middle or back.
- **Front Bevel** and **Back Bevel** each take one of nine shapes (Chamfer, Round, Cove, Bead, Step, Two Steps, Ogee, Engraved, or None) or **Custom**, a curve of your own. **Mirror Bevel** gives the back the front's edge. Width and height are in em.
- **Front Face**, **Back Face** and **Sides** switch surfaces off, for a text seen from one side only.
- **Smoothing Angle** decides which edges shade smooth, and **Curve Tolerance** how finely curves are cut.
- **Share Mesh** lets identical texts share one mesh, so they can instance.

Each glyph is built once, whatever the text's depth, and shared by every text with the same font and edges.

## Materials

Five slots: **Face**, **Bevel**, **Side**, **Back Bevel** and **Back**. An empty slot falls back to its neighbour, so one material on Face covers the whole text. `GetSurfaceMaterial` says what a surface ends up with.

The `Lettera/Solid Lit` shader works under Built-in, URP and HDRP; under HDRP it goes through the pipeline's own light loop, shadows, probes and fog included. A material on it has **Edit In Lettera** on its inspector, which opens it in a window of Lettera's own.

### Looks

A **Solid Look** asset holds a depth, edges, materials and layers, to give at once. Seven ship in `Runtime/Text3D/Looks`: Plain, Gold, Chrome, Candy, Stone, Neon and Sign; the inspector's **Look** card applies one with a click. Neon brings a tube around each glyph and Sign a board behind the whole text. Nothing of them enters a build unless a scene uses one. **Assets > Create > Lettera > Solid Look** makes your own.

## Ranges

A range of a solid text takes tags that only a solid text reads. On flat text they do nothing.

| Tag | What it does | Slots |
|---|---|---|
| `<depth>` | A depth of its own | a multiple of the text's depth: `<depth=2>` |
| `<bevel>` | An edge of its own | shape, width, height: `<bevel=chamfer,0.03,0.03>` |
| `<surface>` | Materials of a named set on the text | name, then surfaces to take: `<surface=gold,face,side>` |
| `<pose>` | A fixed pose about each glyph's middle | pitch, yaw, roll, lift in em, scale, and `whole` to turn the range as one body |
| `<bend>`, `<twist>`, `<taper>`, `<ripple>`, `<sphere>` | A deformer measured by the range's own glyphs | degrees or amount; `<bend=60,up>`, `<ripple=0.1,1.5>` for height and length |
| `<revolve>` | Turns glyphs about an axis | rate, step, axis |
| `<swing>` | Swings them from their top edge | degrees, rate, step, axis |
| `<surge>` | A wave toward the viewer and away | amplitude in em, rate, step |
| `<orbit>` | Circles them round where they rest | radius in em, rate, step, axis |
| `<tumble>` | Turns each about an axis of its own | rate, step |
| `<reveal>` | Reveals in depth: `turnin`, `flyin`, `deepen`, `tumblein` | as for any reveal |

Every flat motion moves solids too, and a fade goes through a screen dither, so nothing needs sorting. On flat text the depth reveals are a fade.

**Surface sets.** A text holds named sets of materials; `<surface=gold>` gives a range a set's face, bevel, side, back bevel and back, and `<surface=gold,face,side>` only those named. An empty slot falls back within the set, then to the text's own. The inspector's **Ranges** card lists the sets and the tags a range can take.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class GoldWord : MonoBehaviour
{
    public LetteraSolidLook gold;     // Runtime/Text3D/Looks/Gold, set in the inspector.

    void Start()
    {
        var solid = GetComponent<LetteraText3D>();
        solid.AddSurfaceSet(LetteraSolidSurfaceSet.FromLook("gold", gold));
        solid.Text = "Plain and <surface=gold>golden</surface>";
    }
}
```

## Layers

Layers are extra solids per glyph, word, line or the whole text: an outline grown or shrunk, a plate with rounded corners, or a round tube along each outline like neon; any of them as a band, and an outline or plate with the letters cut through it or engraved into its front. Each has its own place in depth, shift, depth, edge, colour and materials.

```csharp
solid.AddLayer(LetteraSolidLayer.Preset(LetteraSolidLayerPreset.Badge));
```

The presets are Outline, Back Plate, Neon, Badge, Engraved Plate and Slab, also on the **Layers** card's **Add**. After changing a layer in place, call `solid.RefreshSolids()`. A look can carry layers.

## Deformers

A chain of deformers bends the whole text, in order, with its layers, underlines and formulas:

```csharp
solid.AddDeformer(LetteraDeformer.Bend(360));     // A ring.
solid.AddDeformer(LetteraDeformer.Twist(45));
```

Bend, twist, taper, wave, sphere and path. Amounts are relative to the text's own size, so a deformed text keeps its look when resized. A wave can move with a speed, and motion still moves a deformed text without a rebuild. **Deform Detail** cuts surfaces finer where a deformer needs it, so a curve shows as one; the cut never opens a crack. A range's own deformers, from its tags, run before the text's chain.

## Motion from a script

A custom motion in depth derives from `LetteraSolidMotion` and writes `PoseSolid`, which must be pure and allocate nothing:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

[System.Serializable]
public class HopMotion : LetteraSolidMotion
{
    public override void Resolve(in LetteraSlots slots, ref LetteraMotionValues values) { }

    public override void PoseSolid(in LetteraMotionContext context, ref LetteraSolidPose pose) =>
        pose.Lift(Mathf.Abs(Mathf.Sin(context.Phi * Mathf.PI * 2)) * context.Em * 0.3f);
}
```

Bind it to a tag with a style, as any effect: `solid.Styles.Add(LetteraStyle.ForTag(new HopMotion(), "hop"))`. See [Motion](Motion.md) for clocks, units and strength, which every motion has.

For poses no tag describes, `solid.SolidPose` takes an `ILetteraSolidPose`, called for each glyph every frame it is posed, with a transform that acts about the glyph's centre.

## Hit it, and follow a place

```csharp
if (solid.Raycast(ray, out LetteraSolidHit hit))
{
    Debug.Log($"Character {hit.Character}, on its {hit.Anchor.Surface}, at {hit.Point}");
    LetteraSolidAnchor place = hit.Anchor;
    // Any later frame, through motion and deformers:
    if (solid.TryGetPose(place, out Pose pose)) go.transform.SetPositionAndRotation(pose.position, pose.rotation);
}
```

`Raycast` finds the nearest point of the drawn solids, glyphs and layers alike, with no collider. An anchor is held by the triangle it lies in; after a rebuild it is found again on the same glyph. `TryAnchorCharacter` takes one from a point near a character.

## Colliders, copies and baking

- **Colliders.** `LetteraText3DColliders` keeps a box or a convex hull per glyph, or the drawn mesh itself, made again with the mesh and moved with motion and moving deformers. A Rigidbody on the text takes them as one compound body, and `CharacterOf(collider)` says which character a collider stands for. It compiles only with the Physics module.
- **Copies on glyphs.** `LetteraGlyphAttachments` puts prefabs on each glyph's top, bottom or front, along its outline, or scattered over what faces up, sized in em and varied by a seed. They ride their glyph through motion and deformers and hide with it. They are never saved with the scene.
- **Bake.** The right click has **Bake...**: a mesh asset, and if asked a static object that draws it, lightmap UVs, a level-of-detail group of up to four coarser meshes, and FBX when the FBX Exporter package is in the project. Materials made only in memory are saved beside the mesh. One undo takes it all back. **Bake To Mesh Asset** and **Export OBJ** are there too.

## In a Canvas

A solid text is not a `Graphic`, since a Canvas has no light: inside one it draws as a scene object, without masks or layout, and not at all under Screen Space Overlay. For solids in a UI, point a camera at the text with a `RenderTexture` as its target, and show that in a `RawImage`. The 3D Text sample does this.

## In a build

- A scene's Text 3D brings its material and shader into the build. A build with none holds neither.
- A text made from a script with no material needs **Lettera > 3D > Allow Creating From Scripts**, which puts the shader in every build, or a **Default Material** in **Project Settings > FirstSymbol > Lettera > 3D**.
- `LETTERA_NO_3D` removes the module.

## What it does not do

- 2D layers (stroke, shadow, glow) do not draw on a solid. There is no selection or editing.
- A colour glyph draws flat on the front plane, or not at all (**Color Glyphs**). Underline and strikethrough extrude; dashed, dotted and wavy lines extrude as plain boxes. Formulas extrude, with their bars as boxes.
- A gradient paint does not reach a solid's vertices: a solid takes a colour.

## Outlines as paths

A host that draws glyphs itself, such as vector graphics, a plotter or a cut file, can read the outlines of any laid-out text, flat or solid, as paths. `GetGlyphOutlines` is on `LetteraText`, `LetteraWorldText` and `LetteraText3D`, and fills a `LetteraOutlineBuffer`.

```csharp
using System;
using FirstSymbol.Lettera;
using UnityEngine;

public class OutlinesToPaths : MonoBehaviour
{
    readonly LetteraOutlineBuffer m_Buffer = new LetteraOutlineBuffer();

    void Start()
    {
        var text = GetComponent<LetteraWorldText>();
        // Lines only, never farther than a tenth of a unit from the curve.
        int paths = text.GetGlyphOutlines(m_Buffer, new LetteraOutlineOptions { Tolerance = 0.1f });
        foreach (LetteraGlyphPath path in m_Buffer.Paths)
        {
            if (path.Kind == LetteraOutlineKind.NotOutline) continue;   // A bitmap emoji: nothing to draw as a path.
            ReadOnlySpan<LetteraPathCommand> commands = m_Buffer.CommandsOf(path);
            ReadOnlySpan<Vector2> points = m_Buffer.PointsOf(path);
            Debug.Log($"glyph {path.GlyphIndex} of line {path.Line}: {commands.Length} steps, {points.Length} points, fill {path.Fill}");
        }
    }
}
```

The paths are in the text's local space, the one `GetGlyph`, `InkBounds` and `LayoutBounds` use, with y up, so they sit where the text is drawn after line breaking, alignment, justification and curves. Each path is one glyph, or one layer of a colour glyph, and says its glyph's index, its cluster and line, its fill colour, how opaque it is, the stroke the text has on it, its size and its box. Fill with the non-zero winding rule: a letter's hole is a contour turning the other way.

- **Motion.** The state at the time of the call is read: a glyph a wave has lifted is lifted in its path. `IgnoreMotion` reads where layout put it. A glyph an effect has wholly hidden is left out.
- **Curves.** `Tolerance` 0 keeps the font's quadratic and cubic Beziers (`QuadTo` takes a control point and an end point, `CubicTo` two control points and an end point). Above 0 the paths hold lines only, none of the curve farther from them than the tolerance in local units.
- **Colour glyphs.** A COLRv0 glyph gives one path per layer, in the layer's colour (or the text's, where the font leaves it to the text); `NoColorLayers` gives its plain outline. A glyph that is a bitmap, painted (COLRv1) or a browser emoji has no outline: its path is `NotOutline`, with no commands and the cell it fills as its box, and `SkippedGlyphs` counts them.
- **Formulas.** A fraction bar is a path of four points; the glyphs of a formula come after `GlyphCount`.
- **Cost.** The buffer keeps its arrays: a second read of the same size allocates nothing. Reading rebuilds a text that is out of date first.

## Limits

- Bevels narrow and do not ridge: a wide edge on a thin stroke leaves a thin flat strip.
- Under HDRP, solid and world text have no motion vectors, so temporal anti-aliasing and motion blur see a moving glyph as still. No decals and no screen-space reflections on them, and a solid is always forward-lit. The 3D sample's light is set for Built-in and URP. A Custom Effect's shader is written for Built-in and URP only.
- Layers are traced on a grid of **Detail** cells per em, 32 by default, so their corners are round at about a thirtieth of an em. A word's, line's or text's layer stands still while its glyphs move, and does not turn with a glyph's depth pose; an underline under a posed range stays flat. Neon follows each outline, not the stroke's middle. A text with a layer per word, line or text keeps its own mesh, shared with no other.
- Deformers work on the mesh on the CPU, so a moving text with one costs per vertex each frame, and a fine **Deform Detail** multiplies the vertices. A path is a smooth curve through points of the text's own; Unity's Splines package is not read.
- A depth motion's or reveal's turn happens before a deformer, so a glyph spinning on a bent text also bends as it passes along the curve.
- A collider's shape is taken when the text is built: motion and a moving deformer move and turn it, but do not bend it. A Mesh collider cannot be on a moving Rigidbody, and is cooked again each frame it moves. A convex hull fills a glyph's counters.
- Copies along strokes keep a spacing from every copy on the glyph, so a stroke thinner than the spacing gets copies on one side only.
- Lightmap UVs take the second UV set, from which a material mapped across the whole text reads its texture; such a material maps per surface on the baked object, with a warning.
- Solids have run on Windows and in desktop browsers, and not on a phone or a Mac. See [Platforms](Platforms.md).
- Glyph outlines (`GetGlyphOutlines`) carry solid colours: a gradient or texture paint gives its base colour. Underlines, strikethroughs, shadows, glows and other layers are not in the paths, and a stroke is reported as colour, width and alignment, not outlined. COLRv1 and bitmap colour glyphs have no outline. A very long banded text gives the lines it has laid out.
