using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // Effects that ask for it get called at the stages of a rebuild, and each frame for a pose. Others pay one count check per pass.
    internal sealed partial class TextEngine
    {
        readonly List<(LetteraEffect effect, int start, int end)> m_PoseHooks = new List<(LetteraEffect, int, int)>();
        readonly List<(LetteraEffect effect, int start, int end)> m_StageHooks = new List<(LetteraEffect, int, int)>();
        readonly List<Vector2Int> m_HookedHidden = new List<Vector2Int>();
        readonly HashSet<LetteraEffect> m_HookFailed = new HashSet<LetteraEffect>();
        DirtyLevel m_HookStale;
        bool m_HookRanBreak;

        void ClearStageHooks()
        {
            m_PoseHooks.Clear();
            m_StageHooks.Clear();
            m_HookedHidden.Clear();
        }

        void RegisterStages(LetteraEffect effect, int start, int end)
        {
            if (effect is CompositeEffect composite)
            {
                foreach (LetteraEffect child in composite.Children) RegisterStages(child, start, end);
                return;
            }
            if (effect == null) return;
            LetteraStages stages = effect.Stages;
            if (stages == LetteraStages.None) return;
            if ((stages & ~LetteraStages.Pose) != 0) m_StageHooks.Add((effect, start, end));
            if ((stages & LetteraStages.Pose) != 0) m_PoseHooks.Add((effect, start, end));
        }

        void HookFailed(LetteraEffect effect, Exception e)
        {
            if (m_HookFailed.Add(effect)) Debug.LogException(e, m_Host.Owner);
        }

        void RunAnalyzedHooks()
        {
            if (m_StageHooks.Count == 0) return;
            for (int h = 0; h < m_StageHooks.Count; h++)
            {
                (LetteraEffect effect, int start, int end) = m_StageHooks[h];
                if ((effect.Stages & LetteraStages.Analyzed) == 0) continue;
                var view = new LetteraAnalysisView(this, start, end);
                try { effect.OnAnalyzed(ref view); }
                catch (Exception e) { HookFailed(effect, e); }
            }
        }

        void RunShapedHooks()
        {
            if (m_StageHooks.Count == 0) return;
            for (int h = 0; h < m_StageHooks.Count; h++)
            {
                (LetteraEffect effect, int start, int end) = m_StageHooks[h];
                if ((effect.Stages & LetteraStages.Shaped) == 0) continue;
                var view = new LetteraShapedView(this, start, end);
                try { effect.OnShaped(ref view); }
                catch (Exception e) { HookFailed(effect, e); }
            }
        }

        // The rest of a hook's asks wait for lines: a forced break means breaking them once more.
        void RunLinesHooks(float size, float width, bool wrap)
        {
            if (m_StageHooks.Count == 0) return;
            m_HookRanBreak = false;
            for (int h = 0; h < m_StageHooks.Count; h++)
            {
                (LetteraEffect effect, int start, int end) = m_StageHooks[h];
                if ((effect.Stages & LetteraStages.LinesBroken) == 0) continue;
                var view = new LetteraLinesView(this, start, end);
                try { effect.OnLinesBroken(ref view); }
                catch (Exception e) { HookFailed(effect, e); }
            }
            if (!m_HookRanBreak) return;
            LineStats stats = BreakLines(m_Lines, size, width, wrap, true);
            m_ContentWidth = stats.MaxWidth;
            m_ContentHeight = stats.Height;
        }

        internal void HookForceBreak(int cp)
        {
            if (cp <= 0 || cp >= m_TextLength) return;
            m_Breaks[cp - 1] = Unicode.LineBreakOpportunity.Mandatory;
            m_HookRanBreak = true;
        }

        internal void HookGapBefore(int line, float units)
        {
            if (line <= 0 || line >= m_Lines.Count) return;
            for (int l = line; l < m_Lines.Count; l++)
            {
                LetteraLine moved = m_Lines[l];
                moved.Baseline -= units;
                m_Lines[l] = moved;
            }
            m_ContentHeight += units;
        }

        internal void HookHide(int start, int end)
        {
            m_HookedHidden.Add(new Vector2Int(start, end));
        }

        // Hidden clusters go the way an elided one goes, after each layout clears those.
        void ApplyHookRemovals()
        {
            for (int i = 0; i < m_HookedHidden.Count; i++)
            {
                Vector2Int span = m_HookedHidden[i];
                m_Removals.Add(new Removal { Start = span.x, End = span.y, Anchor = span.x });
            }
        }

        void RunPlacedHooks()
        {
            if (m_StageHooks.Count == 0) return;
            for (int h = 0; h < m_StageHooks.Count; h++)
            {
                (LetteraEffect effect, int start, int end) = m_StageHooks[h];
                if ((effect.Stages & LetteraStages.Placed) == 0) continue;
                var view = new LetteraPlacedView(this, start, end);
                try { effect.OnPlaced(ref view); }
                catch (Exception e) { HookFailed(effect, e); }
            }
        }

        void RunMeshHooks()
        {
            if (m_StageHooks.Count == 0) return;
            m_CurrentOwner = -1;
            for (int h = 0; h < m_StageHooks.Count; h++)
            {
                (LetteraEffect effect, int start, int end) = m_StageHooks[h];
                if ((effect.Stages & LetteraStages.Mesh) == 0) continue;
                var view = new LetteraMeshView(this, start, end);
                try { effect.OnMesh(ref view); }
                catch (Exception e) { HookFailed(effect, e); }
            }
        }

        void RunPoseHooks()
        {
            if (m_PoseHooks.Count == 0) return;
            for (int h = 0; h < m_PoseHooks.Count; h++)
            {
                (LetteraEffect effect, int start, int end) = m_PoseHooks[h];
                var view = new LetteraPoseView(this, start, end);
                try { effect.OnPose(ref view); }
                catch (Exception e) { HookFailed(effect, e); }
            }
        }

        internal void HookStale(DirtyLevel level)
        {
            if (level == DirtyLevel.None) return;
            if (level == DirtyLevel.Pose)
            {
                m_PoseDirty = true;
                return;
            }
            m_EarlyHookLevel = level;
        }

        DirtyLevel m_EarlyHookLevel;

        // A rerun a hook asked for goes to the next frame, so it can never loop inside one.
        void ApplyHookStale()
        {
            if (m_EarlyHookLevel == DirtyLevel.None) return;
            DirtyLevel level = m_EarlyHookLevel;
            m_EarlyHookLevel = DirtyLevel.None;
            SetDirty(level);
            m_Host.RequestRebuild(level);
        }

        // ------------------------------------------------------------- what views read

        internal int StageText(int cp) => m_Text[cp];
        internal int StageLength => m_TextLength;
        internal Unicode.UnicodeScript StageScript(int cp) => m_Scripts[cp];
        internal byte StageLevel(int cp) => m_Levels[cp];
        internal bool StageBoundary(int cp) => m_Boundaries[cp];
        internal int ShapedCount => m_ShapedCount;
        internal uint ShapedGlyphId(int i) => m_Shaped[i].Glyph;
        internal int ShapedCluster(int i) => m_Shaped[i].Cluster;
        internal float ShapedAdvance(int i) => m_Shaped[i].XAdvance;
        internal void SetShapedGlyph(int i, uint glyph) => m_Shaped[i].Glyph = glyph;

        internal void HookAddExtra(int cp, float em, float units)
        {
            if (cp < 0 || cp >= m_TextLength) return;
            m_CpExtraEm[cp] += em;
            m_CpExtraUnits[cp] += units;
        }

        internal int LineCountForHooks => m_Lines.Count;
        internal LetteraLine LineForHooks(int line) => m_Lines[line];
        internal int PlacedCountForHooks => m_PlacedCount;
        internal ref PlacedGlyph PlacedForHooks(int i) => ref m_Placed[i];
        internal void HookPose(int glyph, in LetteraGlyphPose pose) => m_GlyphPoses[glyph] = pose.After(m_GlyphPoses[glyph]);
        internal LetteraGlyphPose HookPoseOf(int glyph) => m_GlyphPoses[glyph];
        internal void HookSolid(float left, float bottom, float right, float top, Color32 color)
        {
            UseSegment(s_NoPaint);
            AddSolid(left, bottom, right, top, color, s_NoPaint, default, 0, 0);
        }
        internal void HookInvalidateActive() => m_ActiveRectsValid = false;

        // For tests.
        internal int HookFailureCount => m_HookFailed.Count;
        internal int DocCharOfLineForTests(int line) => m_SourceIndex[m_Lines[line].Start];
        internal FontChain ChainForTests => m_Chain;
        internal int HoldCount => m_Holds.Count;
        internal int OwnerOfQuad(int quad) => m_QuadOwner[quad];
        internal int ClusterOfGlyph(int glyph) => m_Placed[glyph].Cluster;
        internal float MotionValueForTests(int span, int slot)
        {
            if (m_WantsPose) RunPosePass();
            return m_MotionSpans[span].Effective[slot];
        }
        internal int SourceOfRenderedForTests(int cp) => m_SourceIndex[cp];
        internal uint ShapedGlyphIdForTests(int i) => m_Shaped[i].Glyph;
    }

    [Flags]
    public enum LetteraStages { None = 0, Analyzed = 1, Shaped = 2, LinesBroken = 4, Placed = 8, Mesh = 16, Pose = 32 }

    /// <summary>The text after Unicode analysis. Valid only during the call. Must stay pure over the view: no Unity objects and no shared statics.</summary>
    public readonly ref struct LetteraAnalysisView
    {
        readonly TextEngine m_Engine;
        public readonly int Start, End;
        internal LetteraAnalysisView(TextEngine engine, int start, int end) { m_Engine = engine; Start = start; End = end; }
        public int Length => m_Engine.StageLength;
        public int Codepoint(int cp) => m_Engine.StageText(cp);
        public int Level(int cp) => m_Engine.StageLevel(cp);
        public bool IsClusterStart(int cp) => m_Engine.StageBoundary(cp);

        /// <summary>Takes clusters out of layout. Indices stay what they were.</summary>
        public void Hide(int start, int end) => m_Engine.HookHide(start, end);
        public void MarkStale(DirtyLevel level) => m_Engine.HookStale(level);
    }

    /// <summary>The glyphs a text shaped to. Valid only during the call.</summary>
    public readonly ref struct LetteraShapedView
    {
        readonly TextEngine m_Engine;
        public readonly int Start, End;
        internal LetteraShapedView(TextEngine engine, int start, int end) { m_Engine = engine; Start = start; End = end; }
        public int GlyphCount => m_Engine.ShapedCount;
        public uint Glyph(int index) => m_Engine.ShapedGlyphId(index);
        public int Cluster(int index) => m_Engine.ShapedCluster(index);
        public float Advance(int index) => m_Engine.ShapedAdvance(index);

        /// <summary>Puts another glyph of the same run's face in place.</summary>
        public void ReplaceGlyph(int index, uint glyph) => m_Engine.SetShapedGlyph(index, glyph);

        /// <summary>Adds width after a cluster, in em and in local units.</summary>
        public void AddExtra(int cp, float em, float units = 0) => m_Engine.HookAddExtra(cp, em, units);
        public void MarkStale(DirtyLevel level) => m_Engine.HookStale(level);
    }

    /// <summary>The lines before they are placed. Valid only during the call.</summary>
    public readonly ref struct LetteraLinesView
    {
        readonly TextEngine m_Engine;
        public readonly int Start, End;
        internal LetteraLinesView(TextEngine engine, int start, int end) { m_Engine = engine; Start = start; End = end; }
        public int LineCount => m_Engine.LineCountForHooks;
        public int LineStart(int line) => m_Engine.LineForHooks(line).Start;
        public int LineEnd(int line) => m_Engine.LineForHooks(line).End;
        public float LineWidth(int line) => m_Engine.LineForHooks(line).Width;

        /// <summary>Starts a new line at a codepoint. The lines are then broken once more.</summary>
        public void ForceBreakBefore(int cp) => m_Engine.HookForceBreak(cp);
        public void AddGapBefore(int line, float units) => m_Engine.HookGapBefore(line, units);
        public void MarkStale(DirtyLevel level) => m_Engine.HookStale(level);
    }

    /// <summary>Glyphs where they landed. What moves here, queries and carets read too. Valid only during the call.</summary>
    public readonly ref struct LetteraPlacedView
    {
        readonly TextEngine m_Engine;
        public readonly int Start, End;
        internal LetteraPlacedView(TextEngine engine, int start, int end) { m_Engine = engine; Start = start; End = end; }
        public int Count => m_Engine.PlacedCountForHooks;
        public int Cluster(int glyph) => m_Engine.PlacedForHooks(glyph).Cluster;
        public Vector2 Position(int glyph)
        {
            ref TextEngine.PlacedGlyph g = ref m_Engine.PlacedForHooks(glyph);
            return new Vector2(g.X, g.Y);
        }
        public float Angle(int glyph) => m_Engine.PlacedForHooks(glyph).Angle;

        public void Move(int glyph, float dx, float dy)
        {
            ref TextEngine.PlacedGlyph g = ref m_Engine.PlacedForHooks(glyph);
            g.X += dx;
            g.Pen += dx;
            g.Y += dy;
            m_Engine.HookInvalidateActive();
        }

        public void Rotate(int glyph, float radians) => m_Engine.PlacedForHooks(glyph).Angle += radians;
        public void ScaleX(int glyph, float factor)
        {
            ref TextEngine.PlacedGlyph g = ref m_Engine.PlacedForHooks(glyph);
            g.XScale = (1 + g.XScale) * factor - 1;
        }
        public void MarkStale(DirtyLevel level) => m_Engine.HookStale(level);
    }

    /// <summary>The mesh after layers. Valid only during the call.</summary>
    public readonly ref struct LetteraMeshView
    {
        readonly TextEngine m_Engine;
        public readonly int Start, End;
        internal LetteraMeshView(TextEngine engine, int start, int end) { m_Engine = engine; Start = start; End = end; }
        public int VertexCount => m_Engine.VertexCount;
        public Span<Vector3> Vertices => new Span<Vector3>(m_Engine.Vertices, 0, m_Engine.VertexCount);
        public Span<Color32> Colors => new Span<Color32>(m_Engine.Colors, 0, m_Engine.VertexCount);

        /// <summary>Adds a rectangle of one color.</summary>
        public void AddSolid(float left, float bottom, float right, float top, Color32 color) => m_Engine.HookSolid(left, bottom, right, top, color);

        /// <summary>Holds room for a quad a later pose fills: it has no area until then.</summary>
        public int ReserveQuad()
        {
            int quad = m_Engine.VertexCount / 4;
            m_Engine.HookSolid(0, 0, 0, 0, default);
            return quad;
        }
        public void MarkStale(DirtyLevel level) => m_Engine.HookStale(level);
    }

    /// <summary>The poses of this tick. Valid only during the call.</summary>
    public readonly ref struct LetteraPoseView
    {
        readonly TextEngine m_Engine;
        public readonly int Start, End;
        internal LetteraPoseView(TextEngine engine, int start, int end) { m_Engine = engine; Start = start; End = end; }
        public int Count => m_Engine.PlacedCountForHooks;
        public int Cluster(int glyph) => m_Engine.PlacedForHooks(glyph).Cluster;
        public Vector2 Position(int glyph)
        {
            ref TextEngine.PlacedGlyph g = ref m_Engine.PlacedForHooks(glyph);
            return new Vector2(g.X, g.Y);
        }
        public float Size(int glyph) => m_Engine.PlacedForHooks(glyph).Size;

        /// <summary>Composes a pose over what the glyph has this tick.</summary>
        public void Compose(int glyph, in LetteraGlyphPose pose) => m_Engine.HookPose(glyph, pose);
    }
}
