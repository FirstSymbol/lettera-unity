using System;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>The keys editing knows. Lettera's own, so that no public API depends on an input backend.</summary>
    public enum LetteraKeyCode
    {
        None, Backspace, Delete, Enter, KeypadEnter, Tab, Escape, Left, Right, Up, Down, Home, End, PageUp, PageDown, Insert, Menu, F10,
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z, Backslash, Space,
    }

    [Flags]
    public enum LetteraKeyModifiers { None = 0, Shift = 1, Control = 2, Alt = 4, Command = 8 }

    /// <summary>The convention keys follow: the system the player runs on, also under a browser.</summary>
    public enum LetteraPlatformFamily { Pc, Apple }

    /// <summary>What a key can do.</summary>
    public enum LetteraEditVerb
    {
        None,
        MoveCharPrev, MoveCharNext, MoveWordPrev, MoveWordNext, MoveLineStart, MoveLineEnd, MoveParagraphStart, MoveParagraphEnd, MoveUp, MoveDown, MovePageUp, MovePageDown, MoveDocStart, MoveDocEnd,
        ExtendCharPrev, ExtendCharNext, ExtendWordPrev, ExtendWordNext, ExtendLineStart, ExtendLineEnd, ExtendParagraphStart, ExtendParagraphEnd, ExtendUp, ExtendDown, ExtendPageUp, ExtendPageDown, ExtendDocStart, ExtendDocEnd,
        DeleteBack, DeleteForward, DeleteWordBack, DeleteWordForward, DeleteToLineStart, DeleteToLineEnd, DeleteToParagraphEnd,
        Transpose, InsertNewline, InsertTab, Copy, Cut, Paste, PastePlain, SelectAll, Undo, Redo, Submit, Cancel, OpenMenu, ScrollDocStart, ScrollDocEnd, Swallow, Pass,
    }

    /// <summary>A key on its way to the table. A rule can replace the verb.</summary>
    public struct LetteraKeyDecision
    {
        public LetteraKeyCode Key;
        public LetteraKeyModifiers Modifiers;
        public bool IsRepeat;
        public LetteraEditVerb Verb;
    }

    /// <summary>Which verb a key means on each family of systems.</summary>
    public static class LetteraKeyTable
    {
        /// <summary>Replaces the detected family. Null detects it.</summary>
        public static LetteraPlatformFamily? FamilyOverride;

        public static LetteraPlatformFamily Family
        {
            get
            {
                if (FamilyOverride.HasValue) return FamilyOverride.Value;
                switch (Application.platform)
                {
                    case RuntimePlatform.OSXEditor:
                    case RuntimePlatform.OSXPlayer:
                    case RuntimePlatform.IPhonePlayer:
                        return LetteraPlatformFamily.Apple;
                    case RuntimePlatform.WebGLPlayer:
                        // Unity reports the visitor's system here.
                        string os = SystemInfo.operatingSystem ?? "";
                        return os.IndexOf("Mac", StringComparison.OrdinalIgnoreCase) >= 0 || os.IndexOf("iOS", StringComparison.OrdinalIgnoreCase) >= 0 || os.IndexOf("iPhone", StringComparison.OrdinalIgnoreCase) >= 0
                            ? LetteraPlatformFamily.Apple
                            : LetteraPlatformFamily.Pc;
                    default:
                        return LetteraPlatformFamily.Pc;
                }
            }
        }

        const LetteraKeyModifiers S = LetteraKeyModifiers.Shift, C = LetteraKeyModifiers.Control, A = LetteraKeyModifiers.Alt, M = LetteraKeyModifiers.Command;

        /// <summary>The verb of a key, or None. Control with Alt is AltGr on Windows and Linux: that is text, never a shortcut.</summary>
        public static LetteraEditVerb Resolve(LetteraKeyCode key, LetteraKeyModifiers modifiers, LetteraPlatformFamily family)
        {
            return family == LetteraPlatformFamily.Apple ? ResolveApple(key, modifiers) : ResolvePc(key, modifiers);
        }

        static LetteraEditVerb Extended(LetteraEditVerb move, bool extend) => extend ? move + (LetteraEditVerb.ExtendCharPrev - LetteraEditVerb.MoveCharPrev) : move;

        static LetteraEditVerb ResolvePc(LetteraKeyCode key, LetteraKeyModifiers m)
        {
            if ((m & (C | A)) == (C | A) || (m & M) != 0) return LetteraEditVerb.None;
            bool shift = (m & S) != 0, control = (m & C) != 0;
            if ((m & A) != 0) return LetteraEditVerb.None;
            switch (key)
            {
                case LetteraKeyCode.Left: return Extended(control ? LetteraEditVerb.MoveWordPrev : LetteraEditVerb.MoveCharPrev, shift);
                case LetteraKeyCode.Right: return Extended(control ? LetteraEditVerb.MoveWordNext : LetteraEditVerb.MoveCharNext, shift);
                case LetteraKeyCode.Up: return Extended(control ? LetteraEditVerb.MoveParagraphStart : LetteraEditVerb.MoveUp, shift);
                case LetteraKeyCode.Down: return Extended(control ? LetteraEditVerb.MoveParagraphEnd : LetteraEditVerb.MoveDown, shift);
                case LetteraKeyCode.Home: return Extended(control ? LetteraEditVerb.MoveDocStart : LetteraEditVerb.MoveLineStart, shift);
                case LetteraKeyCode.End: return Extended(control ? LetteraEditVerb.MoveDocEnd : LetteraEditVerb.MoveLineEnd, shift);
                case LetteraKeyCode.PageUp: return Extended(LetteraEditVerb.MovePageUp, shift);
                case LetteraKeyCode.PageDown: return Extended(LetteraEditVerb.MovePageDown, shift);
                case LetteraKeyCode.Backspace:
                    if (control) return shift ? LetteraEditVerb.DeleteToLineStart : LetteraEditVerb.DeleteWordBack;
                    return LetteraEditVerb.DeleteBack;
                case LetteraKeyCode.Delete:
                    if (control) return shift ? LetteraEditVerb.DeleteToLineEnd : LetteraEditVerb.DeleteWordForward;
                    return shift ? LetteraEditVerb.Cut : LetteraEditVerb.DeleteForward;
                case LetteraKeyCode.Insert:
                    if (control && !shift) return LetteraEditVerb.Copy;
                    return shift && !control ? LetteraEditVerb.Paste : LetteraEditVerb.None;
                case LetteraKeyCode.Enter:
                case LetteraKeyCode.KeypadEnter:
                    return control ? LetteraEditVerb.None : LetteraEditVerb.InsertNewline;
                case LetteraKeyCode.Tab: return control ? LetteraEditVerb.None : LetteraEditVerb.Pass;
                case LetteraKeyCode.Escape: return LetteraEditVerb.Cancel;
                case LetteraKeyCode.Menu: return LetteraEditVerb.OpenMenu;
                case LetteraKeyCode.F10: return shift ? LetteraEditVerb.OpenMenu : LetteraEditVerb.None;
            }
            if (!control) return LetteraEditVerb.None;
            switch (key)
            {
                case LetteraKeyCode.A: return shift ? LetteraEditVerb.None : LetteraEditVerb.SelectAll;
                case LetteraKeyCode.C: return shift ? LetteraEditVerb.None : LetteraEditVerb.Copy;
                case LetteraKeyCode.X: return shift ? LetteraEditVerb.None : LetteraEditVerb.Cut;
                case LetteraKeyCode.V: return shift ? LetteraEditVerb.PastePlain : LetteraEditVerb.Paste;
                case LetteraKeyCode.Z: return shift ? LetteraEditVerb.Redo : LetteraEditVerb.Undo;
                case LetteraKeyCode.Y: return shift ? LetteraEditVerb.None : LetteraEditVerb.Redo;
            }
            return LetteraEditVerb.None;
        }

        static LetteraEditVerb ResolveApple(LetteraKeyCode key, LetteraKeyModifiers m)
        {
            bool shift = (m & S) != 0, control = (m & C) != 0, option = (m & A) != 0, command = (m & M) != 0;
            switch (key)
            {
                case LetteraKeyCode.Left:
                    return Extended(command ? LetteraEditVerb.MoveLineStart : option ? LetteraEditVerb.MoveWordPrev : LetteraEditVerb.MoveCharPrev, shift);
                case LetteraKeyCode.Right:
                    return Extended(command ? LetteraEditVerb.MoveLineEnd : option ? LetteraEditVerb.MoveWordNext : LetteraEditVerb.MoveCharNext, shift);
                case LetteraKeyCode.Up:
                    return Extended(command ? LetteraEditVerb.MoveDocStart : option ? LetteraEditVerb.MoveParagraphStart : LetteraEditVerb.MoveUp, shift);
                case LetteraKeyCode.Down:
                    return Extended(command ? LetteraEditVerb.MoveDocEnd : option ? LetteraEditVerb.MoveParagraphEnd : LetteraEditVerb.MoveDown, shift);
                case LetteraKeyCode.Home: return LetteraEditVerb.ScrollDocStart;
                case LetteraKeyCode.End: return LetteraEditVerb.ScrollDocEnd;
                case LetteraKeyCode.PageUp: return Extended(LetteraEditVerb.MovePageUp, shift);
                case LetteraKeyCode.PageDown: return Extended(LetteraEditVerb.MovePageDown, shift);
                case LetteraKeyCode.Backspace:
                    return command ? LetteraEditVerb.DeleteToLineStart : option ? LetteraEditVerb.DeleteWordBack : LetteraEditVerb.DeleteBack;
                case LetteraKeyCode.Delete:
                    return command ? LetteraEditVerb.DeleteToLineEnd : option ? LetteraEditVerb.DeleteWordForward : LetteraEditVerb.DeleteForward;
                case LetteraKeyCode.Enter:
                case LetteraKeyCode.KeypadEnter:
                    return command ? LetteraEditVerb.None : LetteraEditVerb.InsertNewline;
                case LetteraKeyCode.Tab: return LetteraEditVerb.Pass;
                case LetteraKeyCode.Escape: return LetteraEditVerb.Cancel;
                case LetteraKeyCode.Menu: return LetteraEditVerb.OpenMenu;
            }
            if (command)
            {
                switch (key)
                {
                    case LetteraKeyCode.A: return LetteraEditVerb.SelectAll;
                    case LetteraKeyCode.C: return LetteraEditVerb.Copy;
                    case LetteraKeyCode.X: return LetteraEditVerb.Cut;
                    case LetteraKeyCode.V: return shift ? LetteraEditVerb.PastePlain : LetteraEditVerb.Paste;
                    case LetteraKeyCode.Z: return shift ? LetteraEditVerb.Redo : LetteraEditVerb.Undo;
                }
                return LetteraEditVerb.None;
            }
            if (!control) return LetteraEditVerb.None;
            // The system's own text bindings.
            switch (key)
            {
                case LetteraKeyCode.B: return Extended(LetteraEditVerb.MoveCharPrev, shift);
                case LetteraKeyCode.F: return Extended(LetteraEditVerb.MoveCharNext, shift);
                case LetteraKeyCode.A: return Extended(LetteraEditVerb.MoveLineStart, shift);
                case LetteraKeyCode.E: return Extended(LetteraEditVerb.MoveLineEnd, shift);
                case LetteraKeyCode.P: return Extended(LetteraEditVerb.MoveUp, shift);
                case LetteraKeyCode.N: return Extended(LetteraEditVerb.MoveDown, shift);
                case LetteraKeyCode.H: return LetteraEditVerb.DeleteBack;
                case LetteraKeyCode.D: return LetteraEditVerb.DeleteForward;
                case LetteraKeyCode.K: return LetteraEditVerb.DeleteToParagraphEnd;
                case LetteraKeyCode.T: return LetteraEditVerb.Transpose;
            }
            return LetteraEditVerb.None;
        }

        /// <summary>Left and right mean logical directions only in a left-to-right paragraph: the arrow keys flip in a right-to-left one.</summary>
        internal static bool FollowsParagraphDirection(LetteraKeyCode key) => key == LetteraKeyCode.Left || key == LetteraKeyCode.Right;

        internal static LetteraEditVerb Mirror(LetteraEditVerb verb)
        {
            switch (verb)
            {
                case LetteraEditVerb.MoveCharPrev: return LetteraEditVerb.MoveCharNext;
                case LetteraEditVerb.MoveCharNext: return LetteraEditVerb.MoveCharPrev;
                case LetteraEditVerb.MoveWordPrev: return LetteraEditVerb.MoveWordNext;
                case LetteraEditVerb.MoveWordNext: return LetteraEditVerb.MoveWordPrev;
                case LetteraEditVerb.ExtendCharPrev: return LetteraEditVerb.ExtendCharNext;
                case LetteraEditVerb.ExtendCharNext: return LetteraEditVerb.ExtendCharPrev;
                case LetteraEditVerb.ExtendWordPrev: return LetteraEditVerb.ExtendWordNext;
                case LetteraEditVerb.ExtendWordNext: return LetteraEditVerb.ExtendWordPrev;
                default: return verb;
            }
        }

        internal static bool IsMovement(LetteraEditVerb verb) => verb >= LetteraEditVerb.MoveCharPrev && verb <= LetteraEditVerb.ExtendDocEnd;
        internal static bool IsExtend(LetteraEditVerb verb) => verb >= LetteraEditVerb.ExtendCharPrev && verb <= LetteraEditVerb.ExtendDocEnd;

        internal static LetteraKeyCode FromUnity(KeyCode code)
        {
            if (code >= KeyCode.A && code <= KeyCode.Z) return LetteraKeyCode.A + (code - KeyCode.A);
            switch (code)
            {
                case KeyCode.Backspace: return LetteraKeyCode.Backspace;
                case KeyCode.Delete: return LetteraKeyCode.Delete;
                case KeyCode.Return: return LetteraKeyCode.Enter;
                case KeyCode.KeypadEnter: return LetteraKeyCode.KeypadEnter;
                case KeyCode.Tab: return LetteraKeyCode.Tab;
                case KeyCode.Escape: return LetteraKeyCode.Escape;
                case KeyCode.LeftArrow: return LetteraKeyCode.Left;
                case KeyCode.RightArrow: return LetteraKeyCode.Right;
                case KeyCode.UpArrow: return LetteraKeyCode.Up;
                case KeyCode.DownArrow: return LetteraKeyCode.Down;
                case KeyCode.Home: return LetteraKeyCode.Home;
                case KeyCode.End: return LetteraKeyCode.End;
                case KeyCode.PageUp: return LetteraKeyCode.PageUp;
                case KeyCode.PageDown: return LetteraKeyCode.PageDown;
                case KeyCode.Insert: return LetteraKeyCode.Insert;
                case KeyCode.Menu: return LetteraKeyCode.Menu;
                case KeyCode.F10: return LetteraKeyCode.F10;
                case KeyCode.Backslash: return LetteraKeyCode.Backslash;
                case KeyCode.Space: return LetteraKeyCode.Space;
                default: return LetteraKeyCode.None;
            }
        }
    }
}
