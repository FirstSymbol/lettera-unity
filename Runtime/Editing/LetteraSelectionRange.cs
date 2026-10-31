using System;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>A selection in codepoint indices of the shown text: where it began, where its moving end stands, and which side that end is on.</summary>
    public readonly struct LetteraSelectionRange : IEquatable<LetteraSelectionRange>
    {
        public readonly int Anchor, Head;
        public readonly LetteraCaretBias Bias;

        public LetteraSelectionRange(int anchor, int head, LetteraCaretBias bias = LetteraCaretBias.Forward)
        {
            Anchor = anchor;
            Head = head;
            Bias = bias;
        }

        public bool IsCaret => Anchor == Head;
        public int Start => Mathf.Min(Anchor, Head);
        public int End => Mathf.Max(Anchor, Head);
        public int Length => End - Start;

        public static LetteraSelectionRange AtCaret(int index, LetteraCaretBias bias = LetteraCaretBias.Forward) => new LetteraSelectionRange(index, index, bias);

        public LetteraSelectionRange Clamp(int length) => new LetteraSelectionRange(Mathf.Clamp(Anchor, 0, length), Mathf.Clamp(Head, 0, length), Bias);

        public bool Equals(LetteraSelectionRange other) => Anchor == other.Anchor && Head == other.Head && Bias == other.Bias;
        public override bool Equals(object obj) => obj is LetteraSelectionRange other && Equals(other);
        public override int GetHashCode() => Anchor * 397 ^ Head * 31 ^ (int)Bias;
        public static bool operator ==(LetteraSelectionRange a, LetteraSelectionRange b) => a.Equals(b);
        public static bool operator !=(LetteraSelectionRange a, LetteraSelectionRange b) => !a.Equals(b);
        public override string ToString() => $"[{Anchor}..{Head} {Bias}]";
    }

    /// <summary>A selection change on its way: a handler can assign Wanted to clamp it, or set Veto.</summary>
    public struct LetteraSelectionProposal
    {
        public LetteraSelectionRange Current, Wanted;
        public string Reason;
        public bool Veto;
    }

    public delegate void LetteraSelectionFilter(ref LetteraSelectionProposal proposal);

    /// <summary>A selection after it changed.</summary>
    public readonly struct LetteraSelectionChange
    {
        public readonly LetteraSelectionRange Previous, Current;
        public readonly string Reason;

        public LetteraSelectionChange(LetteraSelectionRange previous, LetteraSelectionRange current, string reason)
        {
            Previous = previous;
            Current = current;
            Reason = reason;
        }
    }

    public enum LetteraInactiveHighlight { Hide, Dim, Keep }

    /// <summary>Why a text lost the keyboard.</summary>
    public enum LetteraBlurReason { Pointer, Code, Submit, Cancel, Tab, Disabled, AppFocus }

    /// <summary>The time selection and editing run on. It ignores the time scale; tests replace it.</summary>
    public static class LetteraEditClock
    {
        /// <summary>Replaces the clock. Null restores real time.</summary>
        public static Func<double> Override;

        public static double Now => Override != null ? Override() : Time.realtimeSinceStartupAsDouble;
    }

    /// <summary>For scripts and for what later reads the system's preference.</summary>
    public static class LetteraAccessibility
    {
        static bool s_ReducedMotion, s_ScriptSetMotion, s_SteadyCaret, s_ScriptSetCaret;

        /// <summary>Animations snap to their end. On the web this follows the browser's preference, until a script sets it.</summary>
        public static bool ReducedMotion
        {
            get => s_ReducedMotion;
            set
            {
                s_ScriptSetMotion = true;
                if (s_ReducedMotion == value) return;
                s_ReducedMotion = value;
                Changed?.Invoke();
            }
        }

        /// <summary>The caret stays on rather than blinking. Follows the system's preference, until a script sets it.</summary>
        public static bool SteadyCaret
        {
            get => s_SteadyCaret;
            set
            {
                s_ScriptSetCaret = true;
                if (s_SteadyCaret == value) return;
                s_SteadyCaret = value;
                Changed?.Invoke();
            }
        }

        /// <summary>What the system prefers. A value a script set wins over this.</summary>
        internal static void FromSystem(bool reducedMotion, bool steadyCaret)
        {
            bool changed = false;
            if (!s_ScriptSetMotion && s_ReducedMotion != reducedMotion)
            {
                s_ReducedMotion = reducedMotion;
                changed = true;
            }
            if (!s_ScriptSetCaret && s_SteadyCaret != steadyCaret)
            {
                s_SteadyCaret = steadyCaret;
                changed = true;
            }
            if (changed) Changed?.Invoke();
        }

        internal static void ResetForTests()
        {
            s_ScriptSetMotion = s_ScriptSetCaret = false;
            s_ReducedMotion = s_SteadyCaret = false;
        }

        /// <summary>Raised when ReducedMotion changes.</summary>
        public static event System.Action Changed;
    }
}
