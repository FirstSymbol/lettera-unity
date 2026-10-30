using System;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace FirstSymbol.Lettera
{
    /// <summary>The shared materials.</summary>
    public static class LetteraMaterials
    {
        static Material s_Ui, s_World;

        public static Material Ui => s_Ui != null ? s_Ui : s_Ui = Create("Lettera/LetteraUI");
        public static Material World => s_World != null ? s_World : s_World = Create("Lettera/LetteraWorld");

        static Material Create(string resource)
        {
            var shader = Resources.Load<Shader>(resource);
            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave, name = resource };
        }
    }

    /// <summary>Once per frame, before rendering: world text rebuilds, batches upload and glyphs reach the GPU.</summary>
    internal static class GlyphAtlasRegistration
    {
        static bool s_Registered;
        static bool s_LoopInstalled;

        struct LetteraUpdate { }

        public static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            int version = LetteraNative.lt_version();
            if (version != LetteraNative.ExpectedVersion)
                Debug.LogError($"Lettera: the native plugin is version {version}, the scripts expect {LetteraNative.ExpectedVersion}. Reinstall the package.");
            Canvas.willRenderCanvases += GlyphAtlas.Flush;
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
                var systems = new PlayerLoopSystem[post.subSystemList.Length + 1];
                int insert = 0;
                for (int s = 0; s < post.subSystemList.Length; s++)
                    if (post.subSystemList[s].type == typeof(PostLateUpdate.FinishFrameRendering))
                        insert = s;
                Array.Copy(post.subSystemList, 0, systems, 0, insert);
                systems[insert] = new PlayerLoopSystem { type = typeof(LetteraUpdate), updateDelegate = Tick };
                Array.Copy(post.subSystemList, insert, systems, insert + 1, post.subSystemList.Length - insert);
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
        }

        /// <summary>Releases native fonts and atlas memory; nothing survives a domain reload.</summary>
        public static void Shutdown()
        {
            LetteraWorldBatcher.Shutdown();
            GlyphAtlas.Shutdown();
            FontFace.DisposeAll();
            LetteraSystemFonts.Reset();
        }
    }
}
