using System.Text;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>The one line a phone's control reports per event: tab-separated plain values, so nothing but a string crosses. Android and iOS write the same form.</summary>
    internal static class LetteraHostLines
    {
        /// <summary>False for a line that cannot be read: it is dropped, and the next push puts the control right.</summary>
        internal static bool TryParse(string line, out LetteraHostEvent e)
        {
            e = default;
            if (string.IsNullOrEmpty(line)) return false;
            string[] parts = line.Split('\t');
            if (parts.Length != 9 || parts[0].Length != 1) return false;
            if (!int.TryParse(parts[1], out e.BaseRevision) || !int.TryParse(parts[2], out e.Start) || !int.TryParse(parts[3], out e.End) || !int.TryParse(parts[4], out e.Caret)) return false;
            if (!int.TryParse(parts[5], out int flag) || !int.TryParse(parts[6], out int key) || !int.TryParse(parts[7], out int modifiers)) return false;
            e.Text = Unescape(parts[8]);
            switch (parts[0][0])
            {
                case 'R': e.Kind = LetteraHostEventKind.Replace; break;
                case 'W': e.Kind = LetteraHostEventKind.WholeText; break;
                case 'C': e.Kind = LetteraHostEventKind.Compose; break;
                case 'E':
                    e.Kind = LetteraHostEventKind.ComposeEnd;
                    e.Commit = flag != 0;
                    break;
                case 'S': e.Kind = LetteraHostEventKind.Select; break;
                case 'V': e.Kind = LetteraHostEventKind.Verb; break;
                case 'K':
                    e.Kind = LetteraHostEventKind.Key;
                    e.KeyCode = (LetteraKeyCode)key;
                    e.Modifiers = (LetteraKeyModifiers)modifiers;
                    e.Repeat = flag != 0;
                    break;
                case 'F':
                    e.Kind = LetteraHostEventKind.KeyboardFrame;
                    e.Frame = new Rect(e.Start, e.End, e.Caret, flag);
                    break;
                case 'X': e.Kind = LetteraHostEventKind.Closed; break;
                default: return false;
            }
            return true;
        }

        static string Unescape(string text)
        {
            if (text.IndexOf('\\') < 0) return text;
            var builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '\\' || i + 1 == text.Length)
                {
                    builder.Append(c);
                    continue;
                }
                char next = text[++i];
                builder.Append(next == 't' ? '\t' : next == 'n' ? '\n' : next == 'r' ? '\r' : next);
            }
            return builder.ToString();
        }

        /// <summary>Android's input type for a set of traits, from its documented constants. Pure, so a table can test it.</summary>
        internal static int AndroidInputType(in LetteraKeyboardTraits traits)
        {
            const int ClassText = 1, ClassNumber = 2, ClassPhone = 3, FlagDecimal = 0x2000;
            const int VariationUri = 0x10, VariationEmail = 0x20, VariationPassword = 0x80, VariationPersonName = 0x60, FlagMultiLine = 0x20000, FlagAutoCorrect = 0x8000, FlagNoSuggestions = 0x80000;
            const int CapCharacters = 0x1000, CapWords = 0x2000, CapSentences = 0x4000;
            int type;
            switch (traits.Kind)
            {
                case LetteraKeyboardKind.Number: type = ClassNumber; break;
                case LetteraKeyboardKind.Decimal: type = ClassNumber | FlagDecimal; break;
                case LetteraKeyboardKind.Phone: type = ClassPhone; break;
                case LetteraKeyboardKind.Url: type = ClassText | VariationUri; break;
                case LetteraKeyboardKind.Email: type = ClassText | VariationEmail; break;
                case LetteraKeyboardKind.Name: type = ClassText | VariationPersonName; break;
                default: type = ClassText; break;
            }
            if ((type & 0xF) == ClassText)
            {
                if (traits.Secure) type = ClassText | VariationPassword;
                if (traits.Multiline && !traits.Secure) type |= FlagMultiLine;
                type |= traits.Autocorrect ? FlagAutoCorrect : FlagNoSuggestions;
                switch (traits.AutoCapitalize)
                {
                    case LetteraAutoCapitalize.Sentences: type |= CapSentences; break;
                    case LetteraAutoCapitalize.Words: type |= CapWords; break;
                    case LetteraAutoCapitalize.All: type |= CapCharacters; break;
                }
            }
            return type;
        }

        /// <summary>Android's action for a return key, and the flags a field asked for.</summary>
        internal static int AndroidImeOptions(in LetteraKeyboardTraits traits)
        {
            const int ActionNone = 1, ActionGo = 2, ActionSearch = 3, ActionSend = 4, ActionNext = 5, ActionDone = 6;
            int options;
            switch (traits.ReturnKey)
            {
                case LetteraReturnKey.Done: options = ActionDone; break;
                case LetteraReturnKey.Go: options = ActionGo; break;
                case LetteraReturnKey.Next: options = ActionNext; break;
                case LetteraReturnKey.Search: options = ActionSearch; break;
                case LetteraReturnKey.Send: options = ActionSend; break;
                default: options = traits.Multiline ? ActionNone : ActionDone; break;
            }
            if ((traits.AndroidImeFlags & LetteraAndroidImeFlags.NoFullscreen) != 0) options |= 0x2000000;
            if ((traits.AndroidImeFlags & LetteraAndroidImeFlags.NoExtractUi) != 0) options |= 0x10000000;
            if ((traits.AndroidImeFlags & LetteraAndroidImeFlags.NoAccessoryAction) != 0) options |= 0x20000000;
            if ((traits.AndroidImeFlags & LetteraAndroidImeFlags.NoPersonalizedLearning) != 0) options |= 0x1000000;
            return options;
        }
    }
}
