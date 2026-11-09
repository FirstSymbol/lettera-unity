using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>
    /// What a version does to objects saved by an older one, when a field cannot just carry <c>[FormerlySerializedAs]</c>. Steps run in the editor, when an older object loads, and never on a build.
    /// With no step between its version and the current one an object is stamped in memory only, and opening an old scene or asset leaves it clean: the stamp goes out with its next save.
    /// </summary>
    internal static class LetteraUpgrades
    {
        struct Step
        {
            public int From, To;
            public Action<object> Action;
        }

        static readonly List<Step> s_Steps = new List<Step>();

        /// <summary>Registers a step, usually from an <c>[InitializeOnLoadMethod]</c>. The action gets the text, style or asset, and a Unity object is marked changed after it. A style's step reaches disk when the object holding it is saved, and until then runs again on each load, so it must be able to.</summary>
        public static void Register(int from, int to, Action<object> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (from < 0 || to <= from) throw new ArgumentException("A step goes from an older version to a newer one.");
            s_Steps.Add(new Step { From = from, To = to, Action = action });
        }

        internal static void Unregister(Action<object> action) => s_Steps.RemoveAll(step => step.Action == action);

        [InitializeOnLoadMethod]
        static void Hook() => EditorApplication.update += Tick;

        static void Tick()
        {
            if (LetteraStamps.Queued) RunPending();
        }

        /// <summary>Brings everything loaded so far up to date, and says how many objects it looked at.</summary>
        internal static int RunPending()
        {
            ILetteraStamped[] taken = LetteraStamps.Take();
            foreach (ILetteraStamped target in taken) Bring(target);
            return taken.Length;
        }

        static void Bring(ILetteraStamped target)
        {
            if (target is UnityEngine.Object gone && gone == null)
            {
                LetteraStamps.Done(target);
                return;
            }
            int version = target.Version;
            bool changed = false;
            while (version < LetteraSerializedVersions.Current)
            {
                int next = -1;
                for (int i = 0; i < s_Steps.Count; i++)
                    if (s_Steps[i].From >= version && (next < 0 || s_Steps[i].From < s_Steps[next].From))
                        next = i;
                if (next < 0) break;
                try
                {
                    s_Steps[next].Action(target);
                }
                catch (Exception e)
                {
                    // It stays older, so that a save does not stamp what was not upgraded, and the next load tries again.
                    Debug.LogException(e, target as UnityEngine.Object);
                    return;
                }
                changed = true;
                version = s_Steps[next].To;
            }
            target.Version = LetteraSerializedVersions.Current;
            LetteraStamps.Done(target);
            if (changed && target is UnityEngine.Object done) EditorUtility.SetDirty(done);
        }
    }
}
