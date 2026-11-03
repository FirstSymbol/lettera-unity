using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // Short texts changed in one frame are shaped, broken into lines, placed and, when plain, meshed on the threads together, each by the passes it
    // would run alone. What those passes share with other texts is only read there: the main thread parses first and finds the font chain, and
    // afterwards makes the paragraphs and glyphs a thread found newest in the caches. A text that would shape anew or make a glyph, or use anything
    // shared that writes, does that part alone on the main thread as before.
    internal sealed unsafe partial class TextEngine
    {
        /// <summary>How many texts a batch took as far as their mesh. Tests read this.</summary>
        internal static int BatchedTexts, BatchedMeshes;

        /// <summary>Whether a batch builds meshes on the threads too. Tests turn it off to compare.</summary>
        internal static bool BatchMeshes = true;

        bool m_BatchWorker, m_BatchMissed, m_BatchDone, m_BatchMeshMissed, m_BatchMeshDone, m_MeshReady;
        int m_DirtyStamp, m_BatchStamp;
        float m_FrozenScale, m_FrozenQuadMargin;
        int m_BatchHits;
        List<IntPtr> m_BatchTouched;
        Rect m_FrozenRect;
        bool m_RectFrozen;

        // The host's rect is Unity's to read, on the main thread only: a batch reads the one taken before it.
        Rect HostRect => m_RectFrozen ? m_FrozenRect : m_Host.LayoutRect;
        float HostRasterScale => m_RectFrozen ? m_FrozenScale : m_Host.RasterScale;

        // A mesh a thread may build: faces of one colour each from glyphs the atlas holds, and nothing that draws or moves besides them.
        bool MeshAside =>
            !m_Attributes.NeedsStyledShader() && !HasInlineSprites && m_Attributes.Layers.Count == 0 && m_Attributes.Decorations.Count == 0 &&
            m_Attributes.Highlights.Count == 0 && m_Attributes.Filters.Count == 0 && m_Attributes.Warps.Count == 0 && !m_Attributes.HasUnion &&
            m_Attributes.PaintOrder != LetteraPaintOrder.GlyphMajor && m_Aside.MotionSpans.Count == 0 && !HasPoseConsumers && !NeedsRestState && !ShowsBars &&
            m_Aside.Marquee == null && !m_Aside.MarqueeScrolling && m_Aside.ExtraQuads.Count == 0 && !m_SolidsOnly;

        // Taken on the main thread for a batch: what finds the settings asset is Unity's.
        internal static LetteraSettings s_BatchSettings;
        static LetteraSettings SettingsNow => s_BatchSettings ?? LetteraSettings.Instance;

        static int BatchLimit => Math.Min(LetteraWorkers.ParallelFromCodepoints, LetteraLongText.FromGlyphs / 2);

        /// <summary>Whether this text may join a batch, by what is known before it is parsed.</summary>
        internal bool MayBatch =>
            m_Dirty >= DirtyLevel.Layout && !FontsGone && !m_SolidsOnly && m_PendingCount == 0 && !IsWindowed && !m_Banded && !m_Aside.PlainScroll &&
            MaskGlyph == 0 && RevealSource < 0 && m_Aside.StageHooks.Count == 0 && RawChars.Length < BatchLimit;

        /// <summary>Nothing outside the package runs while this text is parsed: no resolver, hold or style of its own, and no tag to match.</summary>
        internal bool ParsesQuietly
        {
            get
            {
                if (m_Dirty < DirtyLevel.Parse) return !m_Reapply;
                if (m_Resolver != null || m_Holds.Count != 0 || ClipStyle != null || m_OwnCopies) return false;
                LetteraTextData data = m_Host.Data;
                return data.Styles.Count == 0 && data.StylePresets.Count == 0 && RawChars.IndexOfAny('<', '\\') < 0;
            }
        }

        /// <summary>Main thread: parses, and readies what the threads will read. False leaves the text to rebuild alone, already parsed.</summary>
        internal bool BeginBatch(Rect rect, float rasterScale)
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
            LetteraTextData data = m_Host.Data;
            if (m_TextLength >= BatchLimit || m_Attributes.HasLayout || m_Attributes.HasCurve || m_Attributes.Elides.Count > 0 || m_Attributes.Marquee != null ||
                m_Attributes.RevealRequests.Count > 0 || m_Aside.RevealSpans.Count > 0 || m_Aside.InlineItems.Count > 0 || m_Aside.StageHooks.Count > 0 ||
                m_Aside.HookedHidden.Count > 0 || m_Aside.FormulaCount > 0 || m_Aside.MathSpecials.Count > 0 || data.FitBudgets.Count > 0 || DeferApplies(m_TextLength, data))
                return false;
            // An edit past the first paragraph shapes on from where it starts, keeping what is before: a text that left the batch could not do that again.
            if (m_EditParse == ParseCount && m_Paragraphs.Count > 1) return false;
            if (m_Dirty >= DirtyLevel.Shape)
            {
                m_Chain = LetteraFontChains.Get(data.Font, data.FontStack);
                LetteraWordDictionaries.EnsureFromSettings();
            }
            // A thread reads the groups in drawing order, and must not be the one to sort them.
            m_Attributes.SortedGroups();
            m_FrozenRect = rect;
            m_FrozenScale = rasterScale;
            // A pixel's length asks the canvas, which only the main thread may.
            m_FrozenQuadMargin = QuadMargin();
            m_RectFrozen = m_BatchWorker = true;
            m_BatchMissed = m_BatchDone = m_BatchMeshMissed = m_BatchMeshDone = false;
            m_BatchHits = 0;
            m_BatchStamp = m_DirtyStamp;
            return true;
        }

        /// <summary>
        /// Any thread: the passes up to placing, as a query before the frame's rebuild runs them, and a plain mesh. A paragraph no cache holds stops it;
        /// a glyph the atlas lacks leaves the mesh to the main thread.
        /// </summary>
        internal void BatchPasses()
        {
            try
            {
                if (m_Dirty >= DirtyLevel.Shape)
                {
                    using (s_ShapeMarker.Auto()) { long t = PassStart(); ShapeText(); PassEnd(1, t); }
                    if (m_BatchMissed) return;
                }
                if (m_Dirty >= DirtyLevel.Layout)
                    using (s_LayoutMarker.Auto()) { long t = PassStart(); RunLayout(); PassEnd(2, t); }
                using (s_PositionsMarker.Auto()) { long t = PassStart(); PositionGlyphs(); PassEnd(3, t); }
                m_BatchDone = true;
                if (BatchMeshes && MeshAside)
                {
                    using (s_MeshMarker.Auto()) { long t = PassStart(); BuildMesh(); PassEnd(4, t); }
                    m_BatchMeshDone = !m_BatchMeshMissed;
                }
            }
            catch (Exception)
            {
                // Rebuilding alone on the main thread meets the same failure, where it is reported as ever.
                m_BatchMissed = true;
                m_BatchDone = m_BatchMeshDone = false;
            }
        }

        /// <summary>Main thread, after the threads: what they found becomes newest in the caches, and the mesh, or only its upload, is all a done text has left.</summary>
        internal void EndBatch()
        {
            m_BatchWorker = m_RectFrozen = m_BatchMissed = m_BatchMeshMissed = false;
            // Changed again while it was in the batch, from a project's code: what the threads made is of the text before, and it rebuilds alone.
            if (m_DirtyStamp != m_BatchStamp) m_BatchDone = m_BatchMeshDone = false;
            if (m_BatchTouched != null)
            {
                if (m_BatchDone)
                    foreach (IntPtr touched in m_BatchTouched)
                    {
                        var entry = (ParagraphEntry*)touched;
                        Unlink(entry);
                        LinkNewest(entry);
                    }
                m_BatchTouched.Clear();
            }
            if (!m_BatchDone) return;
            m_BatchDone = false;
            BatchedTexts++;
            LetteraCaches.s_Stats.Hits += m_BatchHits;
            if (m_Dirty >= DirtyLevel.Layout) m_EarlyChanges |= LetteraChanges.Layout;
            m_EarlyChanges |= LetteraChanges.Positions;
            m_Dirty = DirtyLevel.Mesh;
            if (!m_BatchMeshDone) return;
            m_BatchMeshDone = false;
            for (int i = 0; i < m_PlacedCount; i++) GlyphAtlas.Touch(m_Placed[i].Instance, m_PlacedAtlas[i]);
            UpdateHubMembership();
            m_MeshReady = true;
            BatchedMeshes++;
        }
    }
}
