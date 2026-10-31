using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>What selection and editing need from either text component.</summary>
    internal interface ITextComponent
    {
        TextEngine SharedEngine { get; }
        RectTransform TextRect { get; }
        bool IsVisible { get; }
        string SourceText { get; set; }
        void Dirty(DirtyLevel level);
        event Action SourceWritten;
        event Action<LetteraChanges> Rebuilt;
    }

    /// <summary>What the engine needs from the component that owns it.</summary>
    internal interface ITextHost
    {
        LetteraTextData Data { get; }
        Rect LayoutRect { get; }
        Color32 BaseColor { get; }
        float RasterScale { get; }
        bool ClipsOverflow { get; }
        Component Owner { get; }

        /// <summary>Something other than the component changed what the text shows.</summary>
        void RequestRebuild(DirtyLevel level);
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
        char[] m_RuntimeChars = new char[64];
        int m_RuntimeLength;
        string m_RuntimeText;
        bool m_HasRuntimeText;
        ILetteraTextResolver m_Resolver;
        string m_ResolvedText = "";

        public TextEngine(ITextHost host) => m_Host = host;

        public DirtyLevel Dirty => m_Dirty;
        public float CurrentFontSize { get; private set; }
        public LetteraChanges LastChanges { get; private set; }
        public int RebuildCount { get; private set; }

        // How often each expensive pass ran, to prove that a change stayed at its level.
        public int ParseCount { get; private set; }
        public int ShapeCount { get; private set; }

        public void SetDirty(DirtyLevel level)
        {
            if (level > m_Dirty) m_Dirty = level;
        }

        bool m_Reapply;

        /// <summary>An effect's parameter changed: runs the effects again on the ranges already found, then the passes from a level up.</summary>
        public void ReapplyEffects(DirtyLevel level)
        {
            m_Reapply = true;
            SetDirty(level);
        }

        void ReapplyIfNeeded()
        {
            if (!m_Reapply) return;
            m_Reapply = false;
            ApplyEffects(m_MatchContext);
        }

        /// <summary>Clears any runtime override; the serialized text applies again.</summary>
        public void ClearRuntimeText()
        {
            m_RuntimeText = null;
            m_RuntimeLength = 0;
            m_HasRuntimeText = false;
        }

        /// <summary>Sets runtime text. Returns false when the characters are the ones already shown.</summary>
        public bool SetRuntimeText(ReadOnlySpan<char> text)
        {
            if (m_HasRuntimeText && text.SequenceEqual(new ReadOnlySpan<char>(m_RuntimeChars, 0, m_RuntimeLength))) return false;
            if (m_RuntimeChars.Length < text.Length) m_RuntimeChars = new char[Math.Max(text.Length, m_RuntimeChars.Length * 2)];
            text.CopyTo(m_RuntimeChars);
            m_RuntimeLength = text.Length;
            m_RuntimeText = null;
            m_HasRuntimeText = true;
            return true;
        }

        public string RawText => m_HasRuntimeText ? m_RuntimeText ?? (m_RuntimeText = new string(m_RuntimeChars, 0, m_RuntimeLength)) : m_Host.Data.Text ?? "";

        // What gets parsed, without building a string for runtime text.
        ReadOnlySpan<char> RawChars => m_HasRuntimeText ? new ReadOnlySpan<char>(m_RuntimeChars, 0, m_RuntimeLength) : (m_Host.Data.Text ?? "").AsSpan();
        public string ResolvedText => m_Resolver != null ? m_ResolvedText : RawText;
        public string RenderedSourceText => ResolvedText;

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
            LastChanges = m_EarlyChanges;
            if (level == DirtyLevel.None) return false;
            m_EarlyChanges = LetteraChanges.None;
            RebuildCount++;

            if (level >= DirtyLevel.Parse)
            {
                Parse();
                LastChanges |= LetteraChanges.Text;
            }
            else
            {
                ReapplyIfNeeded();
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

        // What passes run ahead of the frame's rebuild changed, for the rebuild to report.
        LetteraChanges m_EarlyChanges;

        /// <summary>Makes glyph positions current, for caret and selection queries before the frame's rebuild. The mesh waits for it.</summary>
        public void EnsurePositioned()
        {
            if (m_Dirty < DirtyLevel.Positions) return;
            EnsureMeasured();
            if (m_Dirty >= DirtyLevel.Layout)
            {
                RunLayout();
                m_EarlyChanges |= LetteraChanges.Layout;
            }
            PositionGlyphs();
            m_EarlyChanges |= LetteraChanges.Positions;
            m_Dirty = DirtyLevel.Mesh;
        }

        /// <summary>Makes parsing and shaping current, for queries before the frame's rebuild.</summary>
        public void EnsureMeasured()
        {
            if (m_Dirty >= DirtyLevel.Parse)
            {
                Parse();
                m_EarlyChanges |= LetteraChanges.Text;
                m_Dirty = DirtyLevel.Shape;
            }
            else
            {
                ReapplyIfNeeded();
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
            public void RequestRebuild(DirtyLevel level) { }
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
