using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraNativeFeature { TextInput, Replica, Clipboard, SystemMenu, SystemLoupe, PasteControl, OsPreferences }

    public enum LetteraGate { Auto, On, Off }

    public enum LetteraPlatformKind { WindowsPlayer, WindowsEditor, OtherEditor, Web, Android, IOS, MacPlayer, LinuxPlayer, TvOS, VisionOS, Uwp }

    /// <summary>Per feature, whether a project wants what the platform offers. Auto takes the shipped default, which is off for what has never run on a platform.</summary>
    [Serializable]
    public class LetteraPlatformSettings
    {
        public LetteraGate TextInput, Replica, Clipboard, SystemMenu, SystemLoupe, PasteControl, OsPreferences;

        [Tooltip("Compiles the real iOS code into an iOS build. Off keeps a stub, so code nothing here has compiled cannot fail your Xcode build.")]
        public bool IosNativeInBuild;

        [Tooltip("The largest pasted picture, in bytes. A larger one is left out.")]
        public int PasteImageMaxBytes = 16 * 1024 * 1024;

        [Tooltip("In a browser, a selection longer than this many bytes copies as plain text only, so a copy never holds the frame up.")]
        public int WebCopySyncLimit = 1024 * 1024;

        internal LetteraGate Of(LetteraNativeFeature feature)
        {
            switch (feature)
            {
                case LetteraNativeFeature.TextInput: return TextInput;
                case LetteraNativeFeature.Replica: return Replica;
                case LetteraNativeFeature.Clipboard: return Clipboard;
                case LetteraNativeFeature.SystemMenu: return SystemMenu;
                case LetteraNativeFeature.SystemLoupe: return SystemLoupe;
                case LetteraNativeFeature.PasteControl: return PasteControl;
                default: return OsPreferences;
            }
        }
    }

    /// <summary>
    /// What this platform's own facilities may be used for. A feature that has never run on a platform ships off there: nothing here can catch a native crash, and
    /// the release that turns one on comes after someone has seen it work. Set up on first use by a field, never by a plain label.
    /// </summary>
    public static class LetteraPlatform
    {
        /// <summary>Replaces the answer for tests: a feature, and what to say.</summary>
        internal static Func<LetteraNativeFeature, bool?> Override;

        /// <summary>Which platform to answer for, in tests.</summary>
        internal static LetteraPlatformKind? KindOverride;

        /// <summary>A host failed, or said which route it took.</summary>
        public static event Action<LetteraNativeFeature, string> Reported;

        static readonly HashSet<LetteraNativeFeature> s_Reported = new HashSet<LetteraNativeFeature>();
        static readonly HashSet<LetteraNativeFeature> s_Failed = new HashSet<LetteraNativeFeature>();
        static readonly Dictionary<LetteraNativeFeature, string> s_FailedWhy = new Dictionary<LetteraNativeFeature, string>();

        internal static LetteraPlatformKind Kind
        {
            get
            {
                if (KindOverride.HasValue) return KindOverride.Value;
#if UNITY_EDITOR_WIN
                return LetteraPlatformKind.WindowsEditor;
#elif UNITY_EDITOR
                return LetteraPlatformKind.OtherEditor;
#elif UNITY_WEBGL
                return LetteraPlatformKind.Web;
#elif UNITY_ANDROID
                return LetteraPlatformKind.Android;
#elif UNITY_IOS
                return LetteraPlatformKind.IOS;
#elif UNITY_TVOS
                return LetteraPlatformKind.TvOS;
#elif UNITY_VISIONOS
                return LetteraPlatformKind.VisionOS;
#elif UNITY_WSA
                return LetteraPlatformKind.Uwp;
#elif UNITY_STANDALONE_WIN
                return LetteraPlatformKind.WindowsPlayer;
#elif UNITY_STANDALONE_OSX
                return LetteraPlatformKind.MacPlayer;
#else
                return LetteraPlatformKind.LinuxPlayer;
#endif
            }
        }

        // The phase's central safety decision. When a platform's run has passed on a device, its line turns on in a later release.
        internal static bool ShippedDefault(LetteraNativeFeature feature, LetteraPlatformKind kind)
        {
            switch (feature)
            {
                case LetteraNativeFeature.TextInput:
                case LetteraNativeFeature.OsPreferences:
                    return kind == LetteraPlatformKind.WindowsPlayer || kind == LetteraPlatformKind.Web;
                case LetteraNativeFeature.Replica:
                    return kind == LetteraPlatformKind.Web;
                case LetteraNativeFeature.Clipboard:
                    return kind == LetteraPlatformKind.WindowsPlayer || kind == LetteraPlatformKind.WindowsEditor || kind == LetteraPlatformKind.Web;
                default:
                    return false;
            }
        }

        /// <summary>Whether a feature is to be used here, now.</summary>
        public static bool IsActive(LetteraNativeFeature feature) => Decide(feature, out _);

        /// <summary>Why a feature is on or off, for an inspector and a device run.</summary>
        public static string Why(LetteraNativeFeature feature)
        {
            Decide(feature, out string why);
            return why;
        }

        static bool Decide(LetteraNativeFeature feature, out string why)
        {
            bool? forced = Override?.Invoke(feature);
            if (forced.HasValue)
            {
                why = "Set by a test.";
                return forced.Value;
            }
            if (s_Failed.Contains(feature))
            {
                why = s_FailedWhy[feature];
                return false;
            }
            LetteraGate gate = LetteraSettings.Instance.Platform.Of(feature);
            if (gate == LetteraGate.Off)
            {
                why = "Turned off in the project settings.";
                return false;
            }
            bool on = gate == LetteraGate.On || ShippedDefault(feature, Kind);
            if (!on)
            {
                why = "Off by default on this platform, since it has not been seen to work on one yet. A project can turn it on in the settings.";
                return false;
            }
            if (LetteraPlatformGuard.Tripped(feature))
            {
                why = "Off for this version of the app: its last two uses did not finish.";
                return false;
            }
            why = gate == LetteraGate.On ? "Turned on in the project settings." : "On by default on this platform.";
            return true;
        }

        static readonly List<string> s_Reports = new List<string>();

        /// <summary>Everything reported so far this run, for a device check to send back.</summary>
        public static IReadOnlyList<string> Reports => s_Reports;

        /// <summary>A host says something went wrong or which way it went. Each feature logs once.</summary>
        public static void Report(LetteraNativeFeature feature, string message)
        {
            // A host that says the same thing at every focus would fill the list with it.
            string line = feature + ": " + message;
            if (s_Reports.Count < 256 && !s_Reports.Contains(line)) s_Reports.Add(line);
            if (s_Reported.Add(feature)) Debug.Log($"Lettera: {feature}: {message}");
            Reported?.Invoke(feature, message);
        }

        /// <summary>A host failed: this feature stays off for the rest of the run, and what uses it takes the fallback.</summary>
        public static void MarkFailed(LetteraNativeFeature feature, string message)
        {
            s_Failed.Add(feature);
            s_FailedWhy[feature] = "It failed in this run: " + message;
            Report(feature, message);
        }

        internal static void ResetForTests()
        {
            s_Reported.Clear();
            s_Failed.Clear();
            s_FailedWhy.Clear();
            Override = null;
            KindOverride = null;
        }
    }

    /// <summary>
    /// A native crash cannot be caught, so use leaves a mark that a clean end removes. A mark still there at the next launch means the last use did not finish.
    /// One is forgiven, since a user may just have killed the app; two in a row turn the feature off for this version of the app.
    /// </summary>
    internal static class LetteraPlatformGuard
    {
        internal interface IFiles
        {
            string Read(string name);
            void Write(string name, string text);
            void Delete(string name);
        }

        sealed class RealFiles : IFiles
        {
            static string PathOf(string name) => Path.Combine(Application.persistentDataPath, "Lettera", name);

            public string Read(string name)
            {
                try
                {
                    string path = PathOf(name);
                    return File.Exists(path) ? File.ReadAllText(path) : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            public void Write(string name, string text)
            {
                try
                {
                    string path = PathOf(name);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, text);
                }
                catch (Exception)
                {
                    // A mark that cannot be written blocks nothing.
                }
            }

            public void Delete(string name)
            {
                try
                {
                    File.Delete(PathOf(name));
                }
                catch (Exception)
                {
                    // Nothing depends on this having worked.
                }
            }
        }

        internal static IFiles Files = new RealFiles();
        internal static Func<string> Version = () => Application.version;
        internal const int TripsToDisable = 2;
        static readonly HashSet<LetteraNativeFeature> s_Checked = new HashSet<LetteraNativeFeature>();
        static readonly HashSet<LetteraNativeFeature> s_Off = new HashSet<LetteraNativeFeature>();

        static bool Applies
        {
            get
            {
                LetteraPlatformKind kind = LetteraPlatform.Kind;
                return kind == LetteraPlatformKind.Android || kind == LetteraPlatformKind.IOS || kind == LetteraPlatformKind.MacPlayer;
            }
        }

        static string NameOf(LetteraNativeFeature feature) => "guard-" + feature;

        static void Parse(string text, out string version, out int count)
        {
            version = null;
            count = 0;
            if (text == null) return;
            int bar = text.LastIndexOf('|');
            if (bar <= 0) return;
            version = text.Substring(0, bar);
            int.TryParse(text.Substring(bar + 1), out count);
        }

        internal static bool Tripped(LetteraNativeFeature feature)
        {
            if (!Applies) return false;
            if (s_Checked.Add(feature))
            {
                Parse(Files.Read(NameOf(feature)), out string version, out int count);
                // A new version of the app gets a new chance.
                if (version == Version() && count >= TripsToDisable) s_Off.Add(feature);
            }
            return s_Off.Contains(feature);
        }

        /// <summary>Before a guarded feature's first use in a process.</summary>
        internal static void Enter(LetteraNativeFeature feature)
        {
            if (!Applies) return;
            Parse(Files.Read(NameOf(feature)), out string version, out int count);
            Files.Write(NameOf(feature), Version() + "|" + (version == Version() ? count + 1 : 1));
        }

        /// <summary>A clean end: the mark goes.</summary>
        internal static void Leave(LetteraNativeFeature feature)
        {
            if (Applies) Files.Delete(NameOf(feature));
        }

        internal static void ResetForTests()
        {
            s_Checked.Clear();
            s_Off.Clear();
            Files = new RealFiles();
            Version = () => Application.version;
        }
    }
}
