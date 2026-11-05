using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>What the system asks of motion and the caret. Read at startup and when the app comes back to the front.</summary>
    internal static class LetteraOsPreferences
    {
        static bool s_Installed;

        internal static void Install()
        {
            if (s_Installed) return;
            s_Installed = true;
            Application.focusChanged += focused =>
            {
                if (focused) Refresh();
            };
            Refresh();
        }

        internal static void Refresh()
        {
            if (!LetteraPlatform.IsActive(LetteraNativeFeature.OsPreferences)) return;
            try
            {
                if (Read(out bool reducedMotion, out bool steadyCaret)) LetteraAccessibility.FromSystem(reducedMotion, steadyCaret);
            }
            catch (Exception e)
            {
                LetteraPlatform.Report(LetteraNativeFeature.OsPreferences, "The system's preferences could not be read: " + e.Message);
            }
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        const uint SPI_GETCLIENTAREAANIMATION = 0x1042, BlinkNever = 0xFFFFFFFF;
        [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfoW(uint action, uint param, out int value, uint ini);
        [DllImport("user32.dll")] static extern uint GetCaretBlinkTime();

        static bool Read(out bool reducedMotion, out bool steadyCaret)
        {
            reducedMotion = SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0, out int animate, 0) && animate == 0;
            steadyCaret = GetCaretBlinkTime() == BlinkNever;
            return true;
        }
#elif UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int LetteraMotion_PrefersReduced();

        static bool Read(out bool reducedMotion, out bool steadyCaret)
        {
            reducedMotion = steadyCaret = LetteraMotion_PrefersReduced() != 0;
            return true;
        }
#elif UNITY_ANDROID && !UNITY_EDITOR
        static bool Read(out bool reducedMotion, out bool steadyCaret)
        {
            using (var bridge = new AndroidJavaClass("com.firstsymbol.lettera.LetteraBridge"))
                reducedMotion = steadyCaret = bridge.CallStatic<bool>("reducedMotion", UnityEngine.Android.AndroidApplication.currentActivity);
            return true;
        }
#elif UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int LetteraIos_ReduceMotion();

        static bool Read(out bool reducedMotion, out bool steadyCaret)
        {
            reducedMotion = steadyCaret = LetteraIos_ReduceMotion() != 0;
            return true;
        }
#else
        static bool Read(out bool reducedMotion, out bool steadyCaret)
        {
            reducedMotion = steadyCaret = false;
            return false;
        }
#endif

        /// <summary>The one figure a check can print: what this machine says now.</summary>
        internal static string Describe()
        {
            bool read = Read(out bool reducedMotion, out bool steadyCaret);
            return read ? "motion=" + reducedMotion + " caret=" + steadyCaret : "none";
        }
    }
}
