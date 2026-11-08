using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor
{
    internal enum LetteraSlotState { Inherit, Set, Invalid, OutOfRange }

    /// <summary>What a token means for a slot's descriptor: whether the effect reads it, and as what.</summary>
    internal static class LetteraSlotValue
    {
        static readonly string[] s_Bool = { "1", "0", "true", "false", "on", "off" };

        public static bool ReadsBool(string token, out bool value)
        {
            value = false;
            if (string.IsNullOrEmpty(token)) return false;
            if (token == "1" || token.Equals("true", StringComparison.OrdinalIgnoreCase) || token.Equals("on", StringComparison.OrdinalIgnoreCase)) return value = true;
            return token == "0" || token.Equals("false", StringComparison.OrdinalIgnoreCase) || token.Equals("off", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Which choice a token names, by name or by an initial only one choice has; -1 for none.</summary>
        public static int ChoiceOf(LetteraParamInfo info, string token)
        {
            if (info.Choices == null || string.IsNullOrEmpty(token)) return -1;
            int initial = -1, initials = 0;
            for (int i = 0; i < info.Choices.Length; i++)
            {
                if (info.Choices[i].Equals(token, StringComparison.OrdinalIgnoreCase)) return i;
                if (token.Length == 1 && char.ToLowerInvariant(info.Choices[i][0]) == char.ToLowerInvariant(token[0]))
                {
                    initial = i;
                    initials++;
                }
            }
            return info.Kind == LetteraParamKind.Enum && initials == 1 ? initial : -1;
        }

        /// <summary>The number a token carries, in the descriptor's own terms: a percent is read as a hundredth for a number that has Percent among its units.</summary>
        public static bool TryNumber(LetteraParamInfo info, string token, out double number)
        {
            number = 0;
            if (string.IsNullOrEmpty(token)) return false;
            if (info.Kind == LetteraParamKind.Length)
            {
                if (!LetteraLength.TryParse(token, out LetteraLength length)) return false;
                number = length.Value;
                return true;
            }
            if ((info.Units & LetteraSlotUnits.Percent) != 0 && token.EndsWith("%", StringComparison.Ordinal)
                && LetteraSlots.TryNumber(token.Substring(0, token.Length - 1), out float percent))
            {
                number = percent / 100.0;
                return true;
            }
            if (!LetteraSlots.TryNumber(token, out float value)) return false;
            number = value;
            return true;
        }

        public static bool UnitAllowed(LetteraParamInfo info, LetteraLength.Unit unit)
        {
            if (info.Units == LetteraSlotUnits.None) return true;
            switch (unit)
            {
                case LetteraLength.Unit.Pixels: return (info.Units & LetteraSlotUnits.Px) != 0;
                case LetteraLength.Unit.Em: return (info.Units & LetteraSlotUnits.Em) != 0;
                case LetteraLength.Unit.Percent: return (info.Units & LetteraSlotUnits.Percent) != 0;
                default: return (info.Units & LetteraSlotUnits.Bare) != 0;
            }
        }

        /// <summary>Whether the effect reads the token for this slot. A token it cannot read is dropped by the effect, which then uses its own value.</summary>
        public static bool Reads(LetteraParamInfo info, string token)
        {
            if (string.IsNullOrEmpty(token)) return true;
            if (info.Accepts != null && info.Accepts(token)) return true;
            switch (info.Kind)
            {
                case LetteraParamKind.Color: return LetteraSlots.TryColor(token, out _);
                case LetteraParamKind.Paint: return true;
                case LetteraParamKind.Bool: return ReadsBool(token, out _);
                case LetteraParamKind.Enum:
                case LetteraParamKind.Choice: return ChoiceOf(info, token) >= 0;
                case LetteraParamKind.Length:
                    return LetteraLength.TryParse(token, out LetteraLength length) && UnitAllowed(info, length.Kind) && (!length.IsDelta || (info.Units & LetteraSlotUnits.Delta) != 0 || token[0] == '-' || token[0] == '+');
                case LetteraParamKind.Float:
                case LetteraParamKind.Int: return TryNumber(info, token, out _);
                default: return true;
            }
        }

        public static LetteraSlotState Classify(LetteraParamInfo info, string token)
        {
            if (string.IsNullOrEmpty(token)) return LetteraSlotState.Inherit;
            if (!Reads(info, token)) return LetteraSlotState.Invalid;
            bool numeric = info.Kind == LetteraParamKind.Float || info.Kind == LetteraParamKind.Int || info.Kind == LetteraParamKind.Length;
            if (numeric && !float.IsInfinity(info.Min) && !float.IsInfinity(info.Max) && TryNumber(info, token, out double number) && (number < info.Min - 1e-6 || number > info.Max + 1e-6))
                return LetteraSlotState.OutOfRange;
            return LetteraSlotState.Set;
        }

        /// <summary>A short account of what the slot takes, for the signature line and tooltips.</summary>
        public static string Describe(LetteraParamInfo info)
        {
            switch (info.Kind)
            {
                case LetteraParamKind.Color: return "colour";
                case LetteraParamKind.Paint: return "colour or swatch";
                case LetteraParamKind.Bool: return "on or off";
                case LetteraParamKind.Enum:
                case LetteraParamKind.Choice: return "choice";
                case LetteraParamKind.String: return "text";
                case LetteraParamKind.Int: return Ranged("whole number", info);
                case LetteraParamKind.Length: return "length (" + BareName(info) + ")";
                default: return Ranged("number", info);
            }
        }

        static string Ranged(string what, LetteraParamInfo info) =>
            !float.IsInfinity(info.Min) && !float.IsInfinity(info.Max) ? what + " " + LetteraTagWriter.FormatNumber(info.Min) + ".." + LetteraTagWriter.FormatNumber(info.Max) : what;

        /// <summary>What a bare number is called for this slot.</summary>
        public static string BareName(LetteraParamInfo info)
        {
            if (!string.IsNullOrEmpty(info.BareLabel)) return info.BareLabel;
            return info.BareMeans == LetteraLength.Unit.Em ? "em" : info.BareMeans == LetteraLength.Unit.Percent ? "%" : "px";
        }

        /// <summary>A token that the descriptor's own kind reads, for tests.</summary>
        public static string Sample(LetteraParamInfo info, int variant = 0)
        {
            switch (info.Kind)
            {
                case LetteraParamKind.Color:
                case LetteraParamKind.Paint: return variant == 0 ? "#336699" : "#CC5500";
                case LetteraParamKind.Bool: return variant == 0 ? "true" : "false";
                case LetteraParamKind.Enum:
                case LetteraParamKind.Choice: return info.Choices[Math.Min(variant, info.Choices.Length - 1)];
                case LetteraParamKind.String: return variant == 0 ? "en" : "ru";
                case LetteraParamKind.Int: return !float.IsInfinity(info.Min) ? LetteraTagWriter.FormatNumber(info.Min + (variant == 0 ? 0 : info.Step)) : "3";
                default:
                    double v = !float.IsInfinity(info.Min) && !float.IsInfinity(info.Max) ? info.Min + (info.Max - info.Min) * (variant == 0 ? 0.25 : 0.75) : (variant == 0 ? 0.25 : 0.75);
                    return LetteraTagWriter.FormatNumber(v, 3);
            }
        }
    }

    /// <summary>What a slot shows when its tag leaves it empty: the style's default, else the effect's own value.</summary>
    internal static class LetteraSlotDefaults
    {
        public const string FromStyle = "style default", FromEffect = "effect";

        public static string Inherited(LetteraParamInfo info, LetteraEffect effect, string[] styleDefaults, out string source)
        {
            source = "";
            if (styleDefaults != null && info.Slot >= 0 && info.Slot < styleDefaults.Length && !string.IsNullOrEmpty(styleDefaults[info.Slot]))
            {
                source = FromStyle;
                return styleDefaults[info.Slot];
            }
            if (!string.IsNullOrEmpty(info.DefaultField) && effect != null && TryRead(effect, info.DefaultField, info, out string text))
            {
                source = FromEffect;
                return text;
            }
            if (!string.IsNullOrEmpty(info.Default))
            {
                source = FromEffect;
                return info.Default;
            }
            return "";
        }

        /// <summary>A field of an effect by path, such as <c>m_Offset.x</c>, written as a tag would write it.</summary>
        public static bool TryRead(object effect, string path, LetteraParamInfo info, out string text)
        {
            text = null;
            object current = effect;
            foreach (string part in path.Split('.'))
            {
                if (current == null) return false;
                FieldInfo field = FindField(current.GetType(), part);
                if (field == null) return false;
                current = field.GetValue(current);
            }
            switch (current)
            {
                case null: return false;
                case float f: text = LetteraTagWriter.FormatNumber(f, info.Decimals); return true;
                case int n: text = n.ToString(CultureInfo.InvariantCulture); return true;
                case bool b: text = b ? "true" : "false"; return true;
                case string s: text = s; return true;
                case Color32 c32: text = LetteraTagWriter.FormatColor(c32); return true;
                case Color c: text = LetteraTagWriter.FormatColor(c); return true;
                case Enum e: text = e.ToString(); return true;
                default: return false;
            }
        }

        static FieldInfo FindField(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }
    }

    /// <summary>One slot as the panel shows it.</summary>
    internal sealed class LetteraSlotRow
    {
        public LetteraParamInfo Info;
        /// <summary>The value the tag gives, or null when the slot is empty.</summary>
        public string Token;
        public LetteraSlotState State;
        public string Inherited, InheritedFrom;
    }

    internal static class LetteraTagModel
    {
        /// <summary>The rows of an effect's described slots for a tag, in slot order. The tag may be null for an empty form.</summary>
        public static List<LetteraSlotRow> Rows(LetteraTagSpan tag, IReadOnlyList<LetteraParamInfo> described, LetteraEffect effect, string[] styleDefaults)
        {
            var rows = new List<LetteraSlotRow>(described.Count);
            for (int i = 0; i < described.Count; i++)
            {
                LetteraParamInfo info = described[i];
                if (info.Slot < 0) continue;
                string token = tag?.Slot(info.Slot);
                var row = new LetteraSlotRow { Info = info, Token = token, State = LetteraSlotValue.Classify(info, token) };
                row.Inherited = LetteraSlotDefaults.Inherited(info, effect, styleDefaults, out row.InheritedFrom);
                rows.Add(row);
            }
            rows.Sort((a, b) => a.Info.Slot.CompareTo(b.Info.Slot));
            return rows;
        }

        /// <summary>Whether a form can be drawn: at least one described slot.</summary>
        public static bool HasSchema(IReadOnlyList<LetteraParamInfo> described)
        {
            if (described == null) return false;
            for (int i = 0; i < described.Count; i++)
                if (described[i].Slot >= 0)
                    return true;
            return false;
        }

        public static int SlotCount(IReadOnlyList<LetteraParamInfo> described)
        {
            int max = -1;
            for (int i = 0; i < described.Count; i++) max = Math.Max(max, described[i].Slot);
            return max + 1;
        }

        /// <summary>The markup of a bare tag when only the style's default fills it, for the style card: <c>&lt;color=#FF8800&gt;</c>.</summary>
        public static string BareMeans(string tagName, string defaultParameter) =>
            "<" + tagName + (string.IsNullOrEmpty(defaultParameter) ? "" : "=" + defaultParameter) + ">";
    }
}
