using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;

namespace FirstSymbol.Lettera
{
    /// <summary>The shared materials.</summary>
    public static class LetteraMaterials
    {
        static Material s_Ui, s_World, s_WorldLit;

        public static Material Ui => s_Ui != null ? s_Ui : s_Ui = Create("Lettera/LetteraUI");
        public static Material World => s_World != null ? s_World : s_World = Create("Lettera/LetteraWorld");
        public static Material WorldLit => s_WorldLit != null ? s_WorldLit : s_WorldLit = Create("Lettera/LetteraWorldLit");

        static ResourceRequest s_UiAhead, s_WorldAhead;

        // With the settings, as a player starts: the shaders load on Unity's loading thread before the first text asks for its material.
        internal static void LoadAhead()
        {
            s_UiAhead = Resources.LoadAsync<Shader>("Lettera/LetteraUI");
            s_WorldAhead = Resources.LoadAsync<Shader>("Lettera/LetteraWorld");
        }

        /// <summary>True when the UI material would not wait for its shader's load begun at start.</summary>
        internal static bool UiLoadedAhead => s_Ui != null || s_UiAhead == null || s_UiAhead.isDone;

        static Material Create(string resource)
        {
            Shader shader = resource == "Lettera/LetteraUI" ? Take(ref s_UiAhead) : resource == "Lettera/LetteraWorld" ? Take(ref s_WorldAhead) : null;
            if (shader == null) shader = Resources.Load<Shader>(resource);
            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave, name = resource };
        }

        // Reading the asset of a request still loading waits for it, as a load would.
        static Shader Take(ref ResourceRequest ahead)
        {
            Shader shader = ahead != null ? ahead.asset as Shader : null;
            ahead = null;
            return shader;
        }

        static readonly int s_SrcBlend = Shader.PropertyToID("_SrcBlend");
        static readonly int s_DstBlend = Shader.PropertyToID("_DstBlend");
        static readonly int s_PaintTex = Shader.PropertyToID("_LetteraPaintTex");
        static readonly Dictionary<(Material, bool, LetteraBlend, Texture2D, bool, bool), Material> s_Variants = new Dictionary<(Material, bool, LetteraBlend, Texture2D, bool, bool), Material>();

        /// <summary>
        /// The material a stretch of text draws with: a variant of a base material with the styled shader tier,
        /// a blend and a paint texture. Variants are shared across texts.
        /// </summary>
        // Merged layers take a variant of their own: plain glyphs and merged quads, with none of the styled path's weight on the rest.
        // Shared layers take another: plain glyphs whose colour and edge are flat, read from the corner that leads each triangle.
        // A text whose gradients have grain takes the styled tier's grained variant, so no other text pays for the grain.
        internal static Material Variant(Material source, bool styled, LetteraBlend blend, Texture2D texture, bool merged = false, bool shared = false, bool grained = false)
        {
            if (blend == LetteraBlend.Inherit) blend = LetteraBlend.Normal;
            shared &= !styled && blend == LetteraBlend.Normal && texture == null;
            if (!styled && blend == LetteraBlend.Normal && texture == null && !shared) return source;
            merged &= styled && blend == LetteraBlend.Normal && texture == null;
            // A material of the user's own whose shader has no grained variant keeps its styled one.
            grained = grained && styled && !merged && source.shader != null && source.shader.keywordSpace.FindKeyword("LETTERA_GRAINED").isValid;
            var key = (source, styled, blend, texture, merged || shared, grained);
            if (s_Variants.TryGetValue(key, out Material variant) && variant != null) return variant;
            variant = new Material(source) { hideFlags = HideFlags.HideAndDontSave, name = source.name + (merged ? " Merged" : shared ? " Shared" : grained ? " Grained" : " Variant") };
            if (shared) variant.EnableKeyword("LETTERA_LAYERED");
            else if (merged) variant.EnableKeyword("LETTERA_MERGED");
            else if (grained) variant.EnableKeyword("LETTERA_GRAINED");
            else if (styled) variant.EnableKeyword("LETTERA_STYLED");
            if (texture != null) variant.SetTexture(s_PaintTex, texture);
            SetBlend(variant, blend);
            s_Variants[key] = variant;
            return variant;
        }

