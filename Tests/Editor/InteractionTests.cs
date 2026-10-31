using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Ranges that take input: identity, the router, links, spoilers, rules and what a screen reader sees.</summary>
    public class InteractionTests : EditingFixture
    {
        sealed class FakeOpener : ILetteraLinkOpener
        {
            public readonly List<string> Opened = new List<string>();
            public void Open(string url) => Opened.Add(url);
        }

        FakeOpener m_Opener;

        [SetUp]
        public void Prepare()
        {
            m_Opener = new FakeOpener();
            LetteraLinks.Opener = m_Opener;
            LetteraCursors.Backend = new FakeCursor();
            LetteraSelectable.ClassifyOverride = _ => LetteraPointerKind.Mouse;
        }

        [TearDown]
        public void Clean()
        {
            LetteraLinks.Opener = null;
            LetteraCursors.Backend = null;
            LetteraCursors.Set(LetteraRangeCursor.Default);
            LetteraSelectable.s_LinkModifierOverride = null;
        }

        sealed class FakeCursor : ILetteraCursorBackend
        {
            public LetteraRangeCursor Last;
            public void Set(LetteraRangeCursor cursor) => Last = cursor;
        }

        static LetteraRangeRouter RouterOf(LetteraWorldText text)
        {
            text.ForceRebuild();
            return LetteraRangeRouter.Of((Component)text);
        }

        static Vector2 PointIn(LetteraWorldText text, int start, int end)
        {
            var rects = new List<Rect>();
            text.GetRangeRects(start, end, rects);
            Assert.Greater(rects.Count, 0);
            return rects[0].center;
        }

        static IReadOnlyList<TextEngine.ActiveRange> Active(LetteraWorldText text)
        {
            text.ForceRebuild();
            return text.Engine.ActiveRanges;
        }

        static PointerEventData Mouse(Vector2 point, PointerEventData.InputButton button = PointerEventData.InputButton.Left) => Pointer(point, button, -1);

        static void Click(LetteraRangeRouter router, Vector2 point)
        {
            router.Press(Mouse(point));
            router.Release(Mouse(point));
        }

        static void Clock(float seconds) => LetteraMotionHub.Tick(seconds, seconds);

        // ------------------------------------------------------------- identity

        [Test]
        public void AnEditBeforeARangeKeepsItsIdAndOneInsideRetiresIt()
        {
            LetteraWorldText text = Make("a <link=x>bcd</link> c <link=y>d</link>");
            int first = Active(text)[0].Id, second = Active(text)[1].Id;
            Assert.AreNotEqual(first, second);

            text.Text = "za <link=x>bcd</link> c <link=y>d</link>";
            Assert.AreEqual(first, Active(text)[0].Id);
            Assert.AreEqual(second, Active(text)[1].Id);

            text.Text = "za <link=x>bQcd</link> c <link=y>d</link>";
            Assert.AreNotEqual(first, Active(text)[0].Id);
            Assert.AreEqual(second, Active(text)[1].Id);
        }

        [Test]
        public void TheSameCharactersKeepEveryId()
        {
            LetteraWorldText text = Make("<link=x>b</link>");
            int id = Active(text)[0].Id;
            text.Text = "<link=x>b</link> ";
            text.Text = "<link=x>b</link>";
            Assert.AreEqual(id, Active(text)[0].Id);
        }

        [Test]
        public void AnIdIsNeverUsedTwiceAcrossRandomEdits()
        {
            var random = new System.Random(4);
            var seen = new HashSet<int>();
            var live = new HashSet<int>();
            LetteraWorldText text = Make("<link=a>x</link> y");
            for (int i = 0; i < 400; i++)
            {
                string body = "";
                int count = random.Next(4);
                for (int k = 0; k < count; k++) body += new string('a', random.Next(3)) + "<link=" + random.Next(3) + ">" + new string('b', random.Next(1, 3)) + "</link>";
                text.Text = body;
                var now = new HashSet<int>();
                foreach (TextEngine.ActiveRange range in Active(text))
                {
                    Assert.IsTrue(now.Add(range.Id), "two live ranges share an id");
                    if (!live.Contains(range.Id)) Assert.IsTrue(seen.Add(range.Id), "an id came back");
                }
                live = now;
            }
        }

        // ------------------------------------------------------------- router

        [Test]
        public void APlainTextHasNoRangesAndNoInput()
        {
            LetteraWorldText text = Make("Plain");
            CollectionAssert.IsEmpty(Active(text));
            Assert.IsNull(text.GetComponent<LetteraRangeInput>());
        }

        [Test]
        public void AClickOnALinkActivatesItAndOpensIt()
        {
            LetteraWorldText text = Make("go <link=https://a.io>here</link> now");
            LetteraRangeRouter router = RouterOf(text);
            string followed = null;
            ((LinkEffect)text.Engine.ActiveRanges[0].Effect).Followed += url => followed = url;
            Click(router, PointIn(text, 3, 7));
            Assert.AreEqual("https://a.io", followed);
            CollectionAssert.AreEqual(new[] { "https://a.io" }, m_Opener.Opened);
        }

        [Test]
        public void MovingOffBeforeReleaseActivatesNothingAndComingBackDoes()
        {
            LetteraWorldText text = Make("go <link=https://a.io>here</link> and more text here");
            LetteraRangeRouter router = RouterOf(text);
            Vector2 on = PointIn(text, 3, 7), off = PointIn(text, 12, 14);
            router.Press(Mouse(on));
            router.Release(Mouse(off));
            CollectionAssert.IsEmpty(m_Opener.Opened);
            router.Press(Mouse(on));
            router.Release(Mouse(on));
            Assert.AreEqual(1, m_Opener.Opened.Count);
        }

        [Test]
        public void ASecondFingerEndsThePressWithNoActivation()
        {
            LetteraWorldText text = Make("<link=https://a.io>here</link>");
            LetteraRangeRouter router = RouterOf(text);
            bool cancelled = false;
            router.All.Released += e => cancelled = e.Cancelled;
            Vector2 on = PointIn(text, 0, 4);
            router.Press(Pointer(on, PointerEventData.InputButton.Left, 1));
            router.Press(Pointer(on, PointerEventData.InputButton.Left, 2));
            router.Release(Pointer(on, PointerEventData.InputButton.Left, 1));
            Assert.IsTrue(cancelled);
            CollectionAssert.IsEmpty(m_Opener.Opened);
        }

        [Test]
        public void ANestedRangeIsTheTargetAndTheOuterHearsOnBothSides()
        {
            LetteraWorldText text = Make("<link=a>x<link=b>y</link>z</link>");
            LetteraRangeRouter router = RouterOf(text);
            var order = new List<string>();
            // Both tags share one effect, so what is heard here is its events along the nest: outer, target, outer.
            text.Engine.ActiveRanges[0].Effect.Events.Activated += e => order.Add(">" + e.Primary);
            Click(router, PointIn(text, 1, 2));
            Assert.AreEqual(3, order.Count);
            Assert.IsTrue(order.TrueForAll(line => line.EndsWith(">b")));
        }

        [Test]
        public void PriorityBeatsLength()
        {
            var outer = new ActionEffect { Priority = 5 };
            var inner = new ActionEffect();
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(outer, "outer"));
            text.Styles.Add(LetteraStyle.ForTag(inner, "inner"));
            text.Text = "<outer=o>x<inner=i>y</inner>z</outer>";
            LetteraRangeRouter router = RouterOf(text);
            string target = null;
            router.All.Activated += e => target = e.Primary;
            Click(router, PointIn(text, 1, 2));
            Assert.AreEqual("o", target);
        }

        [Test]
        public void StopInCaptureEndsDelivery()
        {
            var outer = new ActionEffect();
            var inner = new ActionEffect();
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(outer, "outer"));
            text.Styles.Add(LetteraStyle.ForTag(inner, "inner"));
            text.Text = "<outer=o>x<inner=i>y</inner>z</outer>";
            LetteraRangeRouter router = RouterOf(text);
            bool reached = false;
            outer.Events.Activated += e => e.Stop();
            inner.Events.Activated += e => reached = true;
            Click(router, PointIn(text, 1, 2));
            Assert.IsFalse(reached);
        }

        [Test]
        public void HoverFollowsAMovedRangeAndAReparseKeepsIt()
        {
            LetteraWorldText text = Make("<link=a>here</link>");
            LetteraRangeRouter router = RouterOf(text);
            int entered = 0, exited = 0;
            router.All.Entered += _ => entered++;
            router.All.Exited += _ => exited++;
            Vector2 on = PointIn(text, 0, 4);
            router.PointerInside(Mouse(on), true);
            Assert.AreEqual(1, entered);
            Assert.AreEqual(LetteraRangeState.Hovered, router.GetState(router.Hovered));

            text.SetDirty(DirtyLevel.Parse);
            text.ForceRebuild();
            Clock(0.01f);
            Assert.AreEqual(1, entered);
            Assert.AreEqual(0, exited);

            text.transform.position += new Vector3(5000, 0, 0);
            Clock(0.01f);
            Assert.AreEqual(1, exited);
            Assert.AreEqual(0, router.Hovered);
            router.PointerInside(Mouse(on), false);
        }

        [Test]
        public void HitPaddingGrowsTheTarget()
        {
            var effect = new ActionEffect { HitPadding = 0.5f };
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(effect, "act"));
            text.Text = "<act=o>here</act>";
            LetteraRangeRouter router = RouterOf(text);
            var rects = new List<Rect>();
            text.GetRangeRects(0, 4, rects);
            Assert.AreNotEqual(0, router.HitTest(new Vector2(rects[0].center.x, rects[0].yMax + 5), null));
            effect.HitPadding = 0;
            Assert.AreEqual(0, router.HitTest(new Vector2(rects[0].center.x, rects[0].yMax + 5), null));
        }

        [Test]
        public void ADisabledRangeIsNotATarget()
        {
            LetteraWorldText text = Make("<link=https://a.io>here</link>");
            LetteraRangeRouter router = RouterOf(text);
            int id = text.Engine.ActiveRanges[0].Id;
            router.SetDisabled(id, true);
            Click(router, PointIn(text, 0, 4));
            CollectionAssert.IsEmpty(m_Opener.Opened);
            Assert.AreEqual(LetteraRangeState.Disabled, router.GetState(id));
        }

        [Test]
        public void TheKeyboardWalksRangesInOrder()
        {
            var effect = new ActionEffect { Focusable = false };
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(effect, "skip"));
            text.Text = "<link=https://1.io>a</link> <skip=x>b</skip> <link=https://2.io>c</link>";
            LetteraRangeRouter router = RouterOf(text);
            Assert.IsTrue(router.StepFocus(true));
            Assert.IsTrue(router.ActivateFocused());
            Assert.IsTrue(router.StepFocus(true));
            Assert.IsTrue(router.ActivateFocused());
            CollectionAssert.AreEqual(new[] { "https://1.io", "https://2.io" }, m_Opener.Opened);
            Assert.IsFalse(router.StepFocus(true), "past the end leaves");
            Assert.AreEqual(0, router.Focused);
        }

        // ------------------------------------------------------------- links

        [TestCase("javascript:alert(1)")]
        [TestCase("file:///etc/passwd")]
        [TestCase("data:text/html,x")]
        [TestCase("nothing")]
        public void WhatTheListDoesNotHoldStaysShut(string url)
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(LetteraLinks.Open(url));
            CollectionAssert.IsEmpty(m_Opener.Opened);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void ABareAddressGainsItsScheme()
        {
            Assert.IsTrue(LetteraLinks.Open("www.a.io"));
            CollectionAssert.AreEqual(new[] { "https://www.a.io" }, m_Opener.Opened);
        }

        [Test]
        public void OpenOnActivateOffAndPreventDefaultLeaveItShut()
        {
            LetteraWorldText text = Make("<link=https://a.io>here</link>");
            LetteraRangeRouter router = RouterOf(text);
            var link = (LinkEffect)text.Engine.ActiveRanges[0].Effect;
            link.OpenOnActivate = false;
            int heard = 0;
            router.All.Activated += _ => heard++;
            Click(router, PointIn(text, 0, 4));
            Assert.AreEqual(1, heard);
            CollectionAssert.IsEmpty(m_Opener.Opened);
            link.OpenOnActivate = true;

            System.Action<LetteraRangeEvent> stop = e => e.PreventDefault();
            link.Events.Activated += stop;
            Click(router, PointIn(text, 0, 4));
            link.Events.Activated -= stop;
            Assert.AreEqual(2, heard, "subscribers still hear it");
            CollectionAssert.IsEmpty(m_Opener.Opened);
        }

        [Test]
        public void MarkdownAndBareUrlsActLikeTheTag()
        {
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownLinkMatcher(), new LinkEffect()));
            text.Text = "[x](https://a.io)";
            LetteraRangeRouter router = RouterOf(text);
            Click(router, PointIn(text, 0, 1));
            CollectionAssert.AreEqual(new[] { "https://a.io" }, m_Opener.Opened);
        }

        // ------------------------------------------------------------- selection arbitration

        [Test]
        public void OnReadOnlyTextThePressGoesToTheRangeAndLeavesTheSelection()
        {
            LetteraSelectable reader = Reader("go <link=https://a.io>here</link> now");
            LetteraWorldText text = reader.GetComponent<LetteraWorldText>();
            text.ForceRebuild();
            Vector2 on = PointIn(text, 3, 7);
            reader.Select(0, 2);
            reader.OnPointerDown(Mouse(on));
            Assert.IsTrue(reader.Claimed);
            reader.OnPointerUp(Mouse(on));
            Assert.IsFalse(reader.Claimed);
            Assert.AreEqual(1, m_Opener.Opened.Count);
            Assert.AreEqual(new LetteraSelectionRange(0, 2), reader.Selection);
        }

        [Test]
        public void ADragFromARangeSelectsFromWhereItBegan()
        {
            LetteraSelectable reader = Reader("go <link=https://a.io>here</link> and on");
            LetteraWorldText text = reader.GetComponent<LetteraWorldText>();
            text.ForceRebuild();
            Vector2 on = PointIn(text, 3, 7), far = PointIn(text, 10, 12);
            reader.OnPointerDown(Mouse(on));
            reader.OnDrag(Mouse(far));
            Assert.IsFalse(reader.Claimed);
            reader.OnPointerUp(Mouse(far));
            CollectionAssert.IsEmpty(m_Opener.Opened);
            Assert.IsFalse(reader.Selection.IsCaret);
        }

        [Test]
        public void ASecondClickOnTheSameSpotSelectsTheWord()
        {
            LetteraSelectable reader = Reader("go <link=https://a.io>here</link> now");
            LetteraWorldText text = reader.GetComponent<LetteraWorldText>();
            text.ForceRebuild();
            Vector2 on = PointIn(text, 3, 7);
            reader.OnPointerDown(Mouse(on));
            reader.OnPointerUp(Mouse(on));
            Assert.AreEqual(1, m_Opener.Opened.Count);
            reader.OnPointerDown(Mouse(on));
            reader.OnPointerUp(Mouse(on));
            Assert.AreEqual(1, m_Opener.Opened.Count);
            Assert.AreEqual(new LetteraSelectionRange(3, 7).Start, reader.Selection.Start);
            Assert.AreEqual(7, reader.Selection.End);
        }

        [Test]
        public void InAFieldAPlainPressPlacesTheCaretAndTheModifierFollows()
        {
            LetteraEditable field = Field("go <link=https://a.io>here</link> now");
            LetteraWorldText text = field.GetComponent<LetteraWorldText>();
            text.ForceRebuild();
            Vector2 on = PointIn(text, 3, 7);
            LetteraSelectable.s_LinkModifierOverride = false;
            field.Selectable.OnPointerDown(Mouse(on));
            field.Selectable.OnPointerUp(Mouse(on));
            CollectionAssert.IsEmpty(m_Opener.Opened);
            Now += 5;
            LetteraSelectable.s_LinkModifierOverride = true;
            field.Selectable.OnPointerDown(Mouse(on));
            Assert.IsTrue(field.Selectable.Claimed);
            field.Selectable.OnPointerUp(Mouse(on));
            Assert.AreEqual(1, m_Opener.Opened.Count);
        }

        [Test]
        public void InAFieldCtrlEnterAtACaretInALinkFollowsItAndEnterTypes()
        {
            LetteraEditable field = Field("go <link=https://a.io>here</link> now");
            field.GetComponent<LetteraWorldText>().ForceRebuild();
            field.Selectable.PlaceCaret(5);
            field.Selectable.HandleKey(LetteraKeyCode.Enter, Ctrl, false);
            Assert.AreEqual(1, m_Opener.Opened.Count);
        }

        [Test]
        public void TabOnReadOnlyTextStepsAndEnterActivates()
        {
            LetteraSelectable reader = Reader("<link=https://a.io>a</link>");
            reader.GetComponent<LetteraWorldText>().ForceRebuild();
            Assert.IsTrue(reader.HandleKey(LetteraKeyCode.Tab, LetteraKeyModifiers.None, false));
            Assert.IsTrue(reader.HandleKey(LetteraKeyCode.Enter, LetteraKeyModifiers.None, false));
            Assert.AreEqual(1, m_Opener.Opened.Count);
            Assert.IsTrue(reader.HandleKey(LetteraKeyCode.Escape, LetteraKeyModifiers.None, false));
        }

        // ------------------------------------------------------------- spoilers

        static float AlphaOfGlyph(LetteraWorldText text, int quad) => text.Engine.Colors[quad * 4].a;

        [Test]
        public void ACoveredGlyphDrawsNothingAndAClickFadesItIn()
        {
            LetteraWorldText text = Make("a <spoiler>bc</spoiler>");
            LetteraRangeRouter router = RouterOf(text);
            int rebuilds = text.RebuildCount;
            for (int q = 0; q < text.Engine.VertexCount / 4; q++)
            {
                int owner = text.Engine.OwnerOfQuad(q);
                if (owner < 0) continue;
                int cluster = text.Engine.ClusterOfGlyph(owner);
                if (cluster >= 2) Assert.AreEqual(0f, AlphaOfGlyph(text, q), "a covered glyph is drawn");
            }
            int id = text.Engine.ActiveRanges[0].Id;
            var effect = (SpoilerEffect)text.Engine.ActiveRanges[0].Effect;
            int changes = 0;
            effect.CoverChanged += (_, __) => changes++;
            Click(router, PointIn(text, 2, 4));
            Assert.IsTrue(router.IsUncovered(id));
            Assert.AreEqual(1, changes);
            Clock(effect.FadeSeconds + 0.01f);
            Clock(0.01f);
            Assert.AreEqual(rebuilds, text.RebuildCount, "the fade runs no rebuild");
            int quads = text.Engine.VertexCount / 4;
            for (int q = 0; q < quads; q++)
            {
                int owner = text.Engine.OwnerOfQuad(q);
                if (owner >= 0 && text.Engine.ClusterOfGlyph(owner) >= 2) Assert.Greater(AlphaOfGlyph(text, q), 200f);
            }
            Click(router, PointIn(text, 2, 4));
            Assert.IsFalse(router.IsUncovered(id));
            Assert.AreEqual(2, changes);
        }

        [Test]
        public void AnEditBeforeASpoilerKeepsItOpenAndOneInsideCoversItAgain()
        {
            LetteraWorldText text = Make("a <spoiler>bc</spoiler>");
            LetteraRangeRouter router = RouterOf(text);
            router.SetUncovered(text.Engine.ActiveRanges[0].Id, true);
            text.Text = "za <spoiler>bc</spoiler>";
            Assert.IsTrue(router.IsUncovered(Active(text)[0].Id));
            text.Text = "za <spoiler>bQc</spoiler>";
            Assert.IsFalse(router.IsUncovered(Active(text)[0].Id));
        }

        [Test]
        public void PlainCopyMasksWhatIsCovered()
        {
            LetteraSelectable reader = Reader("x <spoiler>abc</spoiler> y");
            reader.GetComponent<LetteraWorldText>().ForceRebuild();
            reader.Select(0, reader.GetComponent<LetteraWorldText>().Engine.ShownCodepoints.Length);
            reader.Copy();
            Assert.AreEqual("x ███ y", Clip.Text(LetteraClipFormat.PlainText));
            Assert.IsNull(Clip.Text(LetteraClipFormat.Lettera), "the rich formats would carry the source");
        }

        [Test]
        public void CopyPolicyKeepLetsTheTextOut()
        {
            var effect = new SpoilerEffect { CopyPolicy = LetteraSpoilerCopy.Keep };
            LetteraSelectable reader = Reader("");
            LetteraWorldText text = reader.GetComponent<LetteraWorldText>();
            text.Styles.Add(LetteraStyle.ForTag(effect, "hide"));
            text.Text = "x <hide>abc</hide>";
            text.ForceRebuild();
            reader.Select(0, 5);
            reader.Copy();
            Assert.AreEqual("x abc", Clip.Text(LetteraClipFormat.PlainText));
        }

        // ------------------------------------------------------------- rules

        static LetteraWorldText TintedLink(out RangeTintEffect tint, out LinkEffect link, LetteraEnvelopeKind envelope = LetteraEnvelopeKind.Transition, LetteraWorldText text = null)
        {
            tint = new RangeTintEffect { Color = new Color(1, 0, 0, 1) };
            link = new LinkEffect();
            link.Rules.Add(new LetteraRangeRule
            {
                When = new SignalIs(LetteraSignals.Hovered),
                Envelope = envelope,
                Enter = 0.2f,
                Exit = 0.2f,
                Ease = LetteraEase.Linear,
                Changes = { new LetteraRangeChange { Target = tint, Parameter = "Weight", ToNumber = 1 } },
            });
            text.Styles.Add(LetteraStyle.ForTag(new CompositeEffect(link, tint), "hot"));
            return text;
        }

        [Test]
        public void AHoverRuleMovesThroughItsEnvelopeAndLeavesNothingAfter()
        {
            LetteraWorldText text = TintedLink(out RangeTintEffect tint, out _, text: Make(""));
            text.Text = "<hot>here</hot>";
            LetteraRangeRouter router = RouterOf(text);
            int rebuilds = text.RebuildCount, parse = text.Engine.ParseCount;
            byte Green() => text.Engine.Colors[0].g;
            byte rest = Green();
            Vector2 on = PointIn(text, 0, 4);
            router.PointerInside(Mouse(on), true);
            Clock(0.1f);
            Clock(0.0001f);
            byte midway = Green();
            Assert.Less(midway, rest);
            Assert.Greater(midway, (byte)10);
            Clock(0.2f);
            Clock(0.0001f);
            Assert.Less(Green(), 10, "the tint arrived");
            router.PointerInside(Mouse(on), false);
            Clock(0.3f);
            Clock(0.0001f);
            Clock(0.0001f);
            Assert.AreEqual(rest, Green(), "exit returns to the cascade");
            Assert.AreEqual(0, text.Engine.HoldCount, "no hold stays when it settled");
            Assert.AreEqual(rebuilds, text.RebuildCount);
            Assert.AreEqual(parse, text.Engine.ParseCount);
            Assert.AreEqual(0f, tint.Weight, "the field never changed");
        }

        [Test]
        public void AnInstantRuleAppliesInTheSameFrame()
        {
            LetteraWorldText text = TintedLink(out _, out _, LetteraEnvelopeKind.Instant, Make(""));
            text.Text = "<hot>here</hot>";
            LetteraRangeRouter router = RouterOf(text);
            router.PointerInside(Mouse(PointIn(text, 0, 4)), true);
            Clock(0.0001f);
            Assert.Less(text.Engine.Colors[0].g, 10);
            router.PointerInside(Mouse(PointIn(text, 0, 4)), false);
        }

        [Test]
        public void ReducedMotionJumpsATransition()
        {
            LetteraAccessibility.ReducedMotion = true;
            LetteraWorldText text = TintedLink(out _, out _, text: Make(""));
            text.Text = "<hot>here</hot>";
            LetteraRangeRouter router = RouterOf(text);
            router.PointerInside(Mouse(PointIn(text, 0, 4)), true);
            Clock(0.0001f);
            Assert.Less(text.Engine.Colors[0].g, 10);
            router.PointerInside(Mouse(PointIn(text, 0, 4)), false);
        }

        [Test]
        public void ConditionsFollowTheirTruthTables()
        {
            LetteraWorldText text = Make("<link=a>x</link>");
            LetteraRangeRouter router = RouterOf(text);
            int id = text.Engine.ActiveRanges[0].Id;
            router.Publish(id, "my.flag", 1);
            var flag = new SignalIs("my.flag");
            var off = new SignalIs(LetteraSignals.Disabled);
            Assert.IsTrue(flag.Test(router, id));
            Assert.IsFalse(off.Test(router, id));
            Assert.IsFalse(new AllOf { Items = { flag, off } }.Test(router, id));
            Assert.IsTrue(new AnyOf { Items = { flag, off } }.Test(router, id));
            Assert.IsFalse(new NotOf { Item = flag }.Test(router, id));
            Assert.IsTrue(new ScalarAtLeast { Signal = "my.flag", Threshold = 0.5f }.Test(router, id));
            router.Publish(id, "my.flag", 0.25f);
            Assert.IsFalse(new ScalarAtLeast { Signal = "my.flag", Threshold = 0.5f }.Test(router, id));
        }

        [Test]
        public void ARetiredRangeLetsGoOfItsHolds()
        {
            LetteraWorldText text = TintedLink(out _, out _, LetteraEnvelopeKind.Instant, Make(""));
            text.Text = "<hot>here</hot>";
            LetteraRangeRouter router = RouterOf(text);
            router.PointerInside(Mouse(PointIn(text, 0, 4)), true);
            Assert.AreEqual(1, text.Engine.HoldCount);
            text.Text = "plain";
            text.ForceRebuild();
            Assert.AreEqual(0, text.Engine.HoldCount);
            router.PointerInside(Mouse(Vector2.zero), false);
        }

        // ------------------------------------------------------------- semantics

        [Test]
        public void NothingIsDescribedUntilAnEffectSaysSo()
        {
            LetteraWorldText text = Make("plain <link=a>x</link>");
            text.ForceRebuild();
            Assert.IsFalse(LetteraSemanticTree.TryGet(text, out _));
            CollectionAssert.IsEmpty(LetteraSemanticTree.Of(text).Nodes);
        }

        [Test]
        public void AHeadingGivesOneNodeWithItsWords()
        {
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(new SemanticEffect { Role = LetteraSemanticRole.Heading, Level = 2 }, "h"));
            text.Text = "<h>Title</h> body";
            text.ForceRebuild();
            LetteraSemanticTree tree = LetteraSemanticTree.Of(text);
            Assert.AreEqual(1, tree.Nodes.Count);
            Assert.AreEqual("Title", tree.Nodes[0].Label);
            Assert.AreEqual(LetteraSemanticRole.Heading, tree.Nodes[0].Role);
            Assert.AreEqual(2, tree.Nodes[0].Level);
            Assert.Greater(tree.Nodes[0].Segments.Count, 0);
        }

        [Test]
        public void ADescribedLinkActsAndHoverIsNoRebuild()
        {
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(new CompositeEffect(new LinkEffect(), new SemanticEffect()), "a2"));
            text.Text = "<a2=https://a.io>here</a2>";
            LetteraRangeRouter router = RouterOf(text);
            LetteraSemanticTree tree = LetteraSemanticTree.Of(text);
            LetteraSemanticNode node = tree.Nodes[0];
            Assert.AreEqual(LetteraSemanticRole.Link, node.Role);
            int changes = 0, commits = 0;
            tree.Changed += (_, __) => changes++;
            tree.Committed += () => commits++;
            router.PointerInside(Mouse(PointIn(text, 0, 4)), true);
            Assert.AreEqual(1, changes);
            Assert.AreEqual(0, commits);
            Assert.IsTrue((node.States & LetteraSemanticStates.Hovered) != 0);
            router.PointerInside(Mouse(PointIn(text, 0, 4)), false);
            Assert.IsTrue(tree.RunAction(node.Id, LetteraSemanticActions.Activate));
            Assert.AreEqual(1, m_Opener.Opened.Count);
            Assert.IsFalse(tree.RunAction(node.Id, LetteraSemanticActions.Dismiss));
        }

        [Test]
        public void ACoveredSpanSaysItIsHidden()
        {
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(new CompositeEffect(new SpoilerEffect(), new SemanticEffect()), "s2"));
            text.Text = "<s2>secret</s2>";
            LetteraRangeRouter router = RouterOf(text);
            LetteraSemanticTree tree = LetteraSemanticTree.Of(text);
            Assert.AreEqual("Hidden text", tree.Nodes[0].Label);
            Assert.IsTrue((tree.Nodes[0].States & LetteraSemanticStates.Concealed) != 0);
            router.SetUncovered(text.Engine.ActiveRanges[0].Id, true);
            tree.Refresh();
            Assert.AreEqual("secret", tree.Nodes[0].Label);
        }

        // ------------------------------------------------------------- inspectors

        static void AssertCustom(Object target)
        {
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            UnityEngine.UIElements.VisualElement root = editor.CreateInspectorGUI();
            Assert.IsNotNull(root, target.GetType().Name);
            Assert.IsTrue(root.ClassListContains("lt-root"), target.GetType().Name);
            Assert.AreEqual(0, UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.IMGUIContainer>(root).ToList().Count, target.GetType().Name);
            Object.DestroyImmediate(editor);
        }

        [Test]
        public void TheNewInspectorsAreAllOurOwn()
        {
            LetteraWorldText text = Make("<wave>x</wave>");
            AssertCustom(text.gameObject.AddComponent<LetteraSequencer>());
            AssertCustom(text.gameObject.AddComponent<LetteraParamLink>());
            var topic = ScriptableObject.CreateInstance<LetteraRangeTopic>();
            var inline = ScriptableObject.CreateInstance<LetteraInlineLibrary>();
            var entrances = ScriptableObject.CreateInstance<LetteraEntranceLibrary>();
            AssertCustom(topic);
            AssertCustom(inline);
            AssertCustom(entrances);
            Object.DestroyImmediate(topic);
            Object.DestroyImmediate(inline);
            Object.DestroyImmediate(entrances);
        }

        [Test]
        public void ThePickerFindsNewEffectsByWhatPeopleCallThem()
        {
            bool Finds(string word, System.Type effect) => FirstSymbol.Lettera.Editor.LetteraStyleTemplates.All.Any(t => t.Keywords != null && t.Keywords.Contains(word) && t.Effect != null && t.Effect().GetType() == effect);
            Assert.IsTrue(Finds("typewriter", typeof(RevealEffect)));
            Assert.IsTrue(Finds("ticker", typeof(MarqueeEffect)));
            Assert.IsTrue(Finds("<sprite>", typeof(InlineEffect)));
            Assert.IsFalse(FirstSymbol.Lettera.Editor.LetteraStyleTemplates.All.Any(t => t.Kind == "Custom" && t.Effect != null && t.Effect() is WaveMotion), "a built-in motion is listed as custom");
        }

        // ------------------------------------------------------------- stages

        sealed class HookEffect : LetteraEffect
        {
            public LetteraStages Wanted;
            public System.Func<bool> Throw = () => false;
            public override LetteraStages Stages => Wanted;
            public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) { }

            public override void OnAnalyzed(ref LetteraAnalysisView view) => view.Hide(view.Start + 1, view.Start + 2);
            public override void OnShaped(ref LetteraShapedView view) => view.AddExtra(view.Start, 1);
            public override void OnLinesBroken(ref LetteraLinesView view) => view.ForceBreakBefore(view.Start + 1);
            public override void OnPlaced(ref LetteraPlacedView view) => view.Move(0, 2, 0);
            public override void OnMesh(ref LetteraMeshView view) => view.AddSolid(0, 0, 10, 10, new Color32(1, 2, 3, 255));
            public override void OnPose(ref LetteraPoseView view)
            {
                if (Throw()) throw new System.InvalidOperationException("from a hook");
                for (int g = 0; g < view.Count; g++)
                {
                    var pose = LetteraGlyphPose.Identity;
                    pose.Move(0, 3);
                    view.Compose(g, pose);
                }
            }
        }

        static LetteraWorldText Hooked(LetteraStages stages, string body, LetteraWorldText text, out HookEffect effect)
        {
            effect = new HookEffect { Wanted = stages };
            text.Styles.Add(LetteraStyle.ForTag(effect, "hook"));
            text.Text = "<hook>" + body + "</hook>";
            text.ForceRebuild();
            return text;
        }

        [Test]
        public void AnAnalysisHookHidesAClusterAndLeavesIndices()
        {
            float whole = Make("abc").GetMaxContentWidth();
            LetteraWorldText text = Hooked(LetteraStages.Analyzed, "abc", Make(""), out _);
            Assert.Less(text.GetMaxContentWidth(), whole);
            Assert.AreEqual(3, text.Engine.ShownCodepoints.Length);
        }

        [Test]
        public void AShapedHookAddsWidth()
        {
            float whole = Make("abc").GetMaxContentWidth();
            LetteraWorldText text = Hooked(LetteraStages.Shaped, "abc", Make(""), out _);
            Assert.AreEqual(whole + 20, text.GetMaxContentWidth(), 0.5f);
        }

        [Test]
        public void ALinesHookForcesABreak()
        {
            LetteraWorldText text = Hooked(LetteraStages.LinesBroken, "abc", Make(""), out _);
            Assert.AreEqual(2, text.Engine.LineCountForHooks);
        }

        [Test]
        public void APlacedHookMovesAQuad()
        {
            float rest = Make("abc").Engine.Vertices[0].x;
            LetteraWorldText text = Hooked(LetteraStages.Placed, "abc", Make(""), out _);
            Assert.AreEqual(rest + 2, text.Engine.Vertices[0].x, 0.01f);
        }

        [Test]
        public void AMeshHookAddsAQuad()
        {
            int plain = Make("abc").Engine.VertexCount;
            LetteraWorldText text = Hooked(LetteraStages.Mesh, "abc", Make(""), out _);
            Assert.AreEqual(plain + 4, text.Engine.VertexCount);
        }

        [Test]
        public void APoseHookMovesEveryFrameAndAThrowLogsOnce()
        {
            LetteraWorldText text = Hooked(LetteraStages.Pose, "abc", Make(""), out HookEffect effect);
            float rest = text.Engine.RestVertex(0).y;
            int rebuilds = text.RebuildCount;
            Clock(0.016f);
            Assert.AreEqual(rest + 3, text.Engine.Vertices[0].y, 0.01f);
            Assert.AreEqual(rebuilds, text.RebuildCount);
            effect.Throw = () => true;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Clock(0.016f);
            Clock(0.016f);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(1, text.Engine.HookFailureCount);
        }
    }
}
