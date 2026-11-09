using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FirstSymbol.Lettera.Unicode;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Compiles a word list into the flat automaton the runtime reads. The same words always give the same bytes.</summary>
    internal static class LetteraWordDictionaryBuilder
    {
        sealed class State
        {
            public readonly SortedList<int, State> Next = new SortedList<int, State>();
            public bool Final;
            public int Offset = -1;
        }

        /// <summary>Takes words as written, or lines of a list: a # starts a comment, and what follows a space or a tab on a line is ignored.</summary>
        public static byte[] Build(IEnumerable<string> lines, out string report)
        {
            var words = new List<int[]>();
            var seen = new HashSet<string>();
            int comments = 0, duplicates = 0, invalid = 0;
            foreach (string raw in lines)
            {
                string line = raw.Trim('﻿', ' ', '\t', '\r', '\n');
                if (line.Length == 0 || line[0] == '#')
                {
                    comments++;
                    continue;
                }
                int cut = line.IndexOfAny(new[] { ' ', '\t' });
                if (cut > 0) line = line.Substring(0, cut);
                if (!seen.Add(line))
                {
                    duplicates++;
                    continue;
                }
                var codepoints = new List<int>(line.Length);
                bool ok = true;
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (char.IsHighSurrogate(c) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1])) codepoints.Add(char.ConvertToUtf32(c, line[++i]));
                    else if (char.IsSurrogate(c)) ok = false;
                    else codepoints.Add(c);
                }
                if (!ok)
                {
                    invalid++;
                    continue;
                }
                words.Add(codepoints.ToArray());
            }

            var alphabet = new SortedSet<int>();
            foreach (int[] word in words)
                foreach (int cp in word)
                    alphabet.Add(cp);
            if (alphabet.Count > 65000) throw new ArgumentException("A word list can hold at most 65 000 different characters.");
            int[] symbols = alphabet.ToArray();
            var symbolOf = new Dictionary<int, int>(symbols.Length);
            for (int i = 0; i < symbols.Length; i++) symbolOf[symbols[i]] = i + 1;

            words.Sort(CompareWords);

            // Words come sorted, so a state past the point where the next word parts from this one can never change: it either equals a known one or becomes known.
            var register = new Dictionary<string, State>();
            var root = new State();
            var path = new List<State> { root };
            int[] previous = Array.Empty<int>();
            foreach (int[] word in words)
            {
                int common = 0;
                while (common < previous.Length && common < word.Length && previous[common] == word[common]) common++;
                Minimize(path, common, previous, symbolOf, register);
                State node = path[common];
                for (int i = common; i < word.Length; i++)
                {
                    var next = new State();
                    node.Next[symbolOf[word[i]]] = next;
                    path.Add(next);
                    node = next;
                }
                node.Final = true;
                previous = word;
            }
            Minimize(path, 0, previous, symbolOf, register);

            byte[] data = Flatten(root, symbols, words.Count, out int states);
            report = $"{words.Count} words, {duplicates} duplicates dropped, {invalid} invalid, {comments} comment or blank lines; {states} states, {symbols.Length} characters, {data.Length} bytes.";
            return data;
        }

        static int CompareWords(int[] a, int[] b)
        {
            int count = Math.Min(a.Length, b.Length);
            for (int i = 0; i < count; i++)
                if (a[i] != b[i])
                    return a[i].CompareTo(b[i]);
            return a.Length.CompareTo(b.Length);
        }

        static void Minimize(List<State> path, int keep, int[] word, Dictionary<int, int> symbolOf, Dictionary<string, State> register)
        {
            var signature = new StringBuilder();
            for (int i = path.Count - 1; i > keep; i--)
            {
                State state = path[i];
                signature.Clear();
                signature.Append(state.Final ? '1' : '0');
                foreach (KeyValuePair<int, State> pair in state.Next) signature.Append(',').Append(pair.Key).Append(':').Append(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Value));
                string key = signature.ToString();
                if (register.TryGetValue(key, out State known)) path[i - 1].Next[symbolOf[word[i - 1]]] = known;
                else register[key] = state;
                path.RemoveAt(i);
            }
        }

        static byte[] Flatten(State root, int[] symbols, int wordCount, out int stateCount)
        {
            var order = new List<State>();
            var stack = new Stack<State>();
            var visited = new HashSet<State> { root };
            stack.Push(root);
            while (stack.Count > 0)
            {
                State state = stack.Pop();
                order.Add(state);
                for (int i = state.Next.Count - 1; i >= 0; i--)
                {
                    State next = state.Next.Values[i];
                    if (visited.Add(next)) stack.Push(next);
                }
            }
            stateCount = order.Count;

            int header = 5 * 4 + symbols.Length * 4;
            int at = header;
            foreach (State state in order)
            {
                state.Offset = at;
                at += 1 + VarintLength(state.Next.Count) + state.Next.Count * 6;
            }
            var data = new byte[at];
            data[0] = (byte)'L';
            data[1] = (byte)'T';
            data[2] = (byte)'W';
            data[3] = (byte)'D';
            WriteInt(data, 4, LetteraWordAutomaton.FormatVersion);
            WriteInt(data, 8, symbols.Length);
            WriteInt(data, 12, root.Offset);
            WriteInt(data, 16, wordCount);
            for (int i = 0; i < symbols.Length; i++) WriteInt(data, 20 + i * 4, symbols[i]);
            foreach (State state in order)
            {
                int p = state.Offset;
                data[p++] = (byte)(state.Final ? 1 : 0);
                int count = state.Next.Count;
                while (count >= 0x80)
                {
                    data[p++] = (byte)(count | 0x80);
                    count >>= 7;
                }
                data[p++] = (byte)count;
                count = state.Next.Count;
                for (int i = 0; i < count; i++)
                {
                    int label = state.Next.Keys[i];
                    data[p + i * 2] = (byte)label;
                    data[p + i * 2 + 1] = (byte)(label >> 8);
                    WriteInt(data, p + count * 2 + i * 4, state.Next.Values[i].Offset);
                }
            }
            return data;
        }

        static int VarintLength(int value)
        {
            int length = 1;
            while (value >= 0x80)
            {
                value >>= 7;
                length++;
            }
            return length;
        }

        static void WriteInt(byte[] d, int at, int value)
        {
            d[at] = (byte)value;
            d[at + 1] = (byte)(value >> 8);
            d[at + 2] = (byte)(value >> 16);
            d[at + 3] = (byte)(value >> 24);
        }
    }

    [CustomEditor(typeof(LetteraWordDictionary))]
    internal sealed class LetteraWordDictionaryEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, "Word Dictionary", "Where a script written without spaces breaks its lines. It takes effect once listed in the settings");
            var dictionary = (LetteraWordDictionary)target;
            Label status = LtUI.Text("", "lt-subtitle");
            void Show() => status.text = dictionary.Data != null ? $"{dictionary.WordCount} words, {dictionary.Data.bytes.Length / 1024} KB" : "Not built yet.";
            Show();
            page.Add(LtFrame.AllFields(serializedObject, "Dictionary"));
            page.Add(LtUI.Card("Build", status, LtUI.Clickable("Build From A Word List…", "lt-tool", "One word per line, # for comments", () =>
            {
                string list = EditorUtility.OpenFilePanel("Word list", "", "txt");
                if (string.IsNullOrEmpty(list)) return;
                byte[] data = LetteraWordDictionaryBuilder.Build(File.ReadLines(list, Encoding.UTF8), out string report);
                Debug.Log("Lettera: " + report);
                string assetPath = Path.ChangeExtension(AssetDatabase.GetAssetPath(dictionary), ".bytes");
                File.WriteAllBytes(assetPath, data);
                AssetDatabase.ImportAsset(assetPath);
                Undo.RecordObject(dictionary, "Build Word Dictionary");
                dictionary.Data = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
                dictionary.WordCount = new LetteraWordAutomaton(data).WordCount;
                EditorUtility.SetDirty(dictionary);
                Show();
            })));
            return root;
        }
    }
}
