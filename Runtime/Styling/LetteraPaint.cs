using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraGradientMode { Srgb, Linear, Oklab, Stepped }
    public enum LetteraPaintSpan { Inherit, Glyph, Line, Range, Block }
    public enum LetteraPaintShape { Inherit, Linear, Radial, Conic }
    public enum LetteraPaintFit { Inherit, Stretch, Contain, Cover, Tile }
    public enum LetteraPaintEdge { Inherit, Clamp, Repeat, Mirror }
    public enum LetteraBlend { Inherit, Normal, Multiply, Screen, Additive, Exclusion }
    public enum LetteraPaintSourceKind { Solid, Gradient, Texture }
    public enum LetteraPaintChoiceKind { Default, Inline, Named }
    public enum LetteraPaintCatalog { Project, Library, Own }

    /// <summary>One color of a gradient, at a position from 0 to 1.</summary>
    [Serializable]
    public struct LetteraGradientStop
    {
        [Range(0, 1)] public float Position;
        public Color32 Color;

        public LetteraGradientStop(float position, Color32 color)
        {
            Position = position;
            Color = color;
        }
    }

    /// <summary>Ordered color stops and how to interpolate between them.</summary>
    [Serializable]
    public class LetteraGradient
    {
        public List<LetteraGradientStop> Stops = new List<LetteraGradientStop>();
        public LetteraGradientMode Mode;

        /// <summary>Finer steps and a fine grain over them, which hide the bands a slow gradient shows at 8 bits. A text that has it draws with a heavier shader.</summary>
        [Tooltip("Finer steps and a fine grain, which hide the bands of a slow gradient. A text with it draws with a slightly heavier shader.")]
        public bool Grain;

        public LetteraGradient() { }

        public LetteraGradient(params LetteraGradientStop[] stops) => Stops.AddRange(stops);

        /// <summary>A gradient with evenly spaced colors.</summary>
        public static LetteraGradient Of(params Color32[] colors)
        {
            var gradient = new LetteraGradient();
            for (int i = 0; i < colors.Length; i++)
                gradient.Stops.Add(new LetteraGradientStop(colors.Length > 1 ? (float)i / (colors.Length - 1) : 0, colors[i]));
            return gradient;
        }

        internal LetteraGradient Copy() => new LetteraGradient { Stops = new List<LetteraGradientStop>(Stops), Mode = Mode, Grain = Grain };
    }

    /// <summary>How a gradient or texture lies on the text. Inherit and NaN contribute nothing at their level.</summary>
    [Serializable]
    public class LetteraPaintSettings
    {
        public LetteraPaintSpan Span;
        public LetteraPaintShape Shape;
        public LetteraPaintFit Fit;
        public LetteraPaintEdge Edge;
        public LetteraBlend Blend;
        public float Angle = float.NaN;
        public float Scale = float.NaN;
        public Vector2 Offset = new Vector2(float.NaN, float.NaN);

        internal LetteraPaintSettings Copy() => (LetteraPaintSettings)MemberwiseClone();
    }

    /// <summary>What a colored surface shows: a color, a gradient or a texture, and how it lies on the text.</summary>
    [Serializable]
    public class LetteraPaint
    {
        public LetteraPaintSourceKind Source;
        public Color32 Color = new Color32(255, 255, 255, 255);
        public LetteraGradient Gradient = new LetteraGradient();
        public Texture2D Texture;
        public LetteraPaintSettings Settings = new LetteraPaintSettings();

        public static LetteraPaint Solid(Color32 color) => new LetteraPaint { Source = LetteraPaintSourceKind.Solid, Color = color };

        public static LetteraPaint Of(LetteraGradient gradient, LetteraPaintSpan span = LetteraPaintSpan.Inherit, float angle = float.NaN) => new LetteraPaint
        {
            Source = LetteraPaintSourceKind.Gradient,
            Gradient = gradient,
            Settings = new LetteraPaintSettings { Span = span, Angle = angle },
        };

        public static LetteraPaint Of(Texture2D texture) => new LetteraPaint { Source = LetteraPaintSourceKind.Texture, Texture = texture };

        internal LetteraPaint Copy() => new LetteraPaint { Source = Source, Color = Color, Gradient = Gradient?.Copy(), Texture = Texture, Settings = Settings?.Copy() };
    }

    /// <summary>A named paint.</summary>
    [Serializable]
    public class LetteraSwatch
    {
        public string Name = "";
        public LetteraPaint Paint = new LetteraPaint();

        public LetteraSwatch() { }

        public LetteraSwatch(string name, LetteraPaint paint)
        {
            Name = name;
            Paint = paint;
        }
    }

    /// <summary>The paint an effect exposes: its default, an inline paint or a named swatch.</summary>
    [Serializable]
    public class LetteraPaintChoice
    {
        public LetteraPaintChoiceKind Kind;
        public LetteraPaint Inline = new LetteraPaint();
        public string SwatchName = "";

        /// <summary>Settings of this effect, between the swatch's and the markup's.</summary>
        public LetteraPaintSettings Settings = new LetteraPaintSettings();

        internal LetteraPaintChoice Copy() => new LetteraPaintChoice { Kind = Kind, Inline = Inline?.Copy(), SwatchName = SwatchName, Settings = Settings?.Copy() };
    }

    /// <summary>A paint with every setting decided, as the mesh pass needs it.</summary>
    internal struct ResolvedPaint
    {
        public const byte CodeSolid = 0, CodeTexture = 10;

        // Added to a gradient's code in the mesh when it has grain; only the grained shader reads it.
        public const int CodeGrain = 16;

        public Color32 Color;
        public bool UsesGlyphColor;     // Solid in the glyph's own color.
        public float RampV;             // Row in the gradient atlas, or in its fine texture for a gradient with grain.
        public byte Code;               // 0 solid, 1-9 gradient (shape * 3 + edge + 1), 10 texture.
        public bool Grain;              // A gradient with grain.
        public LetteraPaintSpan Span;
        public LetteraPaintShape Shape;
        public LetteraPaintFit Fit;
        public LetteraBlend Blend;
        public float Angle, Scale;
        public Vector2 Offset;
        public Texture2D Texture;
        public float TextureAspect;

        public bool IsSolid => Code == CodeSolid;
    }

    /// <summary>Turns the cascade of a paint (swatch, effect, default parameter, tag) into one resolved paint.</summary>
    internal static class LetteraPaintCatalogs
    {
        static readonly HashSet<string> s_WarnedNames = new HashSet<string>();

        public static LetteraSwatch Find(List<LetteraSwatch> swatches, string name)
        {
            if (swatches == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < swatches.Count; i++)
                if (swatches[i] != null && swatches[i].Name == name)
                    return swatches[i];
            return null;
        }

        public static void WarnUnknown(string name)
        {
            if (s_WarnedNames.Add(name)) Debug.LogWarning($"Lettera: no paint is named '{name}'; the default paint is used.");
        }

        /// <summary>
        /// Resolves a paint. A null source means the default paint, which the caller gave as defaultColor
        /// (or the glyph's own color when defaultIsGlyphColor is set).
        /// </summary>
        public static ResolvedPaint Resolve(LetteraPaint source, LetteraPaintSettings effectSettings, in LetteraSlots slots, int projectionSlot, Color32 defaultColor, bool defaultIsGlyphColor)
        {
            var paint = new ResolvedPaint { Color = defaultColor, UsesGlyphColor = defaultIsGlyphColor };
            LetteraPaintSettings swatch = source?.Settings;

            LetteraPaintSpan span = slots.Enum(projectionSlot, LetteraPaintSpan.Inherit);
            LetteraPaintShape shape = slots.Enum(projectionSlot + 1, LetteraPaintShape.Inherit);
            LetteraPaintFit fit = slots.Enum(projectionSlot + 2, LetteraPaintFit.Inherit);
            LetteraPaintEdge edge = slots.Enum(projectionSlot + 3, LetteraPaintEdge.Inherit);
            LetteraBlend blend = slots.Enum(projectionSlot + 4, LetteraBlend.Inherit);
            float angle = slots.Float(projectionSlot + 5, float.NaN);
            float scale = slots.Float(projectionSlot + 6, float.NaN);
            float offsetX = slots.Float(projectionSlot + 7, float.NaN);
            float offsetY = slots.Float(projectionSlot + 8, float.NaN);

            paint.Span = First(span, effectSettings?.Span ?? default, swatch?.Span ?? default, LetteraPaintSpan.Block);
            paint.Shape = First(shape, effectSettings?.Shape ?? default, swatch?.Shape ?? default, LetteraPaintShape.Linear);
            paint.Fit = First(fit, effectSettings?.Fit ?? default, swatch?.Fit ?? default, LetteraPaintFit.Stretch);
            edge = First(edge, effectSettings?.Edge ?? default, swatch?.Edge ?? default, LetteraPaintEdge.Clamp);
            paint.Blend = First(blend, effectSettings?.Blend ?? default, swatch?.Blend ?? default, LetteraBlend.Normal);
            paint.Angle = First(angle, effectSettings?.Angle ?? float.NaN, swatch?.Angle ?? float.NaN, 0);
            paint.Scale = First(scale, effectSettings?.Scale ?? float.NaN, swatch?.Scale ?? float.NaN, 1);
            if (paint.Scale <= 0) paint.Scale = 1;
            paint.Offset.x = First(offsetX, effectSettings?.Offset.x ?? float.NaN, swatch?.Offset.x ?? float.NaN, 0);
            paint.Offset.y = First(offsetY, effectSettings?.Offset.y ?? float.NaN, swatch?.Offset.y ?? float.NaN, 0);

            if (source == null) return paint;
            switch (source.Source)
            {
                case LetteraPaintSourceKind.Solid:
                    paint.Color = source.Color;
                    paint.UsesGlyphColor = false;
                    break;
                case LetteraPaintSourceKind.Gradient:
                    List<LetteraGradientStop> stops = source.Gradient?.Stops;
                    if (stops == null || stops.Count == 0)
                    {
                        paint.Color = new Color32(0, 0, 0, 0);
                        paint.UsesGlyphColor = false;
                    }
                    else if (stops.Count == 1)
                    {
                        paint.Color = stops[0].Color;
                        paint.UsesGlyphColor = false;
                    }
                    else
                    {
                        paint.Color = new Color32(255, 255, 255, 255);
                        paint.UsesGlyphColor = false;
                        paint.Grain = source.Gradient.Grain;
                        paint.RampV = paint.Grain ? GradientAtlas.FineRowOf(source.Gradient) : GradientAtlas.RowOf(source.Gradient);
                        // One turn of a conic gradient is one period, so it has no edge mode.
                        int edgeCode = paint.Shape == LetteraPaintShape.Conic ? 0 : edge - LetteraPaintEdge.Clamp;
                        paint.Code = (byte)((paint.Shape - LetteraPaintShape.Linear) * 3 + edgeCode + 1);
                    }
                    break;
                case LetteraPaintSourceKind.Texture:
                    if (source.Texture != null)
                    {
                        paint.Color = new Color32(255, 255, 255, 255);
                        paint.UsesGlyphColor = false;
                        paint.Texture = source.Texture;
                        paint.TextureAspect = source.Texture.height > 0 ? (float)source.Texture.width / source.Texture.height : 1;
                        paint.Code = ResolvedPaint.CodeTexture;
                    }
                    break;
            }
            return paint;
        }

        static T First<T>(T a, T b, T c, T fallback) where T : struct, Enum
        {
            if (Convert.ToInt32(a) != 0) return a;
            if (Convert.ToInt32(b) != 0) return b;
            return Convert.ToInt32(c) != 0 ? c : fallback;
        }

        static float First(float a, float b, float c, float fallback) => !float.IsNaN(a) ? a : !float.IsNaN(b) ? b : !float.IsNaN(c) ? c : fallback;
    }
}
