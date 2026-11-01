using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The sequencer's timing and repeat modes, the parameter link, and what stands inline.</summary>
    public class SequencerInlineTests : EditingFixture
    {
        static void Clock(float seconds) => LetteraMotionHub.Tick(seconds, seconds);

        LetteraSequencer Sequenced(string body, out LetteraWorldText text, out WaveMotion wave, params LetteraCue[] cues)
        {
            text = Make("");
            wave = new WaveMotion();
            text.Styles.Clear();
            text.Styles.Add(LetteraStyle.ForTag(wave, "go"));
            text.Text = body;
            text.ForceRebuild();
            var sequencer = text.gameObject.AddComponent<LetteraSequencer>();
            sequencer.Cues.AddRange(cues);
            return sequencer;
        }

        static LetteraCue Cue(float from, float to, float unit = 2, float step = 0, float start = 0, LetteraMix mix = LetteraMix.Replace) => new LetteraCue
        {
            Target = new LetteraParamTarget { Style = 0, Parameter = "Amplitude" },
            From = LetteraValue.Of(from),
            To = LetteraValue.Of(to),
            UnitSeconds = unit,
            StepSeconds = step,
            Start = start,
            Mix = mix,
        };

        static float HeldAmplitude(LetteraWorldText text, int range = 0)
        {
            text.ForceRebuild();
            return text.Engine.MotionValueForTests(range, 0);
        }

        [Test]
        public void ALengthFollowsMembersAndEachRangeHasItsOwnRamp()
        {
            LetteraSequencer sequencer = Sequenced("<go>a</go> <go>b</go>", out LetteraWorldText text, out _, Cue(0, 1, 2, 1));
            Assert.AreEqual(3f, sequencer.ResolvedLength, 1e-4f);
            sequencer.Seek(1.5f);
            Clock(0.0001f);
            Assert.AreEqual(0.75f, HeldAmplitude(text, 0), 1e-3f);
            Assert.AreEqual(0.25f, HeldAmplitude(text, 1), 1e-3f);
        }

        [Test]
        public void OutsideItsWindowACueHoldsItsEdges()
        {
            LetteraSequencer sequencer = Sequenced("<go>a</go>", out LetteraWorldText text, out _, Cue(0.2f, 0.6f, 1, 0, 2));
            sequencer.Length = 10;
            sequencer.Seek(0.5f);
            Clock(0.0001f);
            Assert.AreEqual(0.2f, HeldAmplitude(text), 1e-3f);
            sequencer.Seek(5);
            Clock(0.0001f);
            Assert.AreEqual(0.6f, HeldAmplitude(text), 1e-3f);
        }

        [Test]
        public void ALaterReplaceCueTakesOverAndAddKeepsAdding()
        {
            LetteraSequencer sequencer = Sequenced("<go>a</go>", out LetteraWorldText text, out _, Cue(0, 1, 1), Cue(5, 7, 1, 0, 3), Cue(10, 10, 1, 0, 0, LetteraMix.Add));
            sequencer.Seek(2);
            Clock(0.0001f);
            Assert.AreEqual(11, HeldAmplitude(text), 1e-3f);
            sequencer.Seek(3.5f);
            Clock(0.0001f);
            Assert.AreEqual(16, HeldAmplitude(text), 1e-3f);
        }

        [Test]
        public void ATextChangedUnderALiveCueIsNoEndlessParse()
        {
            LetteraSequencer sequencer = Sequenced("<go>a</go>", out LetteraWorldText text, out _, Cue(0, 1));
            sequencer.Seek(0.5f);
            text.Text = "<go>other</go>";
            Assert.DoesNotThrow(() => sequencer.Seek(0.75f));
            Clock(0.0001f);
            Assert.AreEqual(0.75f, HeldAmplitude(text), 1e-3f);
        }

        [Test]
        public void TheSameProgressGivesTheSameVertices()
        {
            LetteraSequencer sequencer = Sequenced("<go>wave</go>", out LetteraWorldText text, out WaveMotion wave, Cue(0, 1, 2));
            wave.Clock = LetteraClock.Manual;
            wave.Phase = 0.4f;
            sequencer.Progress = 0.5f;
            Clock(0.0001f);
            Vector3 a = text.Engine.Vertices[0];
            sequencer.Progress = 0.2f;
            Clock(0.0001f);
            sequencer.Progress = 0.5f;
            Clock(0.0001f);
            Assert.AreEqual(a, text.Engine.Vertices[0]);
        }

        [TestCase(LetteraRepeat.Retrace, 0f)]
        [TestCase(LetteraRepeat.Return, 0f)]
        [TestCase(LetteraRepeat.Restart, 1f)]
        public void RepeatModesEndWhereTheyShould(LetteraRepeat repeat, float end)
        {
            LetteraSequencer sequencer = Sequenced("<go>a</go>", out LetteraWorldText text, out _, Cue(0, 1, 1));
            sequencer.Repeat = repeat;
            sequencer.Cycles = 2;
            sequencer.Play();
            for (int i = 0; i < 25; i++) Clock(0.1f);
            Assert.IsFalse(sequencer.IsPlaying);
            Assert.AreEqual(end, HeldAmplitude(text), 1e-3f);
        }

        [Test]
        public void AccumulateGoesOnWithNoSeam()
        {
            LetteraSequencer sequencer = Sequenced("<go>a</go>", out LetteraWorldText text, out _, Cue(0, 1, 1));
            sequencer.Repeat = LetteraRepeat.Accumulate;
            sequencer.Cycles = 3;
            sequencer.Play();
            for (int i = 0; i < 40; i++) Clock(0.1f);
            Assert.AreEqual(3, HeldAmplitude(text), 1e-3f);
        }

        [Test]
        public void ARetracePassReadsTheCurveMirroredAndAReturnPassReadsItAfresh()
        {
            LetteraCue cue = Cue(0, 1, 1);
            cue.Curve = LetteraEase.InQuad;
            LetteraSequencer sequencer = Sequenced("<go>a</go>", out LetteraWorldText text, out _, cue);
            sequencer.Repeat = LetteraRepeat.Retrace;
            sequencer.Cycles = 2;
            sequencer.Play();
            Clock(1f / 4);
            Clock(1f / 4);
            Clock(1f / 4);
            Clock(1f / 4);
            Clock(0.25f);
            Assert.AreEqual(0.5625f, HeldAmplitude(text), 1e-2f);

            sequencer.Stop();
            sequencer.Repeat = LetteraRepeat.Return;
            sequencer.Play();
            for (int i = 0; i < 5; i++) Clock(0.25f);
            Assert.AreEqual(0.9375f, HeldAmplitude(text), 1e-2f);
        }

        [Test]
        public void NegativeSpeedRunsBackAndFinishes()
        {
            LetteraSequencer sequencer = Sequenced("<go>a</go>", out LetteraWorldText text, out _, Cue(0, 1, 1));
            sequencer.Speed = -1;
            int finished = 0;
            sequencer.Finished += () => finished++;
            sequencer.Seek(1);
            sequencer.Play();
            for (int i = 0; i < 12; i++) Clock(0.1f);
            Assert.AreEqual(1, finished);
            Assert.AreEqual(0, HeldAmplitude(text), 1e-3f);
        }

        [Test]
        public void DisablingLetsEveryValueGoAndTheFieldNeverChanged()
        {
            LetteraSequencer sequencer = Sequenced("<go>a</go>", out LetteraWorldText text, out WaveMotion wave, Cue(0, 9, 1));
            float field = wave.Amplitude;
            sequencer.Seek(1);
            Clock(0.0001f);
            Assert.AreEqual(9, HeldAmplitude(text), 1e-3f);
            sequencer.enabled = false;
            Clock(0.0001f);
            Assert.AreEqual(field, HeldAmplitude(text), 1e-3f);
            Assert.AreEqual(field, wave.Amplitude);
            Assert.AreEqual(0, text.Engine.HoldCount);
        }

        [Test]
        public void ANewMatchJoinsAndTheLengthFollows()
        {
            LetteraCue cue = Cue(0, 1, 1, 1);
            cue.Select.Label = "x";
            LetteraSequencer sequencer = Sequenced("<go#x>a</go>", out LetteraWorldText text, out _, cue);
            Assert.AreEqual(1f, sequencer.ResolvedLength, 1e-4f);
            text.Text = "<go#x>a</go> <go#x>b</go>";
            text.ForceRebuild();
            Assert.AreEqual(2f, sequencer.ResolvedLength, 1e-4f);
        }

        [Test]
        public void AManualClockMovesOnlyByAdvance()
        {
            LetteraSequencer sequencer = Sequenced("<go>a</go>", out LetteraWorldText text, out _, Cue(0, 1, 1));
            sequencer.Clock = LetteraClock.Manual;
            sequencer.Play();
            Clock(0.5f);
            Assert.AreEqual(0, HeldAmplitude(text), 1e-3f);
            sequencer.Advance(0.5f);
            Clock(0.0001f);
            Assert.AreEqual(0.5f, HeldAmplitude(text), 1e-3f);
        }

        [Test]
        public void AMissingEffectIsAProblemAndComesBackByItself()
        {
            LetteraCue cue = Cue(0, 1, 1);
            cue.Target.Parameter = "NoSuch";
            LetteraSequencer sequencer = Sequenced("<go>a</go>", out LetteraWorldText text, out _, cue);
            sequencer.Seek(1);
            StringAssert.Contains("NoSuch", cue.Problem);
            cue.Target.Parameter = "Amplitude";
            sequencer.Seek(1);
            Assert.IsNull(cue.Problem);
            Assert.AreEqual(1, HeldAmplitude(text), 1e-3f);
        }

        [Test]
        public void ReducedMotionJumpsToTheEnd()
        {
            LetteraSequencer sequencer = Sequenced("<go>a</go>", out LetteraWorldText text, out _, Cue(0, 1, 1));
            LetteraAccessibility.ReducedMotion = true;
            bool finished = false;
            sequencer.Finished += () => finished = true;
            sequencer.Play();
            Assert.IsTrue(finished);
            Assert.AreEqual(1, HeldAmplitude(text), 1e-3f);
        }

        [Test]
        public void AParamLinkDrivesWithNoParse()
        {
            LetteraWorldText text = Make("");
            text.Styles.Clear();
            text.Styles.Add(LetteraStyle.ForTag(new WaveMotion(), "go"));
            text.Text = "<go>wave</go>";
            text.ForceRebuild();
            var link = text.gameObject.AddComponent<LetteraParamLink>();
            link.Target.Style = 0;
            link.Target.Parameter = "Amplitude";
            int parses = text.Engine.ParseCount;
            link.Value = 0.7f;
            Clock(0.0001f);
            Assert.AreEqual(0.7f, HeldAmplitude(text), 1e-3f);
            Assert.AreEqual(parses, text.Engine.ParseCount);
            link.enabled = false;
            Assert.AreEqual(0, text.Engine.HoldCount);
        }

        // ------------------------------------------------------------- inline

        Sprite m_Sprite;

        Sprite Sprite
        {
            get
            {
                if (m_Sprite != null) return m_Sprite;
                var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                var pixels = new Color32[64];
                for (int i = 0; i < 64; i++) pixels[i] = new Color32(255, 0, 0, 255);
                texture.SetPixels32(pixels);
                texture.Apply();
                m_Sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f));
                return m_Sprite;
            }
        }

        [TearDown]
        public void DropSprite()
        {
            if (m_Sprite == null) return;
            Object.DestroyImmediate(m_Sprite.texture);
            Object.DestroyImmediate(m_Sprite);
        }

        LetteraWorldText Inline(string body, float width = 1000, System.Action<LetteraInlineEntry> shape = null, LetteraWorldText text = null)
        {
            text = text != null ? text : Make("", width);
            var entry = new LetteraInlineEntry { Key = "x", Sprite = Sprite, Alt = "star" };
            shape?.Invoke(entry);
            var effect = new InlineEffect();
            effect.Items.Add(entry);
            text.Styles.Add(LetteraStyle.ForMatcher(new InlineMediaMatcher("pic"), effect));
            text.Text = body;
            text.ForceRebuild();
            return text;
        }

        [Test]
        public void OneTagIsOneCharacter()
        {
            LetteraWorldText text = Inline("a<pic=x>b");
            ReadOnlySpanEquals(text, 'a', 0xFFFC, 'b');
            Assert.AreEqual(1, text.Engine.SourceOfRenderedForTests(1));
            Assert.AreEqual(8, text.Engine.SourceOfRenderedForTests(2));
        }

        static void ReadOnlySpanEquals(LetteraWorldText text, params int[] cps)
        {
            System.ReadOnlySpan<int> shown = text.Engine.ShownCodepoints;
            Assert.AreEqual(cps.Length, shown.Length);
            for (int i = 0; i < cps.Length; i++) Assert.AreEqual(cps[i], shown[i]);
        }

        [Test]
        public void TheDefaultItemKeepsTheLineAndALargeOneGrowsIt()
        {
            float plain = Make("ab").GetHeightForWidth(1000);
            LetteraWorldText text = Inline("a<pic=x>b");
            Assert.AreEqual(plain, text.GetHeightForWidth(1000), 0.01f);
            LetteraWorldText large = Inline("a<pic=x,3>b");
            Assert.Greater(large.GetHeightForWidth(1000), plain * 1.5f);
            LetteraWorldText held = Inline("a<pic=x,3,,,1>b");
            Assert.Less(held.GetHeightForWidth(1000), large.GetHeightForWidth(1000));
        }

        [Test]
        public void ItsWidthIsItsAdvanceAndASpriteHasOneQuadOfOwnTexture()
        {
            float bare = Make("ab").GetMaxContentWidth();
            LetteraWorldText text = Inline("a<pic=x>b");
            Assert.AreEqual(bare + 20, text.GetMaxContentWidth(), 0.5f);
            bool textured = false;
            foreach (MeshSegment segment in text.Engine.Segments) textured |= segment.Texture == Sprite.texture;
            Assert.IsTrue(textured);
            Assert.AreEqual(3 * 4, text.Engine.VertexCount);
        }

        [Test]
        public void ItWrapsWhole()
        {
            LetteraWorldText text = Inline("aaaa<pic=x,3>", 100);
            Assert.AreEqual(2, text.Engine.LineCountForHooks);
        }

        [Test]
        public void ANameNoSourceHasKeepsItsPlaceAndShowsNothing()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            float bare = Make("ab").GetMaxContentWidth();
            LetteraWorldText text = Inline("a<pic=nope>b");
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            ReadOnlySpanEquals(text, 'a', 0xFFFC, 'b');
            Assert.AreEqual(bare, text.GetMaxContentWidth(), 0.5f);
            Assert.AreEqual(2 * 4, text.Engine.VertexCount);
        }

        [Test]
        public void CaretStopsAreOneOnEachSideAndBackspaceTakesTheTag()
        {
            LetteraEditable field = Field("");
            LetteraWorldText text = field.GetComponent<LetteraWorldText>();
            Inline("", 1000, null, text);
            field.SetSource("a<pic=x>b");
            field.Selectable.PlaceCaret(2);
            Press(field, LetteraKeyCode.Backspace);
            Assert.AreEqual("ab", field.Source);
            Press(field, LetteraKeyCode.Z, Ctrl);
            Assert.AreEqual("a<pic=x>b", field.Source);
        }

        [Test]
        public void PlainCopyGivesItsWordsOrNothing()
        {
            LetteraSelectable reader = Reader("");
            LetteraWorldText text = reader.GetComponent<LetteraWorldText>();
            Inline("a<pic=x>b", 1000, null, text);
            reader.Select(0, 3);
            reader.Copy();
            Assert.AreEqual("astarb", Clip.Text(LetteraClipFormat.PlainText));
        }

        [Test]
        public void ATypedPlaceholderIsDropped()
        {
            LetteraEditable field = Field("");
            Type(field, "a￼b");
            Assert.AreEqual("ab", field.Source);
        }

        [Test]
        public void ASpriteMovesWithAMotionAndNoRebuild()
        {
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(new WaveMotion(), "go2"));
            Inline("<go2>a<pic=x>b</go2>", 1000, null, text);
            int rebuilds = text.RebuildCount;
            Vector3 rest = text.Engine.RestVertex(4);
            Clock(0.13f);
            Clock(0.13f);
            Assert.AreEqual(rebuilds, text.RebuildCount);
            Assert.AreNotEqual(rest.y, text.Engine.Vertices[8].y);
        }

        [Test]
        public void ATintFollowsTheTextWhenAskedAndTheSpritesOwnOtherwise()
        {
            LetteraWorldText own = Inline("<color=#ff0000>a<pic=x>b</color>");
            LetteraWorldText inherit = Inline("<color=#ff0000>a<pic=x,,,,,,,,i>b</color>");
            Assert.AreEqual(255, own.Engine.Colors[8].g);
            Assert.AreEqual(0, inherit.Engine.Colors[8].g);
        }

        [Test]
        public void ArabicAroundAnItemTakesIsolatedForms()
        {
            LetteraWorldText label = Make("", 1000, 500, Arabic);
            Inline("س<pic=x>س", 1000, null, label);
            LetteraWorldText alone = Make("س س", 1000, 500, Arabic);
            Assert.AreEqual(alone.Engine.ShapedGlyphIdForTests(0), label.Engine.ShapedGlyphIdForTests(0));
        }

        [Test]
        public void AnObjectStaysOutOfTheMeshAndSpawnsWhenTicked()
        {
            var prefab = new GameObject("Inline Prefab", typeof(RectTransform));
            LetteraWorldText text = Make("");
            var effect = new InlineEffect();
            effect.Items.Add(new LetteraInlineEntry { Key = "o", Source = prefab, Size = 1 });
            text.Styles.Add(LetteraStyle.ForMatcher(new InlineMediaMatcher("o2"), effect));
            text.enabled = false;
            text.enabled = true;
            text.Text = "a<o2=o>b";
            text.ForceRebuild();
            Clock(0.01f);
            Assert.AreEqual(1, LetteraInlineObjects.ActiveCount(text.Engine));
            Transform root = text.transform.Find("Lettera Inline");
            Assert.IsNotNull(root);
            Assert.AreEqual(HideFlags.DontSave, root.gameObject.hideFlags);
            Assert.AreEqual(2 * 4, text.Engine.VertexCount);

            text.Text = "ab";
            text.ForceRebuild();
            Clock(0.01f);
            Assert.AreEqual(0, LetteraInlineObjects.ActiveCount(text.Engine));
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void AnAncestorCannotBeAdopted()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var holder = new GameObject("Holder", typeof(RectTransform));
            LetteraWorldText text = Make("");
            text.transform.SetParent(holder.transform, false);
            var effect = new InlineEffect();
            effect.Items.Add(new LetteraInlineEntry { Key = "o", Source = holder, Mode = LetteraInlineMode.Adopt });
            text.Styles.Add(LetteraStyle.ForMatcher(new InlineMediaMatcher("o3"), effect));
            text.enabled = false;
            text.enabled = true;
            text.Text = "a<o3=o>b";
            text.ForceRebuild();
            Clock(0.01f);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(0, LetteraInlineObjects.ActiveCount(text.Engine));
            text.transform.SetParent(null, false);
            Object.DestroyImmediate(holder);
        }

        // ------------------------------------------------------------- random

        [Test]
        public void TenThousandRandomEditsThrowNothingAndTheHubEmpties()
        {
            string[] tags = { "wave", "shake", "reveal", "link=https://a.io", "spoiler", "glitch", "b", "scramble" };
            var random = new System.Random(7);
            int hub = LetteraMotionHub.Count;
            LetteraEditable field = Field("");
            LetteraWorldText text = field.GetComponent<LetteraWorldText>();
            var live = new HashSet<int>();
            var seen = new HashSet<int>();
            var body = new System.Text.StringBuilder();
            for (int i = 0; i < 10000; i++)
            {
                switch (random.Next(5))
                {
                    case 0:
                        body.Clear();
                        for (int k = random.Next(5); k > 0; k--)
                        {
                            string tag = tags[random.Next(tags.Length)];
                            string name = tag.Split('=')[0];
                            body.Append("ab").Append('<').Append(tag).Append('>').Append("xy").Append("</").Append(name).Append('>');
                        }
                        field.SetSource(body.ToString());
                        break;
                    case 1:
                        Type(field, "q");
                        break;
                    case 2:
                        Press(field, LetteraKeyCode.Backspace);
                        break;
                    case 3:
                        field.Selectable.PlaceCaret(random.Next(field.Length + 1));
                        break;
                    default:
                        Clock((float)random.NextDouble() * 0.05f);
                        break;
                }
                if (i % 50 != 0) continue;
                text.ForceRebuild();
                var now = new HashSet<int>();
                foreach (TextEngine.ActiveRange range in text.Engine.ActiveRanges)
                {
                    Assert.IsTrue(now.Add(range.Id));
                    if (!live.Contains(range.Id)) Assert.IsTrue(seen.Add(range.Id), "an id came back");
                }
                live = now;
            }
            Object.DestroyImmediate(text.gameObject);
            Clock(0.01f);
            Assert.AreEqual(hub, LetteraMotionHub.Count);
        }
    }
}
