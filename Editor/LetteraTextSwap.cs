using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>
    /// Turns one text component into another on the same object as one undo step: the components that work on the text are set aside and come back
    /// with their values, and every reference that open scenes and the prefab being edited held to any of them points at its successor.
    /// </summary>
    internal static class LetteraTextSwap
    {
        /// <summary>Components that find their text when they wake and keep it, so they must come back after the text is replaced.</summary>
        internal static readonly Type[] TextBound =
        {
            typeof(LetteraSelectable), typeof(LetteraEditable), typeof(LetteraSequencer), typeof(LetteraRangeNavigator), typeof(LetteraSourceLoader), typeof(LetteraParamLink),
        };

        /// <summary>What the last swap did, for a test or a line in a log.</summary>
        internal struct Report
        {
            public int Carried, Retargeted, Unfit;
        }

        internal static Report Last;

        struct Reference
        {
            public Object Holder;
            public int HolderId;
            public string Path;
            public int Target;
        }

        static IEnumerable<Type> Required(Type type)
        {
            foreach (RequireComponent require in type.GetCustomAttributes(typeof(RequireComponent), true))
            {
                if (require.m_Type0 != null) yield return require.m_Type0;
                if (require.m_Type1 != null) yield return require.m_Type1;
                if (require.m_Type2 != null) yield return require.m_Type2;
            }
        }

        static bool Needs(Component holder, Component other) => Required(holder.GetType()).Any(t => t.IsInstanceOfType(other));

        /// <summary>The components on the text's object that need it, by a requirement or by binding to it, in their order on the object.</summary>
        internal static List<Component> Dependents(Component text)
        {
            Component[] all = text.GetComponents<Component>();
            var found = new List<Component>();
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (Component component in all)
                {
                    if (component == null || component == text || component is Transform || found.Contains(component)) continue;
                    bool bound = TextBound.Any(t => t.IsInstanceOfType(component));
                    if (bound || Needs(component, text) || found.Any(f => Needs(component, f)))
                    {
                        found.Add(component);
                        grew = true;
                    }
                }
            }
            return all.Where(found.Contains).ToList();
        }

        /// <summary>Why the text cannot become the given type with what is on its object, or null.</summary>
        internal static string WhyNot(Component text, Type to)
        {
            if (text == null) return "There is no text.";
            List<Component> dependents = Dependents(text);
            Component[] all = text.GetComponents<Component>();
            foreach (Component dependent in dependents)
            {
                foreach (Type required in Required(dependent.GetType()))
                {
                    if (!required.IsInstanceOfType(text) || required.IsAssignableFrom(to)) continue;
                    if (all.Any(c => c != text && !dependents.Contains(c) && required.IsInstanceOfType(c))) continue;
                    return $"{ObjectNames.NicifyVariableName(dependent.GetType().Name)} needs a {ObjectNames.NicifyVariableName(required.Name)}, which a {ObjectNames.NicifyVariableName(to.Name)} is not.";
                }
            }
            return null;
        }

        /// <summary>
        /// Runs <paramref name="replace"/>, which removes the old text and returns the new one, between setting aside what depends on the text
        /// and bringing it back, then moves references over. All of it is one undo step named <paramref name="undoName"/>.
        /// </summary>
        internal static T Swap<T>(Component old, string undoName, Func<T> replace) where T : Component
        {
            Last = default;
            string why = WhyNot(old, typeof(T));
            if (why != null)
            {
                Debug.LogWarning("Lettera: " + why, old);
                return null;
            }
            GameObject go = old.gameObject;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            int group = Undo.GetCurrentGroup();

            List<Component> dependents = Dependents(old);
            var moved = new HashSet<int> { old.GetInstanceID() };
            foreach (Component dependent in dependents) moved.Add(dependent.GetInstanceID());
            List<Reference> references = FindReferences(moved);
            var states = dependents.Select(d => (id: d.GetInstanceID(), type: d.GetType(), json: EditorJsonUtility.ToJson(d))).ToList();
            // Taken before anything goes, so that undo writes these back last, when what they point at is there again.
            Object[] holders = references.Where(r => !moved.Contains(r.HolderId)).Select(r => r.Holder).Distinct().ToArray();
            if (holders.Length > 0) Undo.RegisterCompleteObjectUndo(holders, undoName);

            // Those nothing else needs go first, so that Unity never refuses one.
            var left = new List<Component>(dependents);
            while (left.Count > 0)
            {
                Component free = left.FirstOrDefault(c => !left.Any(other => other != c && Needs(other, c))) ?? left[0];
                left.Remove(free);
                Undo.DestroyObjectImmediate(free);
            }

            int oldId = old.GetInstanceID();
            T made = replace();
            if (made == null)
            {
                Undo.CollapseUndoOperations(group);
                Undo.PerformUndo();
                return null;
            }
            var map = new Dictionary<int, Object> { [oldId] = made };
            var claimed = new HashSet<Component> { made };
            var started = new HashSet<int>();
            void Restore(int index)
            {
                (int id, Type type, string json) = states[index];
                if (!started.Add(id)) return;
                // What it needs comes back first, so that Unity does not add a blank one of its own.
                for (int other = 0; other < states.Count; other++)
                    if (other != index && Required(type).Any(t => t.IsAssignableFrom(states[other].type)))
                        Restore(other);
                Component back = go.GetComponents(type).FirstOrDefault(c => c.GetType() == type && !claimed.Contains(c));
                if (back == null) back = Undo.AddComponent(go, type);
                else Undo.RecordObject(back, undoName);
                claimed.Add(back);
                EditorJsonUtility.FromJsonOverwrite(json, back);
                map[id] = back;
            }
            for (int i = 0; i < states.Count; i++) Restore(i);

            Retarget(references, map, undoName);
            Last.Carried = states.Count;
            Undo.CollapseUndoOperations(group);
            return made;
        }

        static IEnumerable<GameObject> Roots()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects()) yield return root;
            }
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null) yield return stage.prefabContentsRoot;
        }

        // Arrays of these hold no reference, so they are not walked element by element.
        static readonly HashSet<string> Plain = new HashSet<string>
        {
            "int", "float", "double", "bool", "char", "string", "long", "short", "byte", "sbyte", "uint", "ulong", "ushort",
            "UInt8", "SInt8", "UInt16", "SInt16", "UInt32", "SInt32", "UInt64", "SInt64", "Vector2", "Vector3", "Vector4", "Vector2Int", "Vector3Int", "Color", "Color32", "Quaternion", "Rect",
        };

        static List<Reference> FindReferences(HashSet<int> targets)
        {
            var found = new List<Reference>();
            foreach (GameObject root in Roots())
            {
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null || component is Transform) continue;
                    using (var serialized = new SerializedObject(component))
                    {
                        SerializedProperty property = serialized.GetIterator();
                        bool enter = true;
                        while (property.Next(enter))
                        {
                            SerializedPropertyType kind = property.propertyType;
                            enter = (kind == SerializedPropertyType.Generic && !(property.isArray && Plain.Contains(property.arrayElementType))) || kind == SerializedPropertyType.ManagedReference;
                            if (kind != SerializedPropertyType.ObjectReference) continue;
                            int id = property.objectReferenceInstanceIDValue;
                            if (id != 0 && targets.Contains(id)) found.Add(new Reference { Holder = component, HolderId = component.GetInstanceID(), Path = property.propertyPath, Target = id });
                        }
                    }
                }
            }
            return found;
        }

        static void Retarget(List<Reference> references, Dictionary<int, Object> map, string undoName)
        {
            foreach (IGrouping<int, Reference> byHolder in references.GroupBy(r => r.HolderId))
            {
                // A holder that was itself set aside is its successor now.
                Object holder = map.TryGetValue(byHolder.Key, out Object successor) ? successor : byHolder.First().Holder;
                if (holder == null) continue;
                using (var serialized = new SerializedObject(holder))
                {
                    foreach (Reference reference in byHolder)
                    {
                        SerializedProperty property = serialized.FindProperty(reference.Path);
                        if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        Object to = map[reference.Target];
                        LtUI.FieldOf(property, out Type held);
                        if (held != null && !held.IsInstanceOfType(to))
                        {
                            Last.Unfit++;
                            Debug.LogWarning($"Lettera: {Describe(holder)} keeps its {property.displayName}, which takes a {held.Name} and so cannot hold a {to.GetType().Name}.", holder);
                            continue;
                        }
                        property.objectReferenceValue = to;
                        if (property.objectReferenceValue != to)
                        {
                            property.objectReferenceInstanceIDValue = reference.Target;
                            Last.Unfit++;
                            continue;
                        }
                        Last.Retargeted++;
                        FollowEvent(serialized, reference.Path, to, holder);
                    }
                    serialized.ApplyModifiedProperties();
                }
                // It keeps the engine it found, which went with the old text.
                if (holder is LetteraParamLink link) link.Text = link.Text;
            }
        }

        // A persistent call names the type it was made on, and finds its method there: that name must follow the target.
        static void FollowEvent(SerializedObject serialized, string path, Object to, Object holder)
        {
            const string Suffix = ".m_Target";
            if (!path.EndsWith(Suffix, StringComparison.Ordinal)) return;
            string call = path.Substring(0, path.Length - Suffix.Length);
            SerializedProperty typeName = serialized.FindProperty(call + ".m_TargetAssemblyTypeName");
            SerializedProperty method = serialized.FindProperty(call + ".m_MethodName");
            if (typeName == null || method == null) return;
            Type type = to.GetType();
            typeName.stringValue = type.FullName + ", " + type.Assembly.GetName().Name;
            string name = method.stringValue;
            if (!string.IsNullOrEmpty(name) && !type.GetMethods(BindingFlags.Instance | BindingFlags.Public).Any(m => m.Name == name))
                Debug.LogWarning($"Lettera: {Describe(holder)} calls {name}, which a {type.Name} does not have. Choose another in its event.", holder);
        }

        static string Describe(Object holder) => holder is Component c ? $"{c.gameObject.name} ({c.GetType().Name})" : holder.name;
    }
}
