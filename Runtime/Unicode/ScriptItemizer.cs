using System;

namespace FirstSymbol.Lettera.Unicode
{
    public static class ScriptItemizer
    {
        const int BracketStackSize = 64;

        static bool IsNeutral(int script) => script == (int)UnicodeScript.Common || script == (int)UnicodeScript.Inherited;
        static int CanonicalBracket(int cp) => cp == 0x2329 ? 0x3008 : cp == 0x232A ? 0x3009 : cp;

        public static void Resolve(ReadOnlySpan<int> text, Span<UnicodeScript> output)
        {
            int n = Math.Min(text.Length, output.Length);
            if (n <= 0) return;

            // Backward pass: output[i] holds the next non-neutral script at or after i.
            var following = UnicodeScript.Common;
            for (int i = n - 1; i >= 0; i--)
            {
                int sc = UnicodeData.Script(text[i]);
                if (!IsNeutral(sc)) following = (UnicodeScript)sc;
                output[i] = following;
            }

            Span<int> closers = stackalloc int[BracketStackSize];
            Span<ushort> scripts = stackalloc ushort[BracketStackSize];
            int sp = 0;
            int current = -1; // no run yet

            for (int i = 0; i < n; i++)
            {
                int cp = text[i];
                int next = (int)output[i];
                int sc = UnicodeData.Script(cp);
                int set = UnicodeData.ScxSet(cp);
                var bracket = UnicodeData.Bracket(cp);
                int resolved;

                if (bracket == BidiPairedBracketType.Close && TryPopOpening(closers, scripts, ref sp, CanonicalBracket(cp), out int opening))
                    resolved = opening;
                else if (!IsNeutral(sc) && set == 0)
                    resolved = sc;
                else if (set != 0)
                {
                    // Script_Extensions: stay in the run if allowed, else move to the following run if allowed.
                    if (current >= 0 && UnicodeData.ScxContains(set, current)) resolved = current;
                    else if (UnicodeData.ScxContains(set, next)) resolved = next;
                    else if (!IsNeutral(sc)) resolved = sc;
                    else resolved = current >= 0 ? current : next;
                }
                else
                    resolved = current >= 0 ? current : next;

                if (bracket == BidiPairedBracketType.Open)
                {
                    if (sp == BracketStackSize)
                    {
                        // Drop the oldest entry so that recent pairs still match.
                        closers.Slice(1).CopyTo(closers);
                        scripts.Slice(1).CopyTo(scripts);
                        sp--;
                    }
                    closers[sp] = CanonicalBracket(UnicodeData.Mirror(cp));
                    scripts[sp++] = (ushort)resolved;
                }

                output[i] = (UnicodeScript)resolved;
                // A leading neutral resolved from the following run does not open a run on Common.
                if (!IsNeutral(resolved)) current = resolved;
            }
        }

        static bool TryPopOpening(Span<int> closers, Span<ushort> scripts, ref int sp, int closer, out int script)
        {
            for (int k = sp - 1; k >= 0; k--)
            {
                if (closers[k] != closer) continue;
                script = scripts[k];
                sp = k;
                return true;
            }
            script = 0;
            return false;
        }
    }
}
