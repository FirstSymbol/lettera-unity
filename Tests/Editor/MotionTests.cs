using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Motions, reveal and marquee: they move vertices and run no pass of the rebuild.</summary>
    public class MotionTests : TextFixture
    {
        [TearDown]
        public void Clean()
        {
            LetteraAccessibility.ReducedMotion = false;
        }

        static void Tick(float seconds, float unscaled = -1) => LetteraMotionHub.Tick(seconds, unscaled < 0 ? seconds : unscaled);

        static Vector3 FaceCenter(LetteraWorldText text, int quad)
        {
            TextEngine e = text.Engine;
            int v = quad * 4;
            return (e.Vertices[v] + e.Vertices[v + 2]) * 0.5f;
        }

        static float[] Ys(LetteraWorldText text)
        {
            TextEngine e = text.Engine;
            var ys = new float[e.VertexCount / 4];
            for (int q = 0; q < ys.Length; q++) ys[q] = FaceCenter(text, q).y;
            return ys;
        }

        static float[] Rests(LetteraWorldText text)
        {
            TextEngine e = text.Engine;
            var ys = new float[e.VertexCount / 4];
            for (int q = 0; q < ys.Length; q++) ys[q] = (e.RestVertex(q * 4).y + e.RestVertex(q * 4 + 2).y) * 0.5f;
            return ys;
        }

        [Test]
        public void APlainTextIsNotInTheHub()
        {
            int before = LetteraMotionHub.Count;
            LetteraWorldText text = Make("Plain");
            Assert.AreEqual(before, LetteraMotionHub.Count);
            int poses = text.PoseCount;
            Tick(0.1f);
            Assert.AreEqual(poses, text.PoseCount);
        }

        [Test]
        public void AWaveRunsNoPass()
        {
            LetteraWorldText text = Make("<wave>Some waving text</wave>");
            int rebuilds = text.RebuildCount, parses = text.ParseCount, shapes = text.ShapeCount, poses = text.PoseCount;
            float[] rest = Ys(text);
            for (int i = 0; i < 60; i++) Tick(0.016f);
            Assert.AreEqual((rebuilds, parses, shapes), (text.RebuildCount, text.ParseCount, text.ShapeCount));
            Assert.AreEqual(poses + 60, text.PoseCount);
            float[] moved = Ys(text);
            bool any = false;
            for (int q = 0; q < moved.Length; q++) any |= Mathf.Abs(moved[q] - rest[q]) > 0.01f;
            Assert.IsTrue(any);
        }

        [Test]
        public void TheWaveMatchesItsFormulaInReadingOrder()
        {
            var wave = new WaveMotion { Amplitude = 0.2f, Rate = 1, Step = 0.25f };
            LetteraWorldText text = Make("");
            text.UseGlobalStylePreset = false;
            text.Styles.Add(LetteraStyle.ForTag(wave, "w"));
            text.Text = "<w>HHH</w>";
            text.ForceRebuild();
            float[] rest = Rests(text);
            wave.Clock = LetteraClock.Manual;
            wave.Phase = 0.25f;
            Tick(0);
            float[] now = Ys(text);
            float em = 20;
            Assert.AreEqual(0.2f * em * Mathf.Sin(2 * Mathf.PI * 0.25f), now[0] - rest[0], 0.01f);
            Assert.AreEqual(0, now[1] - rest[1], 0.01f);
            Assert.AreEqual(0.2f * em * Mathf.Sin(-2 * Mathf.PI * 0.25f), now[2] - rest[2], 0.01f);
        }

        [Test]
        public void SteppingOrJumpingGivesTheSamePose()
        {
            LetteraWorldText a = Make("<wave>abc</wave>"), b = Make("<wave>abc</wave>");
            var wave = new WaveMotion();
            a.UseGlobalStylePreset = b.UseGlobalStylePreset = false;
            a.Styles.Add(LetteraStyle.ForTag(wave, "w"));
            b.Styles.Add(LetteraStyle.ForTag(new WaveMotion(), "w"));
            a.Text = b.Text = "<w>abc</w>";
            a.ForceRebuild();
            b.ForceRebuild();
            var pose = new WaveMotion { Amplitude = 0.3f };
            var values = new LetteraMotionValues { V0 = 0.3f, V2 = 1, V3 = 0.1f };
            var box = new LetteraBodyBox { XMax = 10, YMax = 10 };
            LetteraGlyphPose one = pose.PoseAt(values, 0.5f, 1, 3, box, 20);
            LetteraGlyphPose two = pose.PoseAt(values, 0.5f, 1, 3, box, 20);
            Assert.AreEqual(one.Ty, two.Ty, 1e-5f);
        }

        [Test]
        public void TimeScaleStopsOneClockAndNotTheOther()
        {
            LetteraWorldText scaled = Make("<wave>abc</wave>");
            float[] rest = Ys(scaled);
            LetteraMotionHub.Tick(0, 0.25f);
            CollectionAssert.AreEqual(rest, Ys(scaled), "a scaled wave stops at time scale 0");

            var free = new WaveMotion { Clock = LetteraClock.Unscaled };
            LetteraWorldText other = Make("");
            other.UseGlobalStylePreset = false;
            other.Styles.Add(LetteraStyle.ForTag(free, "w"));
            other.Text = "<w>abc</w>";
            other.ForceRebuild();
            float[] start = Ys(other);
            LetteraMotionHub.Tick(0, 0.25f);
            CollectionAssert.AreNotEqual(start, Ys(other));
        }

        [Test]
        public void AManualMotionMovesOnlyWhenWritten()
        {
            var wave = new WaveMotion { Clock = LetteraClock.Manual };
            LetteraWorldText text = Make("");
            text.UseGlobalStylePreset = false;
            text.Styles.Add(LetteraStyle.ForTag(wave, "w"));
            text.Text = "<w>abc</w>";
            text.ForceRebuild();
            int poses = text.PoseCount;
            Tick(1);
            Assert.AreEqual(poses, text.PoseCount);
            wave.Phase = 0.3f;
            Tick(0);
            float[] first = Ys(text);
            wave.Phase = 0.3f;
            Tick(1);
            CollectionAssert.AreEqual(first, Ys(text));
        }

        [Test]
        public void ReducedMotionRestsAndContinues()
        {
            LetteraWorldText text = Make("<wave>abc</wave>");
            float[] rest = Rests(text);
            Tick(0.3f);
            LetteraAccessibility.ReducedMotion = true;
            Tick(0.1f);
            CollectionAssert.AreEqual(rest, Ys(text));
            LetteraAccessibility.ReducedMotion = false;
            Tick(0.01f);
            CollectionAssert.AreNotEqual(rest, Ys(text));
        }

        [Test]
        public void AHiddenTextDoesNothing()
        {
            LetteraWorldText text = Make("<wave>abc</wave>");
            text.Hide();
            int poses = text.PoseCount;
            Tick(0.3f);
            Assert.AreEqual(poses, text.PoseCount);
        }

        [Test]
        public void AWordMovesAsOneBody()
        {
            var wave = new WaveMotion { Clock = LetteraClock.Manual, Unit = LetteraMotionUnit.Word, Step = 0.25f, Phase = 0.25f };
            LetteraWorldText text = Make("");
            text.UseGlobalStylePreset = false;
            text.Styles.Add(LetteraStyle.ForTag(wave, "w"));
            text.Text = "<w>ab cd</w>";
            text.ForceRebuild();
            float[] rest = Rests(text);
            wave.Phase = 0.25f;
            text.Engine.MarkPoseDirty();
            Tick(0);
            float[] now = Ys(text);
            Assert.AreEqual(now[0] - rest[0], now[1] - rest[1], 0.01f, "same word");
            Assert.AreNotEqual(now[0] - rest[0], now[now.Length - 1] - rest[rest.Length - 1], "another word");
        }

        [Test]
        public void LayersKeepTheirOffsetFromTheFace()
        {
            LetteraWorldText text = Make("<wave><shadow=#000,0.1,0.1>H</shadow></wave>");
            TextEngine e = text.Engine;
            Assert.AreEqual(8, e.VertexCount);
            Vector3 before = e.Vertices[4] - e.Vertices[0];
            Tick(0.2f);
            Vector3 after = e.Vertices[4] - e.Vertices[0];
            Assert.AreEqual(before.x, after.x, 0.01f);
            Assert.AreEqual(before.y, after.y, 0.01f);
        }

        [Test]
        public void ASpinKeepsAWordsCenter()
        {
            var spin = new SpinMotion { Clock = LetteraClock.Manual, Unit = LetteraMotionUnit.Word, Rate = 1 };
            var box = new LetteraBodyBox { XMin = 10, XMax = 30, YMin = 0, YMax = 20 };
            var values = new LetteraMotionValues { V0 = 1 };
            foreach (float t in new[] { 0.1f, 0.33f, 0.71f })
            {
                LetteraGlyphPose pose = spin.PoseAt(values, t, 0, 1, box, 20);
                Vector2 center = pose.Apply(box.Center);
                Assert.AreEqual(box.Center.x, center.x, 1e-3f);
                Assert.AreEqual(box.Center.y, center.y, 1e-3f);
            }
        }

        [Test]
        public void ABounceNeverGoesBelowRest()
        {
            var bounce = new BounceMotion();
            var values = new LetteraMotionValues { V0 = 0.3f, V1 = 1, V2 = 0.1f, V3 = 0.5f };
            var box = new LetteraBodyBox { XMax = 10, YMax = 10 };
            for (float t = 0; t < 3; t += 0.013f) Assert.GreaterOrEqual(bounce.PoseAt(values, t, 1, 3, box, 20).Ty, -1e-4f);
        }

        [Test]
        public void AShakeHoldsBetweenReRolls()
        {
            var shake = new ShakeMotion();
            var values = new LetteraMotionValues { V0 = 0.05f, V1 = 10 };
            var box = new LetteraBodyBox { XMax = 10, YMax = 10 };
            LetteraGlyphPose a = shake.PoseAt(values, 0.11f, 0, 1, box, 20), b = shake.PoseAt(values, 0.19f, 0, 1, box, 20), c = shake.PoseAt(values, 0.21f, 0, 1, box, 20);
            Assert.AreEqual(a.Tx, b.Tx);
            Assert.AreNotEqual(a.Tx, c.Tx);
        }

        [Test]
        public void MotionsRepeatOnTheirPeriod()
        {
            var box = new LetteraBodyBox { XMax = 10, YMax = 10 };
            void Same(LetteraMotionEffect motion, in LetteraMotionValues v, float period)
            {
                LetteraGlyphPose a = motion.PoseAt(v, 0.17f, 1, 3, box, 20), b = motion.PoseAt(v, 0.17f + period, 1, 3, box, 20);
                Assert.AreEqual(a.Tx, b.Tx, 1e-2f, motion.GetType().Name);
                Assert.AreEqual(a.Ty, b.Ty, 1e-2f, motion.GetType().Name);
                Assert.AreEqual(a.A, b.A, 1e-3f, motion.GetType().Name);
            }
            Same(new WaveMotion(), new LetteraMotionValues { V0 = 0.2f, V2 = 1, V3 = 0.1f }, 1);
            Same(new WobbleMotion(), new LetteraMotionValues { V0 = 8, V1 = 0.1f, V2 = 2, V3 = 0.1f }, 0.5f);
            Same(new BounceMotion(), new LetteraMotionValues { V0 = 0.3f, V1 = 1, V2 = 0.1f, V3 = 0.5f }, 1);
            Same(new SpinMotion(), new LetteraMotionValues { V0 = 0.5f, V1 = 0.05f }, 2);
            Same(new PulseMotion(), new LetteraMotionValues { V0 = 0.1f, V1 = 1, V2 = 0.1f }, 1);
            Same(new PendulumMotion(), new LetteraMotionValues { V0 = 15, V1 = 0.5f, V2 = 0.1f }, 2);
        }

        [Test]
        public void TwoMotionsComposeInStyleOrder()
        {
            LetteraWorldText text = Make("<wave><pulse>H</pulse></wave>");
            float[] rest = Ys(text);
            Tick(0.2f);
            CollectionAssert.AreNotEqual(rest, Ys(text));
        }

        [Test]
        public void AHoldOnAPoseParameterComposesWithNoReapply()
        {
            var wave = new WaveMotion { Clock = LetteraClock.Manual, Step = 0, Phase = 0.25f };
            LetteraWorldText text = Make("");
            text.UseGlobalStylePreset = false;
            text.Styles.Add(LetteraStyle.ForTag(wave, "w"));
            text.Text = "<w=0.2>H</w>";
            text.ForceRebuild();
            float rest = Rests(text)[0];
            text.Engine.MarkPoseDirty();
            Tick(0);
            float full = Ys(text)[0] - rest;
            int parses = text.ParseCount, rebuilds = text.RebuildCount;
            LetteraHold<float> hold = text.Ranges(wave).Hold<float>(0, LetteraHoldComposition.Multiply);
            hold.Value = 0.5f;
            Tick(0);
            Assert.AreEqual(full * 0.5f, Ys(text)[0] - rest, 0.01f);
            Assert.AreEqual((parses, rebuilds), (text.ParseCount, text.RebuildCount));
            hold.Release();
            Tick(0);
            Assert.AreEqual(full, Ys(text)[0] - rest, 0.01f);
        }

        [Test]
        public void ParametersAreNamedWithTheirCost()
        {
            IReadOnlyList<LetteraParamInfo> table = new WaveMotion().Parameters;
            Assert.AreEqual("Amplitude", table[0].Id);
            Assert.AreEqual(0, table[0].Slot);
            Assert.AreEqual(DirtyLevel.Pose, table[0].Level);
            Assert.AreEqual(0, new UppercaseEffect().Parameters.Count);
        }

        [Test]
        public void ASharedMotionHasAPhasePerTextAndItsFieldReachesBoth()
        {
            var wave = new WaveMotion();
            LetteraWorldText a = Make(""), b = Make("");
            foreach (LetteraWorldText t in new[] { a, b })
            {
                t.UseGlobalStylePreset = false;
                t.Styles.Add(LetteraStyle.ForTag(wave, "w"));
                t.Text = "<w>H</w>";
                t.ForceRebuild();
            }
            int parses = a.ParseCount;
            float[] rest = Rests(a);
            Tick(0.25f);
            float before = Ys(a)[0] - rest[0];
            wave.Amplitude = wave.Amplitude * 2;
            LetteraMotionHub.Tick(0, 0);
            Assert.AreEqual(before * 2, Ys(a)[0] - rest[0], 0.05f);
            Assert.AreEqual(parses, a.ParseCount);
        }

        [Test]
        public void ColorGlyphsAndFamiliesMoveAsOne()
        {
            LetteraWorldText text = Make("<wave>ab</wave>");
            Tick(0.1f);
            Assert.AreEqual(2, text.Engine.VertexCount / 4);
        }

        // ------------------------------------------------------------- glitch, scramble, roll

        [Test]
        public void AGlitchKeepsItsVertexCount()
        {
            var glitch = new GlitchMotion { Chance = 1 };
            LetteraWorldText text = Make("");
            text.UseGlobalStylePreset = false;
            text.Styles.Add(LetteraStyle.ForTag(glitch, "g"));
            text.Text = "<g>HI</g>";
            text.ForceRebuild();
            int vertices = text.Engine.VertexCount, rebuilds = text.RebuildCount;
            Assert.AreEqual(4 * 6, vertices, "face plus two fringes per glyph");
            for (int i = 0; i < 30; i++) Tick(0.05f);
            Assert.AreEqual(vertices, text.Engine.VertexCount);
            Assert.AreEqual(rebuilds, text.RebuildCount);
            TextEngine e = text.Engine;
            Assert.Greater(e.Vertices[2].x - e.Vertices[0].x, 0, "a burst gives the fringe area");
            glitch.Chance = 0;
            Tick(0.05f);
            Assert.AreEqual(0, e.Vertices[2].x - e.Vertices[0].x, 1e-4f, "no burst, no area");
        }

        [Test]
        public void ScrambleSettlesAndKeepsTheRealText()
        {
            var scramble = new ScrambleMotion { Clock = LetteraClock.Manual, Seconds = 0, Rate = 10 };
            LetteraWorldText text = Make("");
            text.UseGlobalStylePreset = false;
            text.Styles.Add(LetteraStyle.ForTag(scramble, "s"));
            text.Text = "<s>Ab c</s>";
            text.ForceRebuild();
            Assert.AreEqual("Ab c", text.CleanText);
            TextEngine e = text.Engine;
            int quads = e.VertexCount / 4;
            var restUv = new Vector4[e.VertexCount];
            scramble.Progress = 1;
            Tick(0);
            Array.Copy(e.Uv0, restUv, e.VertexCount);
            Vector3[] restPositions = (Vector3[])e.Vertices.Clone();

            scramble.Progress = 0;
            scramble.Phase = 0.5f;
            Tick(0);
            bool changed = false;
            for (int v = 0; v < e.VertexCount; v++) changed |= e.Uv0[v] != restUv[v];
            Assert.IsTrue(changed, "at 0 symbols show");
            Assert.AreEqual("Ab c", text.CleanText);
            Assert.AreEqual(quads, e.VertexCount / 4);

            scramble.Progress = 1;
            Tick(0);
            for (int v = 0; v < e.VertexCount; v++)
            {
                Assert.AreEqual(restUv[v], e.Uv0[v]);
                Assert.AreEqual(restPositions[v], e.Vertices[v]);
            }
        }

        [Test]
        public void ARollSettlesAndLeavesOtherCharactersStill()
        {
            var roll = new RollMotion { Clock = LetteraClock.Manual, Lag = 2 };
            LetteraWorldText text = Make("");
            text.UseGlobalStylePreset = false;
            text.Styles.Add(LetteraStyle.ForTag(roll, "r"));
            text.Text = "<r>A12</r>";
            text.ForceRebuild();
            TextEngine e = text.Engine;
            roll.Roll = 0;
            Tick(0);
            Vector4[] restUv = (Vector4[])e.Uv0.Clone();
            Vector3[] restPositions = (Vector3[])e.Vertices.Clone();
            roll.Roll = 1.5f;
            Tick(0);
            bool changed = false;
            for (int v = 0; v < e.VertexCount; v++) changed |= e.Vertices[v] != restPositions[v];
            Assert.IsTrue(changed);
            roll.Roll = 0;
            Tick(0);
            for (int v = 0; v < e.VertexCount; v++)
            {
                Assert.AreEqual(restUv[v], e.Uv0[v]);
                Assert.AreEqual(restPositions[v], e.Vertices[v]);
            }
        }

        [Test]
        public void ADeformWritesOnlyTheThirdUv()
        {
            LetteraWorldText text = Make("<deform>H</deform>");
            TextEngine e = text.Engine;
            Assert.IsTrue(e.Styled);
            float[] rest = Ys(text);
            float before = e.Uv3[0].z;
            Tick(0.3f);
            Assert.AreNotEqual(before, e.Uv3[0].z);
            CollectionAssert.AreEqual(rest, Ys(text));
        }

        // ------------------------------------------------------------- reveal

        LetteraWorldText Revealed(string markup, RevealEffect effect, float width = 1000)
        {
            LetteraWorldText text = Make("", width);
            text.UseGlobalStylePreset = false;
            text.Styles.Add(LetteraStyle.ForTag(effect, "r"));
            text.Text = markup;
            text.ForceRebuild();
            return text;
        }

        static float Alpha(LetteraWorldText text, int quad) => text.Engine.Colors[quad * 4].a / 255f;

        [Test]
        public void AFrontierShowsTheLeadingPartAtTheFullVertexCount()
        {
            var reveal = new RevealEffect { Reach = "50%" };
            LetteraWorldText text = Revealed("<r>abcd</r>", reveal);
            Tick(1);
            text.ForceRebuild();
            Assert.AreEqual(16, text.Engine.VertexCount);
            Assert.AreEqual((1f, 1f, 0f, 0f), (Alpha(text, 0), Alpha(text, 1), Alpha(text, 2), Alpha(text, 3)));
        }

        [Test]
        public void AFractionalFrontierBlendsItsUnit()
        {
            var reveal = new RevealEffect { Reach = "2.5", Entrance = "fade" };
            reveal.Entries.Add(new LetteraEntranceEntry { Name = "fade", Enter = new LetteraEntrance { Kind = LetteraEntranceKind.Fade, Duration = 0.01f, Ease = LetteraEase.Linear } });
            LetteraWorldText text = Revealed("<r>abcd</r>", reveal);
            Tick(1);
            Tick(1);
            Assert.AreEqual(1, Alpha(text, 1), 0.01f);
            Assert.AreEqual(0.5f, Alpha(text, 2), 0.02f);
            Assert.AreEqual(0, Alpha(text, 3), 0.01f);
        }

        [Test]
        public void KeepingSpaceLeavesPositionsAndReflowClosesLines()
        {
            var keep = new RevealEffect { Reach = "50%" };
            LetteraWorldText text = Revealed("<r>aaaa bbbb</r>", keep, 70);
            Tick(1);
            Assert.AreEqual(2, text.Lines.Count);

            var reflow = new RevealEffect { Reach = "4", Reflow = true };
            LetteraWorldText closed = Revealed("<r>aaaa bbbb</r>", reflow, 70);
            int shapes = closed.ShapeCount;
            Tick(1);
            closed.ForceRebuild();
            Assert.AreEqual(1, closed.Lines.Count);
            Assert.AreEqual(shapes, closed.ShapeCount, "no shaping for a change of reach");
            reflow.Reach = "100%";
            Tick(1);
            closed.ForceRebuild();
            Assert.AreEqual(2, closed.Lines.Count);
        }

        [Test]
        public void ARecedingFrontierKeepsLayoutUntilItEnds()
        {
            var reveal = new RevealEffect { Reach = "100%", Reflow = true, Entrance = "out" };
            reveal.Entries.Add(new LetteraEntranceEntry { Name = "out", Enter = new LetteraEntrance { Kind = LetteraEntranceKind.Fade, Duration = 0.25f, Ease = LetteraEase.Linear } });
            LetteraWorldText text = Revealed("<r>aaaa bbbb</r>", reveal, 70);
            for (int i = 0; i < 5; i++) Tick(0.1f);
            text.ForceRebuild();
            Assert.AreEqual(2, text.Lines.Count);
            reveal.Reach = "0";
            Tick(0.1f);
            text.ForceRebuild();
            Assert.AreEqual(2, text.Lines.Count, "still fading");
            Tick(0.25f);
            text.ForceRebuild();
            Assert.LessOrEqual(text.Lines.Count, 1);
        }

        [Test]
        public void TurningBackGoesOnFromWhereItWas()
        {
            var reveal = new RevealEffect { Reach = "100%", Entrance = "f" };
            reveal.Entries.Add(new LetteraEntranceEntry { Name = "f", Enter = new LetteraEntrance { Kind = LetteraEntranceKind.Fade, Duration = 0.4f, Ease = LetteraEase.Linear } });
            LetteraWorldText text = Revealed("<r>a</r>", reveal);
            Tick(0.2f);
            float half = Alpha(text, 0);
            Assert.AreEqual(0.5f, half, 0.02f);
            reveal.Reach = "0";
            Tick(0.04f);
            Assert.AreEqual(0.4f, Alpha(text, 0), 0.02f);
        }

        [Test]
        public void ASharedFrontierRunsThroughRangesOneAfterAnother()
        {
            var shared = new RevealEffect { Reach = "50%" };
            LetteraWorldText text = Revealed("<r>ab</r><r>cd</r>", shared);
            Tick(1);
            Assert.AreEqual((1f, 1f, 0f, 0f), (Alpha(text, 0), Alpha(text, 1), Alpha(text, 2), Alpha(text, 3)));
            var own = new RevealEffect { Reach = "50%", Independent = true };
            LetteraWorldText other = Revealed("<r>ab</r><r>cd</r>", own);
            Tick(1);
            Assert.AreEqual((1f, 0f, 1f, 0f), (Alpha(other, 0), Alpha(other, 1), Alpha(other, 2), Alpha(other, 3)));
        }

        [Test]
        public void EveryEntranceEndsAtRest()
        {
            var box = new LetteraBodyBox { XMin = 2, XMax = 12, YMin = -3, YMax = 15 };
            foreach (LetteraEntranceKind kind in Enum.GetValues(typeof(LetteraEntranceKind)))
            {
                var entrance = new LetteraEntrance { Kind = kind };
                LetteraGlyphPose pose = LetteraGlyphPose.Identity;
                entrance.Pose(1, box, 20, 3, 0, ref pose);
                Assert.IsTrue(pose.IsIdentity, kind.ToString());
            }
        }

        [Test]
        public void EventsFireOncePerUnitAndOncePerRun()
        {
            var reveal = new RevealEffect { Reach = "100%" };
            var shown = new List<(int, int)>();
            int settled = 0;
            reveal.UnitShown += (a, b) => shown.Add((a, b));
            reveal.Settled += () => settled++;
            Revealed("<r>abc</r>", reveal);
            Tick(1);
            Tick(1);
            CollectionAssert.AreEqual(new[] { (0, 1), (1, 2), (2, 3) }, shown);
            Assert.AreEqual(1, settled);
        }

        [Test]
        public void ASettledRevealLeavesTheHub()
        {
            int before = LetteraMotionHub.Count;
            var reveal = new RevealEffect { Reach = "100%" };
            Revealed("<r>abc</r>", reveal);
            Assert.AreEqual(before + 1, LetteraMotionHub.Count);
            Tick(1);
            Assert.AreEqual(before, LetteraMotionHub.Count);
            reveal.Reach = "0";
            Assert.AreEqual(before + 1, LetteraMotionHub.Count);
        }

        [Test]
        public void ManualRevealMovesOnlyByTick()
        {
            var reveal = new RevealEffect { Reach = "100%", Clock = LetteraClock.Manual, Entrance = "f" };
            reveal.Entries.Add(new LetteraEntranceEntry { Name = "f", Enter = new LetteraEntrance { Kind = LetteraEntranceKind.Fade, Duration = 1, Ease = LetteraEase.Linear } });
            LetteraWorldText text = Revealed("<r>a</r>", reveal);
            Tick(5);
            Assert.AreEqual(0, Alpha(text, 0), 0.01f);
            reveal.Tick(0.5f);
            Tick(0);
            Assert.AreEqual(0.5f, Alpha(text, 0), 0.02f);
        }

        [Test]
        public void AGroupFadeOverARevealShowsNoHiddenGlyph()
        {
            var reveal = new RevealEffect { Reach = "50%" };
            LetteraWorldText text = Revealed("<r>ab</r>", reveal);
            Tick(1);
            text.Color = new Color(1, 1, 1, 0.5f);
            text.ForceRebuild();
            Assert.AreEqual(0.5f, Alpha(text, 0), 0.01f);
            Assert.AreEqual(0, Alpha(text, 1), 0.01f);
        }

        [Test]
        public void AppendingKeepsWhatIsShown()
        {
            var reveal = new RevealEffect { Reach = "100%", Entrance = "f" };
            reveal.Entries.Add(new LetteraEntranceEntry { Name = "f", Enter = new LetteraEntrance { Kind = LetteraEntranceKind.Fade, Duration = 0.2f, Ease = LetteraEase.Linear } });
            LetteraWorldText text = Revealed("<r>ab</r>", reveal);
            Tick(1);
            text.Text = "<r>abc</r>";
            text.ForceRebuild();
            Assert.AreEqual(1, Alpha(text, 0), 0.01f, "the old text does not animate");
            Assert.AreEqual(0, Alpha(text, 2), 0.01f);
            Tick(0.1f);
            Assert.AreEqual(0.5f, Alpha(text, 2), 0.02f);
        }

        // ------------------------------------------------------------- marquee

        readonly List<GameObject> m_Canvases = new List<GameObject>();

        [TearDown]
        public void CleanCanvases()
        {
            foreach (GameObject go in m_Canvases) Object.DestroyImmediate(go);
            m_Canvases.Clear();
        }

        LetteraText Marquee(string value, MarqueeEffect effect, float width = 100)
        {
            var go = new GameObject("Canvas", typeof(Canvas));
            m_Canvases.Add(go);
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var child = new GameObject("Text", typeof(RectTransform));
            child.transform.SetParent(go.transform, false);
            ((RectTransform)child.transform).sizeDelta = new Vector2(width, 40);
            var text = child.AddComponent<LetteraText>();
            text.Font = Latin;
            text.FontSize = 20;
            text.WordWrap = false;
            text.Styles.Add(LetteraStyle.ForWholeText(effect));
            text.Text = value;
            text.ForceRebuild();
            return text;
        }

        [Test]
        public void ATextThatFitsDoesNotScroll()
        {
            LetteraText text = Marquee("ab", new MarqueeEffect());
            int plain = text.GlyphCount * 4;
            Assert.AreEqual(plain, text.SharedEngineForTests.VertexCount);
            Assert.IsFalse(text.SharedEngineForTests.IsMarqueeScrolling);
            LetteraText forced = Marquee("ab", new MarqueeEffect { EvenIfFits = true });
            Assert.IsTrue(forced.SharedEngineForTests.IsMarqueeScrolling);
            Assert.Greater(forced.SharedEngineForTests.VertexCount, forced.GlyphCount * 4);
        }

        [Test]
        public void ALoopFollowsItsFormulaAndWrapsWithNoJump()
        {
            var effect = new MarqueeEffect { Gap = "20px", Speed = "10px", Pause = 1 };
            string value = new string('a', 40);
            LetteraText text = Marquee(value, effect);
            TextEngine e = text.SharedEngineForTests;
            Assert.IsTrue(e.IsMarqueeScrolling);
            int rebuilds = text.RebuildCount;
            Tick(0.5f);
            Assert.AreEqual(0, e.MarqueeOffset, 0.01f, "the pause");
            Tick(0.5f);
            Tick(0.25f);
            Assert.AreEqual(-2.5f, e.MarqueeOffset, 0.05f);
            for (int i = 0; i < 100; i++) Tick(0.1f);
            Assert.AreEqual(rebuilds, text.RebuildCount);
            float d = e.LayoutBounds.width + 20;
            double period = 1 + d / 10f;
            Assert.AreEqual(e.MarqueeOffsetAt(0.5), e.MarqueeOffsetAt(period + 0.5), 0.01f);
        }

        [Test]
        public void BackAndForthRestsAtBothEnds()
        {
            var effect = new MarqueeEffect { Mode = LetteraMarqueeMode.BackAndForth, Speed = "20px", Pause = 1 };
            LetteraText text = Marquee(new string('a', 40), effect);
            TextEngine e = text.SharedEngineForTests;
            float span = e.LayoutBounds.width - 100;
            Assert.Greater(span, 0);
            float travel = span / 20f;
            Assert.AreEqual(0, e.MarqueeOffsetAt(0.5), 0.01f);
            Assert.AreEqual(-span, e.MarqueeOffsetAt(1 + travel + 0.5), 0.02f);
            Assert.AreEqual(0, e.MarqueeOffsetAt(2 * (1 + travel) + 0.5), 0.02f);
        }

        [Test]
        public void ReducedMotionRestsTheMarquee()
        {
            LetteraText text = Marquee(new string('a', 40), new MarqueeEffect { Pause = 0 });
            TextEngine e = text.SharedEngineForTests;
            Tick(0.5f);
            Assert.AreNotEqual(0, e.MarqueeOffset);
            LetteraAccessibility.ReducedMotion = true;
            Tick(0.1f);
            Assert.AreEqual(0, e.MarqueeOffset);
        }

        [Test]
        public void AClickOnAnyCopyMeansTheFirst()
        {
            var effect = new MarqueeEffect { Gap = "0px", Pause = 0, Speed = "0" };
            LetteraText text = Marquee(new string('a', 40), effect);
            TextEngine e = text.SharedEngineForTests;
            Rect bounds = e.LayoutBounds;
            float y = (bounds.yMin + bounds.yMax) * 0.5f;
            int first = e.HitTestCaret(new Vector2(bounds.xMin + 12, y), out _);
            int copy = e.HitTestCaret(new Vector2(bounds.xMin + bounds.width + 12, y), out _);
            Assert.AreEqual(first, copy);
        }

        [Test]
        public void WorldTextRefusesAMarquee()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForWholeText(new MarqueeEffect { EvenIfFits = true }));
            text.Text = "ab";
            text.ForceRebuild();
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            Assert.IsFalse(text.Engine.IsMarqueeScrolling);
            Assert.AreEqual(8, text.Engine.VertexCount);
        }
    }
}
