using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Migration
{
    /// <summary>
    /// Files and folders the migration leaves alone, kept per project in a file that is committed, so that a team shares them.
    /// A path covers itself and everything under it.
    /// </summary>
    public sealed class LetteraMigrationExclusions
    {
        public const string DefaultFile = "ProjectSettings/Lettera/Migration.json";

        [Serializable]
        sealed class Stored
        {
            public List<string> Excluded = new List<string>();
        }

        readonly string m_File;
        readonly List<string> m_Paths = new List<string>();

        public IReadOnlyList<string> Paths => m_Paths;
        public string File => m_File;

        public LetteraMigrationExclusions(string file = DefaultFile)
        {
            m_File = file;
            if (string.IsNullOrEmpty(file) || !System.IO.File.Exists(file)) return;
            try
            {
                var stored = JsonUtility.FromJson<Stored>(System.IO.File.ReadAllText(file));
                if (stored?.Excluded == null) return;
                foreach (string path in stored.Excluded)
                {
                    string clean = Clean(path);
                    if (clean.Length > 0 && !m_Paths.Contains(clean)) m_Paths.Add(clean);
                }
                m_Paths.Sort(StringComparer.Ordinal);
            }
            catch (ArgumentException e)
            {
                Debug.LogWarning($"Lettera: {file} could not be read, so nothing is excluded from the migration. {e.Message}");
            }
        }

        static string Clean(string path) => string.IsNullOrEmpty(path) ? "" : path.Replace('\\', '/').TrimEnd('/');

        /// <summary>Whether the path, or a folder above it, is excluded.</summary>
        public bool Covers(string path)
        {
            if (m_Paths.Count == 0 || string.IsNullOrEmpty(path)) return false;
            string clean = Clean(path);
            foreach (string excluded in m_Paths)
                if (clean.Length == excluded.Length ? string.Equals(clean, excluded, StringComparison.Ordinal) : clean.Length > excluded.Length && clean[excluded.Length] == '/' && clean.StartsWith(excluded, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>Whether this very path is on the list, as against a folder above it.</summary>
        public bool Contains(string path) => m_Paths.Contains(Clean(path));

        public void Add(string path)
        {
            string clean = Clean(path);
            if (clean.Length == 0 || m_Paths.Contains(clean)) return;
            m_Paths.Add(clean);
            m_Paths.Sort(StringComparer.Ordinal);
            Save();
        }

        public void Remove(string path)
        {
            if (m_Paths.Remove(Clean(path))) Save();
        }

        // Sorted and pretty, so that two people's changes merge line by line.
        void Save()
        {
            if (string.IsNullOrEmpty(m_File)) return;
            string folder = Path.GetDirectoryName(m_File);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            System.IO.File.WriteAllText(m_File, JsonUtility.ToJson(new Stored { Excluded = new List<string>(m_Paths) }, true) + "\n");
        }
    }

    /// <summary>The folders that hold findings, as a tree with a count on each, so that a long list can be narrowed to one part of a project.</summary>
    public sealed class LetteraFolderTree
    {
        public sealed class Node
        {
            public string Name;
            /// <summary>The folder's path, without a slash at the end.</summary>
            public string Path;
            /// <summary>Files in it and in everything under it.</summary>
            public int Count;
            public int Depth;
            public readonly List<Node> Children = new List<Node>();
        }

        public readonly List<Node> Roots = new List<Node>();

        public static string FolderOf(string path)
        {
            string clean = path.Replace('\\', '/');
            int slash = clean.LastIndexOf('/');
            return slash > 0 ? clean.Substring(0, slash) : "";
        }

        /// <summary>Whether a file lies in a folder or below it. An empty folder holds everything.</summary>
        public static bool InFolder(string path, string folder)
        {
            if (string.IsNullOrEmpty(folder)) return true;
            string clean = path.Replace('\\', '/');
            return clean.Length > folder.Length && clean[folder.Length] == '/' && clean.StartsWith(folder, StringComparison.Ordinal);
        }

        public static LetteraFolderTree Build(IEnumerable<string> files)
        {
            var tree = new LetteraFolderTree();
            var byPath = new Dictionary<string, Node>(StringComparer.Ordinal);
            foreach (string file in files)
            {
                string folder = FolderOf(file);
                if (folder.Length == 0) continue;
                string[] parts = folder.Split('/');
                Node parent = null;
                string at = "";
                for (int i = 0; i < parts.Length; i++)
                {
                    at = i == 0 ? parts[0] : at + "/" + parts[i];
                    if (!byPath.TryGetValue(at, out Node node))
                    {
                        node = new Node { Name = parts[i], Path = at, Depth = i };
                        byPath[at] = node;
                        (parent == null ? tree.Roots : parent.Children).Add(node);
                    }
                    node.Count++;
                    parent = node;
                }
            }
            Sort(tree.Roots);
            return tree;
        }

        static void Sort(List<Node> nodes)
        {
            nodes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (Node node in nodes) Sort(node.Children);
        }

        /// <summary>Every folder, each before those inside it.</summary>
        public IEnumerable<Node> Walk() => Roots.SelectMany(Walk);

        static IEnumerable<Node> Walk(Node node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
    }
}
