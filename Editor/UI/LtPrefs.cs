using System.Collections.Generic;
using UnityEditor;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>What this user chose in the inspectors. EditorPrefs belong to the user, across every editor and project on the machine, so a test run keeps its own in memory.</summary>
    internal static class LtPrefs
    {
        static Dictionary<string, object> s_Memory;

        /// <summary>On: nothing is read from or written to EditorPrefs. Each turn on starts empty.</summary>
        internal static bool InMemory
        {
            get => s_Memory != null;
            set => s_Memory = value ? new Dictionary<string, object>() : null;
        }

        static T Get<T>(string key, T fallback) => s_Memory.TryGetValue(key, out object value) && value is T typed ? typed : fallback;

        public static bool GetBool(string key, bool fallback) => InMemory ? Get(key, fallback) : EditorPrefs.GetBool(key, fallback);
        public static float GetFloat(string key, float fallback) => InMemory ? Get(key, fallback) : EditorPrefs.GetFloat(key, fallback);
        public static string GetString(string key, string fallback) => InMemory ? Get(key, fallback) : EditorPrefs.GetString(key, fallback);

        public static void SetBool(string key, bool value)
        {
            if (InMemory) s_Memory[key] = value;
            else EditorPrefs.SetBool(key, value);
        }

        public static void SetFloat(string key, float value)
        {
            if (InMemory) s_Memory[key] = value;
            else EditorPrefs.SetFloat(key, value);
        }

        public static void SetString(string key, string value)
        {
            if (InMemory) s_Memory[key] = value;
            else EditorPrefs.SetString(key, value);
        }

        public static void DeleteKey(string key)
        {
            if (InMemory) s_Memory.Remove(key);
            else EditorPrefs.DeleteKey(key);
        }
    }
}
