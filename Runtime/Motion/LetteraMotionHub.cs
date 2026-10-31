using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>The time motion runs on. Tests replace it.</summary>
    public static class LetteraMotionClock
    {
        /// <summary>Replaces how much time a tick advances: scaled and unscaled seconds. Null reads Unity's time.</summary>
        public static Func<(float scaled, float unscaled)> Override;

        /// <summary>The most a tick advances, so a page that was in the background does not jump.</summary>
        public static float MaxStep = 0.25f;

        /// <summary>The project's limit, when no test set one.</summary>
        static float Limit => MaxStep != 0.25f ? MaxStep : LetteraSettings.Instance.Motion.MaxStepSeconds;

        internal static void Read(out float scaled, out float unscaled)
        {
            if (Override != null)
            {
                (scaled, unscaled) = Override();
            }
            else
            {
                scaled = Time.deltaTime;
                unscaled = Time.unscaledDeltaTime;
            }
            float limit = Limit;
            if (unscaled > limit) unscaled = limit;
            if (scaled > limit) scaled = limit;
        }
    }

    /// <summary>What ticks once per frame beside the texts: sequencers and the like.</summary>
    internal interface IMotionClient
    {
        /// <summary>False leaves the hub.</summary>
        bool TickMotion(float scaled, float unscaled);
    }

    /// <summary>Runs the pose pass of texts that have something moving. A text with nothing moving is not here at all.</summary>
    internal static class LetteraMotionHub
    {
        static readonly List<TextEngine> s_Engines = new List<TextEngine>();
        static readonly List<IMotionClient> s_Clients = new List<IMotionClient>();
        static bool s_Subscribed;
        static float s_HeldScaled, s_HeldUnscaled;

        internal static int Count => s_Engines.Count;
        internal static int ClientCount => s_Clients.Count;

        public static void Add(TextEngine engine)
        {
            if (!s_Engines.Contains(engine)) s_Engines.Add(engine);
            Subscribe();
        }

        public static void Remove(TextEngine engine) => s_Engines.Remove(engine);

        public static void Add(IMotionClient client)
        {
            if (!s_Clients.Contains(client)) s_Clients.Add(client);
            Subscribe();
        }

        public static void Remove(IMotionClient client) => s_Clients.Remove(client);

        static void Subscribe()
        {
            if (s_Subscribed) return;
            s_Subscribed = true;
            LetteraAccessibility.Changed += OnReducedMotionChanged;
        }

        static void OnReducedMotionChanged()
        {
            foreach (TextEngine engine in s_Engines) engine.MarkPoseDirty();
        }

        public static void Tick()
        {
            if (s_Engines.Count == 0 && s_Clients.Count == 0) return;
            LetteraMotionClock.Read(out float scaled, out float unscaled);
            int cap = LetteraSettings.Instance.Motion.MaxFramesPerSecond;
            if (cap > 0 && LetteraMotionClock.Override == null)
            {
                // Weak devices can spend fewer uploads on this: time still counts, in one step.
                s_HeldScaled += scaled;
                s_HeldUnscaled += unscaled;
                if (s_HeldUnscaled < 1f / cap) return;
                scaled = s_HeldScaled;
                unscaled = s_HeldUnscaled;
                s_HeldScaled = s_HeldUnscaled = 0;
            }
            PollPreferences(unscaled);
            Tick(scaled, unscaled);
        }

        static float s_PollIn;

        // The browser says what the visitor prefers, and it can change while the page is open.
        static void PollPreferences(float unscaled)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            s_PollIn -= unscaled;
            if (s_PollIn > 0) return;
            s_PollIn = 1;
            bool reduced = LetteraMotion_PrefersReduced() != 0;
            if (reduced != s_WebReduced)
            {
                s_WebReduced = reduced;
                LetteraAccessibility.FromSystem(reduced, LetteraAccessibility.SteadyCaret);
            }
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        static bool s_WebReduced;
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int LetteraMotion_PrefersReduced();
#endif

        internal static void Tick(float scaled, float unscaled)
        {
            // A client can leave by itself while it ticks, so what comes back is looked up, not assumed still there.
            for (int i = s_Clients.Count - 1; i >= 0; i--)
            {
                if (i >= s_Clients.Count) continue;
                IMotionClient client = s_Clients[i];
                if (!client.TickMotion(scaled, unscaled)) s_Clients.Remove(client);
            }
            for (int i = s_Engines.Count - 1; i >= 0; i--)
                if (i < s_Engines.Count)
                    s_Engines[i].TickPose(scaled, unscaled);
        }

        public static void Shutdown()
        {
            s_Engines.Clear();
            s_Clients.Clear();
        }
    }
}
