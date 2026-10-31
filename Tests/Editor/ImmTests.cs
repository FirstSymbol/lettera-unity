using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The window procedure's rules, with a fake input method and no window. The real window's run is a self-test flag in the player.</summary>
    public class ImmTests : EditingFixture
    {
        sealed class FakeImm : ILetteraImm
        {
            public string Text = "日本語", Result = "日本語";
            public int Cursor = 2;
            public byte[] Attrs = { 1, 1, 2 };
            public int[] Bounds = { 0, 2, 3 };
            public bool Detached;
            public int Places;
            public string CompositionText(IntPtr window) => Text;
            public int CompositionCursor(IntPtr window) => Cursor;
            public string ResultText(IntPtr window) => Result;
            public byte[] Attributes(IntPtr window) => Attrs;
            public int[] Clauses(IntPtr window) => Bounds;
            public void PlaceCandidates(IntPtr window, Rect excluded) => Places++;
            public void Detach(IntPtr window, bool detached) => Detached = detached;
        }

        sealed class NoKeys : ILetteraInputSource
        {
            public void Attach(ILetteraInputSink sink, ILetteraInputHost host) { }
            public void Detach() { }
            public void Pump() { }
        }

        readonly List<(uint message, long lParam)> m_Forwarded = new List<(uint, long)>();

        LetteraImmInputSource Start(LetteraEditable field, FakeImm imm)
        {
            var source = new LetteraImmInputSource(new NoKeys(), imm);
            source.Attach(field.Selectable, field.Selectable);
            LetteraImmInputSource.MakeActiveForTests(source);
            LetteraImmInputSource.ForwardOverride = (w, m, wp, lp) =>
            {
                m_Forwarded.Add((m, lp.ToInt64()));
                return IntPtr.Zero;
            };
            return source;
        }

        [TearDown]
        public void Back()
        {
            LetteraImmInputSource.ForwardOverride = null;
            LetteraImmInputSource.MakeActiveForTests(null);
            m_Forwarded.Clear();
        }

        static void Send(uint message, long flags = 0) => LetteraImmInputSource.ProcedureForTests(IntPtr.Zero, message, IntPtr.Zero, (IntPtr)flags);

        const long All = LetteraImmInputSource.GCS_COMPSTR | LetteraImmInputSource.GCS_COMPATTR | LetteraImmInputSource.GCS_COMPCLAUSE | LetteraImmInputSource.GCS_CURSORPOS;

        [Test]
        public void TwoClausesReachTheFieldAndTheTextWaits()
        {
            LetteraEditable field = Field("ab");
            LetteraImmInputSource source = Start(field, new FakeImm());
            Send(LetteraImmInputSource.WM_IME_STARTCOMPOSITION);
            Send(LetteraImmInputSource.WM_IME_COMPOSITION, All);
            source.Pump();
            Assert.IsTrue(field.IsComposing);
            Assert.AreEqual("ab", field.PlainText);
            StringAssert.Contains("日本語", field.ShownText);
            source.Detach();
        }

        [Test]
        public void CommitIsOneStepWithNoCharacterTwice()
        {
            LetteraEditable field = Field("ab");
            LetteraImmInputSource source = Start(field, new FakeImm());
            Send(LetteraImmInputSource.WM_IME_STARTCOMPOSITION);
            Send(LetteraImmInputSource.WM_IME_COMPOSITION, All);
            source.Pump();
            Send(LetteraImmInputSource.WM_IME_COMPOSITION, LetteraImmInputSource.GCS_RESULTSTR);
            Send(LetteraImmInputSource.WM_IME_ENDCOMPOSITION);
            source.Pump();
            Assert.IsFalse(field.IsComposing);
            Assert.AreEqual("ab日本語", field.PlainText);
            field.Undo();
            Assert.AreEqual("ab", field.PlainText);
            source.Detach();
        }

        [Test]
        public void EscapeTakesTheCompositionAway()
        {
            LetteraEditable field = Field("ab");
            LetteraImmInputSource source = Start(field, new FakeImm());
            Send(LetteraImmInputSource.WM_IME_COMPOSITION, All);
            source.Pump();
            Send(LetteraImmInputSource.WM_IME_ENDCOMPOSITION);
            source.Pump();
            Assert.IsFalse(field.IsComposing);
            Assert.AreEqual("ab", field.PlainText);
            source.Detach();
        }

        [Test]
        public void TheAskForASystemCompositionWindowIsCleared()
        {
            LetteraEditable field = Field("");
            LetteraImmInputSource source = Start(field, new FakeImm());
            Send(LetteraImmInputSource.WM_IME_SETCONTEXT, 0xC000000FL);
            Assert.AreEqual(1, m_Forwarded.Count);
            Assert.AreEqual(LetteraImmInputSource.WM_IME_SETCONTEXT, m_Forwarded[0].message);
            Assert.AreEqual(0x4000000FL, m_Forwarded[0].lParam);
            source.Detach();
        }

        [Test]
        public void CompositionMessagesStopHereAndOthersGoOn()
        {
            LetteraEditable field = Field("");
            LetteraImmInputSource source = Start(field, new FakeImm());
            Send(LetteraImmInputSource.WM_IME_STARTCOMPOSITION);
            Send(LetteraImmInputSource.WM_IME_COMPOSITION, All);
            Send(LetteraImmInputSource.WM_IME_ENDCOMPOSITION);
            Assert.AreEqual(0, m_Forwarded.Count);
            Send(0x0100);
            Send(0x0005);
            Assert.AreEqual(2, m_Forwarded.Count);
            source.Detach();
        }

        [Test]
        public void AttributesBecomeKinds()
        {
            LetteraClause[] clauses = LetteraImmInputSource.BuildClauses("abcd", new byte[] { 0, 3, 2, 1 }, new[] { 0, 1, 2, 3, 4 });
            Assert.AreEqual(LetteraClauseKind.Raw, clauses[0].Kind);
            Assert.AreEqual(LetteraClauseKind.ActiveRaw, clauses[1].Kind);
            Assert.AreEqual(LetteraClauseKind.Converted, clauses[2].Kind);
            Assert.AreEqual(LetteraClauseKind.ActiveConverted, clauses[3].Kind);
            Assert.AreEqual(LetteraClauseKind.Invalid, LetteraImmInputSource.BuildClauses("a", new byte[] { 4 }, new[] { 0, 1 })[0].Kind);
        }

        [Test]
        public void ClauseBoundariesThatMakeNoSenseAreOneClause()
        {
            foreach (int[] bad in new[] { null, new[] { 5 }, new[] { 1, 3 }, new[] { 0, 9 }, new[] { 0, 2, 1, 3 } })
            {
                LetteraClause[] clauses = LetteraImmInputSource.BuildClauses("abc", null, bad);
                Assert.AreEqual(1, clauses.Length);
                Assert.AreEqual(3, clauses[0].Length);
            }
        }

        [Test]
        public void AThrowInsideFallsBackAndNeverCrossesIntoTheSystem()
        {
            LetteraEditable field = Field("");
            var imm = new ThrowingImm();
            var source = new LetteraImmInputSource(new NoKeys(), imm);
            source.Attach(field.Selectable, field.Selectable);
            LetteraImmInputSource.MakeActiveForTests(source);
            LetteraImmInputSource.ForwardOverride = (w, m, wp, lp) =>
            {
                m_Forwarded.Add((m, lp.ToInt64()));
                return IntPtr.Zero;
            };
            try
            {
                LetteraPlatform.Override = null;
                Assert.DoesNotThrow(() => Send(LetteraImmInputSource.WM_IME_COMPOSITION, All));
                Assert.AreEqual(1, m_Forwarded.Count, "it goes on to the window");
                Assert.IsFalse(LetteraPlatform.IsActive(LetteraNativeFeature.TextInput));
            }
            finally
            {
                LetteraPlatform.ResetForTests();
                source.Detach();
            }
        }

        sealed class ThrowingImm : ILetteraImm
        {
            public string CompositionText(IntPtr window) => throw new InvalidOperationException("no");
            public int CompositionCursor(IntPtr window) => 0;
            public string ResultText(IntPtr window) => "";
            public byte[] Attributes(IntPtr window) => null;
            public int[] Clauses(IntPtr window) => null;
            public void PlaceCandidates(IntPtr window, Rect excluded) { }
            public void Detach(IntPtr window, bool detached) { }
        }

        [Test]
        public void AnUnknownVerbIsRefusedOutLoud()
        {
            LetteraEditable field = Field("abc");
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no verb"));
            field.OnInputAction("explode");
            Assert.AreEqual("abc", field.PlainText);
            Assert.IsTrue(LetteraVerbs.IsKnown("next"));
            Assert.IsFalse(LetteraVerbs.IsKnown("explode"));
        }

        [Test]
        public void OldTraitsReadAsToday()
        {
            var traits = JsonUtility.FromJson<LetteraKeyboardTraits>("{\"Kind\":3,\"ReturnKey\":1,\"Autocorrect\":true,\"Multiline\":true,\"Secure\":false,\"AutofillHint\":\"email\"}");
            Assert.AreEqual(LetteraAutoCapitalize.Default, traits.AutoCapitalize);
            Assert.AreEqual(LetteraTraitToggle.Default, traits.SpellCheck);
            Assert.AreEqual(LetteraIosAppearance.Default, traits.IosAppearance);
            Assert.AreEqual(LetteraAndroidImeFlags.None, traits.AndroidImeFlags);
            Assert.AreEqual("email", traits.AutofillHint);
            Assert.IsTrue(traits.Autocorrect);
        }
    }
}
