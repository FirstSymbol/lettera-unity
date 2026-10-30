using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>What the engine needs from the component that owns it.</summary>
    internal interface ITextHost
    {
        LetteraTextData Data { get; }
        Rect LayoutRect { get; }
        Color32 BaseColor { get; }
        float RasterScale { get; }
        bool ClipsOverflow { get; }
        Component Owner { get; }
    }

    /// <summary>
    /// The text pipeline both components share. Each pass is a separate partial file, in pipeline order:
    /// parse (markup), shape (Unicode analysis, fonts, shaping), layout (lines, auto size, eliding),
    /// positions (alignment, bidi order) and mesh. A rebuild runs only the passes its dirty level asks for.
    /// </summary>
    internal sealed partial class TextEngine
    {
        readonly ITextHost m_Host;
        DirtyLevel m_Dirty = DirtyLevel.Parse;
        LetteraTextData.Snapshot m_Snapshot;

        // Runtime text overrides the serialized field until the field is set again.
        string m_RuntimeText;
        bool m_HasRuntimeText;
        ILetteraTextResolver m_Resolver;
        string m_ResolvedText = "";

        public TextEngine(ITextHost host) => m_Host = host;

        public DirtyLevel Dirty => m_Dirty;
        public float CurrentFontSize { get; private set; }
        public LetteraChanges LastChanges { get; private set; }
        public int RebuildCount { get; private set; }

        public void SetDirty(DirtyLevel level)
        {
            if (level > m_Dirty) m_Dirty = level;
        }

        /// <summary>Clears any runtime override; the serialized text applies again.</summary>
        public void ClearRuntimeText()
        {
            m_RuntimeText = null;
            m_HasRuntimeText = false;
        }

        /// <summary>Sets runtime text. Returns false when the characters are the ones already shown.</summary>
        public bool SetRuntimeText(ReadOnlySpan<char> text)
        {
            if (m_HasRuntimeText && text.SequenceEqual(m_RuntimeText.AsSpan())) return false;
            m_RuntimeText = text.ToString();
            m_HasRuntimeText = true;
            return true;
        }

        public string RawText => m_HasRuntimeText ? m_RuntimeText : m_Host.Data.Text ?? "";
        public string ResolvedText => m_ResolvedText;
        public string RenderedSourceText => m_ResolvedText;

        public ILetteraTextResolver Resolver
        {
            get => m_Resolver;
            set => m_Resolver = value;
        }

        public LetteraTextOverride TextOverride =>
            m_HasRuntimeText && m_Resolver != null ? LetteraTextOverride.Both :
            m_HasRuntimeText ? LetteraTextOverride.RuntimeText :
            m_Resolver != null ? LetteraTextOverride.Resolver : LetteraTextOverride.None;

        /// <summary>Raises the level a serialized write needs (Inspector, Undo, prefab revert, Animator).</summary>
        public void SyncSerializedFields()
        {
            DirtyLevel level = m_Host.Data.Compare(m_Snapshot);
            if (level == DirtyLevel.None) return;
            if (level == DirtyLevel.Parse && m_Snapshot.Text != m_Host.Data.Text) ClearRuntimeText();
            SetDirty(level);
            m_Snapshot = m_Host.Data.Take();
        }

        /// <summary>Runs the passes the dirty level needs. Returns true when the mesh arrays changed.</summary>
        public bool Rebuild()
        {
            m_Snapshot = m_Host.Data.Take();
            DirtyLevel level = m_Dirty;
            m_Dirty = DirtyLevel.None;
            LastChanges = LetteraChanges.None;
            if (level == DirtyLevel.None) return false;
            RebuildCount++;

            if (level >= DirtyLevel.Parse)
            {
                Parse();
                LastChanges |= LetteraChanges.Text;
            }
            if (level >= DirtyLevel.Shape) ShapeText();
            if (level >= DirtyLevel.Layout)
            {
                RunLayout();
                LastChanges |= LetteraChanges.Layout;
            }
            if (level >= DirtyLevel.Positions)
            {
                PositionGlyphs();
                LastChanges |= LetteraChanges.Positions;
            }
            if (level >= DirtyLevel.Mesh)
            {
                BuildMesh();
                LastChanges |= LetteraChanges.Mesh;
            }
            else if (level == DirtyLevel.Alpha || level == DirtyLevel.Tint)
            {
                ApplyBaseColor();
                LastChanges |= LetteraChanges.Mesh;
            }
            return true;
        }

        /// <summary>Makes parsing and shaping current, for queries before the frame's rebuild.</summary>
        public void EnsureMeasured()
        {
            if (m_Dirty >= DirtyLevel.Parse)
            {
                Parse();
                m_Dirty = DirtyLevel.Shape;
            }
            if (m_Dirty >= DirtyLevel.Shape)
            {
                ShapeText();
                m_Dirty = DirtyLevel.Layout;
            }
        }

        public bool HasElideRequests => m_Attributes.Elides.Count > 0;

        sealed class MeasureHost : ITextHost
        {
            public LetteraTextData Data { get; set; }
            public Rect LayoutRect { get; set; }
            public Color32 BaseColor => new Color32(255, 255, 255, 255);
            public float RasterScale => 1f;
            public bool ClipsOverflow => false;
            public Component Owner { get; set; }
        }

        /// <summary>Measures a text without touching what a component displays.</summary>
        public static Vector2 MeasureWith(Component owner, LetteraTextData data, string text, float size, float width, float height, bool wrap, bool autoSize, float minSize, float maxSize)
        {
            LetteraTextData copy = data.Clone();
            copy.Text = text ?? "";
            var engine = new TextEngine(new MeasureHost { Data = copy, Owner = owner });
            return engine.Measure(size, width, height, wrap, autoSize, minSize, maxSize);
        }

        static void EnsureSize<T>(ref T[] array, int size)
        {
            if (array == null) array = new T[Math.Max(size, 64)];
            else if (array.Length < size) Array.Resize(ref array, Math.Max(size, array.Length * 2));
        }
    }
}