        // The shaders output premultiplied color.
        static void SetBlend(Material material, LetteraBlend blend)
        {
            BlendMode src = BlendMode.One, dst = BlendMode.OneMinusSrcAlpha;
            switch (blend)
            {
                case LetteraBlend.Multiply: src = BlendMode.DstColor; break;
                case LetteraBlend.Screen: dst = BlendMode.OneMinusSrcColor; break;
                case LetteraBlend.Additive: dst = BlendMode.One; break;
                case LetteraBlend.Exclusion: src = BlendMode.OneMinusDstColor; dst = BlendMode.OneMinusSrcColor; break;
            }
            material.SetFloat(s_SrcBlend, (float)src);
            material.SetFloat(s_DstBlend, (float)dst);
        }
    }

    /// <summary>Once per frame, before rendering: world text rebuilds, batches upload and glyphs reach the GPU.</summary>
    internal static class GlyphAtlasRegistration
    {
        static bool s_Registered;
        static bool s_LoopInstalled;

        struct LetteraUpdate { }
        struct LetteraMotionUpdate { }

        public static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            int version = LetteraNative.Version;
#if UNITY_EDITOR
            // An editor holds the library it loaded, so after an update this is the normal state until it restarts: what needs the new one just waits.
            if (version < LetteraNative.CurrentVersion && version >= LetteraNative.MinimumVersion)
                Debug.LogWarning("Lettera: restart the editor to load the new native plugin. COLRv1 color fonts and a formula's true spacing wait for that; formulas use fallback layout until then, and everything else works.");
            else
#endif
            if (version != LetteraNative.CurrentVersion)
                Debug.LogError($"Lettera: the native plugin is version {version}, the scripts expect {LetteraNative.CurrentVersion}. Reinstall the package.");
            TextEngine.FontsGone = false;
            Canvas.willRenderCanvases += GlyphAtlas.Flush;
            LetteraOsPreferences.Install();
            LetteraMemory.Install();
            InstallLoop();
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            UnityEditor.EditorApplication.quitting += Shutdown;
            UnityEditor.EditorApplication.update += EditorTick;
#else
            Application.focusChanged += OnFocusChanged;
#endif
        }

#if !UNITY_EDITOR
        // A GL context does not survive every pause; the atlas comes back from its CPU pages.
        static void OnFocusChanged(bool focused)
        {
            if (focused && Application.platform == RuntimePlatform.Android &&
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3)
                GlyphAtlas.RestoreTextures();
        }
#endif

#if UNITY_EDITOR
        static void EditorTick()
        {
            if (Application.isPlaying) return;
            // Edit mode runs the player loop only on demand, so world text updates from here.
            LetteraWorldBatcher.Update();
            GlyphAtlas.Flush();
        }
#endif

        static void InstallLoop()
        {
            if (s_LoopInstalled) return;
            s_LoopInstalled = true;
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < root.subSystemList.Length; i++)
            {
                if (root.subSystemList[i].type != typeof(PostLateUpdate)) continue;
                PlayerLoopSystem post = root.subSystemList[i];
                var systems = new PlayerLoopSystem[post.subSystemList.Length + 2];
                int insert = 0;
                for (int s = 0; s < post.subSystemList.Length; s++)
                    if (post.subSystemList[s].type == typeof(PostLateUpdate.FinishFrameRendering))
                        insert = s;
                // Motion runs first, so whatever scripts and animators wrote this frame lands in it, and before canvases update.
                systems[0] = new PlayerLoopSystem { type = typeof(LetteraMotionUpdate), updateDelegate = LetteraMotionHub.Tick };
                Array.Copy(post.subSystemList, 0, systems, 1, insert);
                systems[insert + 1] = new PlayerLoopSystem { type = typeof(LetteraUpdate), updateDelegate = Tick };
                Array.Copy(post.subSystemList, insert, systems, insert + 2, post.subSystemList.Length - insert);
                post.subSystemList = systems;
                root.subSystemList[i] = post;
                PlayerLoop.SetPlayerLoop(root);
                return;
            }
        }

        static void Tick()
        {
            LetteraWorldBatcher.Update();
            GlyphAtlas.Flush();
            LetteraMemory.Tick();
        }

        /// <summary>Releases native fonts and atlas memory; nothing survives a domain reload.</summary>
        public static void Shutdown()
        {
            // Fonts go below. Unity can still rebuild a canvas after this, from an inspector closing during a domain reload, and no text may reach for them.
            TextEngine.FontsGone = true;
            LetteraWorkers.Shutdown();
            LetteraMotionHub.Shutdown();
            LetteraWorldBatcher.Shutdown();
            GlyphAtlas.Shutdown();
            GradientAtlas.Shutdown();
            FilterAtlas.Shutdown();
            FontFace.DisposeAll();
            LetteraSystemFonts.Reset();
            // Its blocks are native and would outlive a domain reload.
            TextEngine.ClearParagraphCache();
        }
    }
}
