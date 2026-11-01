using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Lettera
{
    [Flags]
    public enum LetteraSurfaceTier { None = 0, UiPlain = 1, UiStyled = 2, WorldPlain = 4, WorldStyled = 8, WorldLit = 16, WorldLitStyled = 32, All = 63 }

    public struct LetteraWarmUpOptions
    {
        /// <summary>Also the variant a marquee, a viewport or a mask clips with.</summary>
        public bool Clip;

        /// <summary>Also the variant that cuts low alpha.</summary>
        public bool AlphaClip;

        /// <summary>The format of the target drawn to. Default is what the camera has.</summary>
        public RenderTextureFormat? TargetFormat;
    }

    /// <summary>One shader state warm-up will touch.</summary>
    public readonly struct LetteraShaderVariant
    {
        public readonly LetteraSurfaceTier Tier;
        public readonly bool Clip, AlphaClip;

        public LetteraShaderVariant(LetteraSurfaceTier tier, bool clip, bool alphaClip)
        {
            Tier = tier;
            Clip = clip;
            AlphaClip = alphaClip;
        }
    }

    /// <summary>A warm-up going on. Runs frame by frame when asked to.</summary>
    public sealed class LetteraWarmUp
    {
        internal readonly List<LetteraShaderVariant> Variants;
        internal readonly RenderTextureFormat Format;

        internal LetteraWarmUp(List<LetteraShaderVariant> variants, RenderTextureFormat format)
        {
            Variants = variants;
            Format = format;
        }

        public int VariantCount => Variants.Count;
        public int VariantsDone { get; internal set; }
        public bool IsDone => VariantsDone >= Variants.Count;
        public float Progress => Variants.Count == 0 ? 1 : (float)VariantsDone / Variants.Count;

        /// <summary>Raised once, when the last variant is done.</summary>
        public event Action Done;

        internal void RaiseDone() => Done?.Invoke();
    }

    /// <summary>
    /// The first text of a session pays once for what every later one finds ready: Lettera's tables, its material, the atlas page and the glyphs. This pays for it at a moment you choose.
    /// The glyphs are made at run time and nothing is added to a build.
    /// </summary>
    public static class LetteraTextWarmUp
    {
        /// <summary>The printable characters of ASCII, the default set.</summary>
        public const string Latin = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

        /// <summary>
        /// Readies a font and a set of characters at one size, so the first text drawn with them costs what a later one does. Call it behind a loading screen; text under 72 units and text over it use different glyphs.
        /// A character the font lacks takes the fallback chain, as it would in a text. Null characters mean <see cref="Latin"/>. Call <see cref="LetteraShaderWarmUp"/> as well to have the first draw's shader ready.
        /// </summary>
        public static void Prepare(LetteraFontAsset font, string characters = null, float size = 18f, LetteraFontStack stack = null)
        {
            GlyphAtlasRegistration.EnsureRegistered();
            _ = LetteraMaterials.Ui;
            TextEngine.PrepareFor(font, stack, characters ?? Latin, size);
            GlyphAtlas.Flush();
        }
    }

    /// <summary>
    /// The first draw of a shader variant can stall a frame. This draws each one at a moment you choose, such as behind a loading screen, on a tiny target and with
    /// the vertex layout text really has, since on modern graphics APIs the state depends on that too. Nothing runs unless asked, and a text that never asks pays nothing.
    /// </summary>
    public static class LetteraShaderWarmUp
    {
        /// <summary>Every variant a set of tiers and options means.</summary>
        public static List<LetteraShaderVariant> AllVariantsOf(LetteraSurfaceTier tiers, LetteraWarmUpOptions options = default)
        {
            var variants = new List<LetteraShaderVariant>();
            foreach (LetteraSurfaceTier tier in new[] { LetteraSurfaceTier.UiPlain, LetteraSurfaceTier.UiStyled, LetteraSurfaceTier.WorldPlain, LetteraSurfaceTier.WorldStyled, LetteraSurfaceTier.WorldLit, LetteraSurfaceTier.WorldLitStyled })
            {
                if ((tiers & tier) == 0) continue;
                variants.Add(new LetteraShaderVariant(tier, false, false));
                // Clipping is a keyword of the UI shaders only.
                if (tier != LetteraSurfaceTier.UiPlain && tier != LetteraSurfaceTier.UiStyled) continue;
                if (options.Clip) variants.Add(new LetteraShaderVariant(tier, true, false));
                if (options.AlphaClip) variants.Add(new LetteraShaderVariant(tier, false, true));
            }
            return variants;
        }

        static RenderTextureFormat FormatOf(in LetteraWarmUpOptions options) => options.TargetFormat ?? (Camera.main != null && Camera.main.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default);

        /// <summary>Draws them all now, in this frame.</summary>
        public static LetteraWarmUp Run(LetteraSurfaceTier tiers, LetteraWarmUpOptions options = default)
        {
            var warmUp = new LetteraWarmUp(AllVariantsOf(tiers, options), FormatOf(options));
            Step(warmUp, warmUp.VariantCount);
            return warmUp;
        }

        sealed class Progressive : IMotionClient
        {
            public LetteraWarmUp WarmUp;
            public int PerFrame;

            public bool TickMotion(float scaled, float unscaled)
            {
                Step(WarmUp, PerFrame);
                return !WarmUp.IsDone;
            }
        }

        /// <summary>A few each frame, so frames stay even. On the web compiling always blocks, so this is what a web project should call.</summary>
        public static LetteraWarmUp RunProgressively(LetteraSurfaceTier tiers, int variantsPerFrame = 1, LetteraWarmUpOptions options = default)
        {
            var warmUp = new LetteraWarmUp(AllVariantsOf(tiers, options), FormatOf(options));
            if (warmUp.VariantCount == 0 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Step(warmUp, warmUp.VariantCount);
                return warmUp;
            }
            LetteraMotionHub.Add(new Progressive { WarmUp = warmUp, PerFrame = Mathf.Max(variantsPerFrame, 1) });
            return warmUp;
        }

        internal static void Step(LetteraWarmUp warmUp, int count)
        {
            if (warmUp.IsDone) return;
            bool draws = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            for (int i = 0; i < count && !warmUp.IsDone; i++)
            {
                if (draws) Draw(warmUp.Variants[warmUp.VariantsDone], warmUp.Format);
                warmUp.VariantsDone++;
            }
            if (warmUp.IsDone) warmUp.RaiseDone();
        }

        static Mesh s_Mesh;

        // One triangle with every stream text uses.
        static Mesh TriangleMesh()
        {
            if (s_Mesh != null) return s_Mesh;
            s_Mesh = new Mesh { name = "Lettera Warm-Up", hideFlags = HideFlags.HideAndDontSave };
            s_Mesh.SetVertices(new[] { new Vector3(-1, -1), new Vector3(0, 1), new Vector3(1, -1) });
            s_Mesh.SetColors(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) });
            var zero = new[] { Vector4.zero, Vector4.zero, Vector4.zero };
            s_Mesh.SetUVs(0, zero);
            s_Mesh.SetUVs(1, zero);
            s_Mesh.SetUVs(2, zero);
            s_Mesh.SetUVs(3, zero);
            s_Mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            return s_Mesh;
        }

        static void Draw(in LetteraShaderVariant variant, RenderTextureFormat format)
        {
            bool styled = variant.Tier == LetteraSurfaceTier.UiStyled || variant.Tier == LetteraSurfaceTier.WorldStyled || variant.Tier == LetteraSurfaceTier.WorldLitStyled;
            Material source = variant.Tier == LetteraSurfaceTier.UiPlain || variant.Tier == LetteraSurfaceTier.UiStyled ? LetteraMaterials.Ui
                : variant.Tier == LetteraSurfaceTier.WorldPlain || variant.Tier == LetteraSurfaceTier.WorldStyled ? LetteraMaterials.World : LetteraMaterials.WorldLit;
            if (source == null) return;
            var material = new Material(LetteraMaterials.Variant(source, styled, LetteraBlend.Normal, null)) { hideFlags = HideFlags.HideAndDontSave };
            if (variant.Clip) material.EnableKeyword("UNITY_UI_CLIP_RECT");
            if (variant.AlphaClip) material.EnableKeyword("UNITY_UI_ALPHACLIP");

            RenderTexture target = RenderTexture.GetTemporary(4, 4, 16, format);
            var buffer = new CommandBuffer { name = "Lettera Warm-Up" };
            buffer.SetRenderTarget(target);
            buffer.DrawMesh(TriangleMesh(), Matrix4x4.identity, material, 0, 0);
            Graphics.ExecuteCommandBuffer(buffer);
            buffer.Dispose();
            RenderTexture.ReleaseTemporary(target);
            if (Application.isPlaying) UnityEngine.Object.Destroy(material);
            else UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
