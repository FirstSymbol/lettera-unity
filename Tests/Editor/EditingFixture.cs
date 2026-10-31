using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Fields driven through the same seams an OS drives them through, on a clock the test controls.</summary>
    public abstract class EditingFixture : TextFixture
    {
        protected double Now = 100;

        [SetUp]
        public void EditingSetUp()
        {
            Now = 100;
            LetteraEditClock.Override = () => Now;
            LetteraInput.NoDefaultSource = true;
            LetteraKeyTable.FamilyOverride = LetteraPlatformFamily.Pc;
            LetteraClipboard.Backend = new FakeClipboard();
            LetteraCaret.PrefersSteady = false;
            LetteraAccessibility.ReducedMotion = false;
        }

        [TearDown]
        public void EditingTearDown()
        {
            if (LetteraSelectable.Current != null) LetteraSelectable.Current.Deactivate();
            LetteraEditClock.Override = null;
            LetteraInput.NoDefaultSource = false;
            LetteraKeyTable.FamilyOverride = null;
            LetteraClipboard.Backend = null;
            LetteraSelectable.ClassifyOverride = null;
            LetteraSelectable.s_ShiftOverride = null;
        }

        protected sealed class FakeClipboard : ILetteraClipboardBackend
        {
            public readonly List<LetteraClipItem> Items = new List<LetteraClipItem>();
            public int Writes;

            public void Write(IReadOnlyList<LetteraClipItem> items)
            {
                Writes++;
                Items.Clear();
                foreach (LetteraClipItem item in items) Items.Add(item);
            }

            public void Read(List<LetteraClipItem> items) => items.AddRange(Items);

            public void ReadAsync(System.Action<List<LetteraClipItem>> done) => done(new List<LetteraClipItem>(Items));

            public string Text(string format)
            {
                foreach (LetteraClipItem item in Items)
                    if (item.Format == format)
                        return item.Text;
                return null;
            }
        }

        protected FakeClipboard Clip => (FakeClipboard)LetteraClipboard.Backend;

        protected LetteraEditable Field(string text = "", float width = 1000, float height = 500, LetteraFontAsset font = null)
        {
            LetteraWorldText label = Make(text, width, height, font);
            var field = label.gameObject.AddComponent<LetteraEditable>();
            field.Selectable.Activate();
            field.Selectable.PlaceCaret(field.Length);
            return field;
        }

        protected LetteraEditable MixedField(string text, float width = 1000)
        {
            LetteraEditable field = Field("", width);
            LetteraWorldText label = field.GetComponent<LetteraWorldText>();
            label.Font = null;
            label.FontStack = Stack(Latin, Arabic, Hebrew);
            field.SetSource(text);
            return field;
        }

        protected LetteraSelectable Reader(string text, float width = 1000)
        {
            LetteraWorldText label = Make(text, width);
            var selectable = label.gameObject.AddComponent<LetteraSelectable>();
            selectable.Activate();
            return selectable;
        }

        protected static void Type(LetteraEditable field, string text)
        {
            // One keystroke at a time, as a keyboard delivers them.
            for (int i = 0; i < text.Length; i++)
            {
                int width = char.IsHighSurrogate(text[i]) ? 2 : 1;
                field.Selectable.Input.Text(text.Substring(i, width));
                i += width - 1;
            }
        }

        protected static void Press(LetteraEditable field, LetteraKeyCode key, LetteraKeyModifiers modifiers = LetteraKeyModifiers.None) => field.Selectable.Input.Key(key, modifiers);

        protected const LetteraKeyModifiers Ctrl = LetteraKeyModifiers.Control, Shift = LetteraKeyModifiers.Shift, Alt = LetteraKeyModifiers.Alt, Cmd = LetteraKeyModifiers.Command;

        protected static PointerEventData Pointer(Vector2 screen, PointerEventData.InputButton button = PointerEventData.InputButton.Left, int id = -1) =>
            new PointerEventData(null) { position = screen, pressPosition = screen, button = button, pointerId = id };
    }
}
