using System;

namespace FirstSymbol.Lettera.Unicode
{
    /// <summary>UAX #9 for one paragraph. Characters removed by X9 get the level of the preceding character.</summary>
    public static class Bidi
    {
        const int MaxDepth = 125;
        const int BracketStackSize = 63; // BD16

        sealed class Scratch
        {
            public byte[] Cls = Array.Empty<byte>(), Types = Array.Empty<byte>(), SeqTypes = Array.Empty<byte>(), Embedding = Array.Empty<byte>();
            public int[] Match = Array.Empty<int>(), Stack = Array.Empty<int>(), Map = Array.Empty<int>(), RunStart = Array.Empty<int>(),
                RunOf = Array.Empty<int>(), Seq = Array.Empty<int>(), Pair = Array.Empty<int>();
            public int MapCount;

            public void Ensure(int n)
            {
                if (Cls.Length >= n) return;
                int c = Math.Max(n, Math.Max(256, Cls.Length * 2));
                Cls = new byte[c]; Types = new byte[c]; SeqTypes = new byte[c]; Embedding = new byte[c];
                Match = new int[c]; Stack = new int[c]; Map = new int[c]; RunStart = new int[c + 1];
                RunOf = new int[c]; Seq = new int[c]; Pair = new int[c];
            }
        }

        [ThreadStatic] static Scratch s_scratch;

        const byte L = (byte)BidiClass.L, R = (byte)BidiClass.R, AL = (byte)BidiClass.AL, EN = (byte)BidiClass.EN, ES = (byte)BidiClass.ES,
            ET = (byte)BidiClass.ET, AN = (byte)BidiClass.AN, CS = (byte)BidiClass.CS, NSM = (byte)BidiClass.NSM, BN = (byte)BidiClass.BN,
            B = (byte)BidiClass.B, S = (byte)BidiClass.S, WS = (byte)BidiClass.WS, ON = (byte)BidiClass.ON, LRE = (byte)BidiClass.LRE,
            LRO = (byte)BidiClass.LRO, RLE = (byte)BidiClass.RLE, RLO = (byte)BidiClass.RLO, PDF = (byte)BidiClass.PDF, LRI = (byte)BidiClass.LRI,
            RLI = (byte)BidiClass.RLI, FSI = (byte)BidiClass.FSI, PDI = (byte)BidiClass.PDI;

        static bool IsInitiator(byte c) => c == LRI || c == RLI || c == FSI;
        static bool IsRemoved(byte c) => c == BN || c == LRE || c == RLE || c == LRO || c == RLO || c == PDF; // X9
        static bool IsNI(byte c) => c == ON || c == WS || c == S || c == B || c == PDI || IsInitiator(c);
        static int CanonicalBracket(int cp) => cp == 0x2329 ? 0x3008 : cp == 0x232A ? 0x3009 : cp; // BD16 canonical equivalents

        public static int ResolveParagraph(ReadOnlySpan<int> text, int baseLevel, Span<byte> levels)
        {
            int n = Math.Min(text.Length, levels.Length);
            var s = s_scratch ??= new Scratch();
            s.Ensure(n);
            byte[] cls = s.Cls;
            int[] match = s.Match, stack = s.Stack;

            // BD9
            int sp = 0;
            for (int i = 0; i < n; i++)
            {
                byte c = (byte)UnicodeData.Bidi(text[i]);
                cls[i] = c;
                match[i] = -1;
                if (IsInitiator(c)) stack[sp++] = i;
                else if (c == PDI && sp > 0)
                {
                    int j = stack[--sp];
                    match[j] = i;
                    match[i] = j;
                }
            }

            // P2, P3
            int para = baseLevel == 0 || baseLevel == 1 ? baseLevel : FirstStrong(cls, match, 0, n) == R ? 1 : 0;
            if (n == 0) return para;

            byte[] embedding = s.Embedding;
            ResolveExplicit(cls, match, s.Types, n, para, embedding);
            int mapCount = 0, runCount = 0;
            int[] map = s.Map, runStart = s.RunStart, runOf = s.RunOf;

            // X9, X10: level runs over the characters that remain.
            for (int i = 0; i < n; i++)
            {
                if (IsRemoved(cls[i])) continue;
                if (mapCount == 0 || embedding[map[mapCount - 1]] != embedding[i]) runStart[runCount++] = mapCount;
                runOf[i] = runCount - 1;
                map[mapCount++] = i;
            }
            runStart[runCount] = mapCount;
            s.MapCount = mapCount;

            // BD13 isolating run sequences
            for (int r = 0; r < runCount; r++)
            {
                int first = map[runStart[r]];
                if (cls[first] == PDI && match[first] >= 0) continue;
                ResolveSequence(s, text, r, para, levels);
            }

            byte prev = (byte)para;
            for (int i = 0; i < n; i++)
            {
                if (IsRemoved(cls[i])) levels[i] = prev;
                else prev = levels[i];
            }
            return para;
        }

        static byte FirstStrong(byte[] cls, int[] match, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                byte c = cls[i];
                if (c == L) return L;
                if (c == R || c == AL) return R;
                if (c == B) break;
                if (IsInitiator(c))
                {
                    if (match[i] < 0) break;
                    i = match[i];
                }
            }
            return ON;
        }

        // X1-X8
        static void ResolveExplicit(byte[] cls, int[] match, byte[] types, int n, int para, byte[] levels)
        {
            Span<byte> stLevel = stackalloc byte[MaxDepth + 2];
            Span<byte> stOverride = stackalloc byte[MaxDepth + 2];
            Span<bool> stIsolate = stackalloc bool[MaxDepth + 2];
            int top = 0, overflowIsolate = 0, overflowEmbedding = 0, validIsolate = 0;
            stLevel[0] = (byte)para;
            stOverride[0] = ON;
            stIsolate[0] = false;

            for (int i = 0; i < n; i++)
            {
                byte c = cls[i];
                types[i] = c;
                switch ((BidiClass)c)
                {
                    case BidiClass.RLE: case BidiClass.LRE: case BidiClass.RLO: case BidiClass.LRO: // X2-X5
                    {
                        levels[i] = stLevel[top];
                        int cur = stLevel[top];
                        int next = c == RLE || c == RLO ? (cur + 1) | 1 : (cur + 2) & ~1;
                        if (next <= MaxDepth && overflowIsolate == 0 && overflowEmbedding == 0)
                        {
                            top++;
                            stLevel[top] = (byte)next;
                            stOverride[top] = c == RLO ? R : c == LRO ? L : ON;
                            stIsolate[top] = false;
                        }
                        else if (overflowIsolate == 0) overflowEmbedding++;
                        break;
                    }
                    case BidiClass.RLI: case BidiClass.LRI: case BidiClass.FSI: // X5a-X5c
                    {
                        levels[i] = stLevel[top];
                        if (stOverride[top] != ON) types[i] = stOverride[top];
                        bool rtl = c == RLI;
                        if (c == FSI) rtl = FirstStrong(cls, match, i + 1, match[i] < 0 ? n : match[i]) == R;
                        int cur = stLevel[top];
                        int next = rtl ? (cur + 1) | 1 : (cur + 2) & ~1;
                        if (next <= MaxDepth && overflowIsolate == 0 && overflowEmbedding == 0)
                        {
                            validIsolate++;
                            top++;
                            stLevel[top] = (byte)next;
                            stOverride[top] = ON;
                            stIsolate[top] = true;
                        }
                        else overflowIsolate++;
                        break;
                    }
                    case BidiClass.PDI: // X6a
                        if (overflowIsolate > 0) overflowIsolate--;
                        else if (validIsolate > 0)
                        {
                            overflowEmbedding = 0;
                            while (!stIsolate[top]) top--;
                            top--;
                            validIsolate--;
                        }
                        levels[i] = stLevel[top];
                        if (stOverride[top] != ON) types[i] = stOverride[top];
                        break;
                    case BidiClass.PDF: // X7
                        levels[i] = stLevel[top];
                        if (overflowIsolate > 0) { }
                        else if (overflowEmbedding > 0) overflowEmbedding--;
                        else if (!stIsolate[top] && top >= 1) top--;
                        break;
                    case BidiClass.B: // X8
                        levels[i] = (byte)para;
                        top = 0;
                        overflowIsolate = overflowEmbedding = validIsolate = 0;
                        break;
                    case BidiClass.BN:
                        levels[i] = stLevel[top];
                        break;
                    default: // X6
                        levels[i] = stLevel[top];
                        if (stOverride[top] != ON) types[i] = stOverride[top];
                        break;
                }
            }
        }

        static void ResolveSequence(Scratch s, ReadOnlySpan<int> text, int headRun, int para, Span<byte> levels)
        {
            byte[] cls = s.Cls, st = s.SeqTypes, emb = s.Embedding;
            int[] seq = s.Seq, map = s.Map, runStart = s.RunStart, match = s.Match, runOf = s.RunOf;

            int m = 0, run = headRun;
            while (true)
            {
                for (int k = runStart[run], e = runStart[run + 1]; k < e; k++) seq[m++] = map[k];
                int last = seq[m - 1];
                if (!IsInitiator(cls[last]) || match[last] < 0) break;
                run = runOf[match[last]];
            }

            int level = emb[seq[0]];
            byte embedding = (level & 1) == 0 ? L : R;

            // X10 sos / eos
            int firstMapped = runStart[headRun];
            int prevLevel = firstMapped > 0 ? emb[map[firstMapped - 1]] : para;
            int lastChar = seq[m - 1];
            int nextMapped = runStart[run + 1];
            int nextLevel = IsInitiator(cls[lastChar]) || nextMapped >= s.MapCount ? para : emb[map[nextMapped]];
            byte sos = (Math.Max(level, prevLevel) & 1) == 0 ? L : R;
            byte eos = (Math.Max(level, nextLevel) & 1) == 0 ? L : R;

            byte[] types = s.Types;
            for (int p = 0; p < m; p++) st[p] = types[seq[p]];

            // W1
            byte prev = sos;
            for (int p = 0; p < m; p++)
            {
                if (st[p] == NSM) st[p] = IsInitiator(prev) || prev == PDI ? ON : prev;
                prev = st[p];
            }

            // W2, W3
            byte strong = sos;
            for (int p = 0; p < m; p++)
            {
                byte c = st[p];
                if (c == L || c == R || c == AL) strong = c;
                else if (c == EN && strong == AL) st[p] = AN;
                if (c == AL) st[p] = R;
            }

            // W4
            for (int p = 1; p < m - 1; p++)
            {
                byte c = st[p];
                if (c == ES && st[p - 1] == EN && st[p + 1] == EN) st[p] = EN;
                else if (c == CS && st[p - 1] == st[p + 1] && (st[p - 1] == EN || st[p - 1] == AN)) st[p] = st[p - 1];
            }

            // W5
            for (int p = 0; p < m; p++)
            {
                if (st[p] != ET) continue;
                int e = p + 1;
                while (e < m && st[e] == ET) e++;
                if ((p > 0 && st[p - 1] == EN) || (e < m && st[e] == EN))
                    for (int q = p; q < e; q++) st[q] = EN;
                p = e - 1;
            }

            // W6, W7
            strong = sos;
            for (int p = 0; p < m; p++)
            {
                byte c = st[p];
                if (c == ES || c == ET || c == CS) st[p] = ON;
                else if (c == L || c == R) strong = c;
                else if (c == EN && strong == L) st[p] = L;
            }

            ResolveBrackets(s, text, m, sos, embedding);

            // N1, N2
            for (int p = 0; p < m; p++)
            {
                if (!IsNI(st[p])) continue;
                int e = p + 1;
                while (e < m && IsNI(st[e])) e++;
                byte lead = p == 0 ? sos : StrongOf(st[p - 1]);
                byte trail = e == m ? eos : StrongOf(st[e]);
                byte dir = lead == trail ? lead : embedding;
                for (int q = p; q < e; q++) st[q] = dir;
                p = e - 1;
            }

            // I1, I2
            for (int p = 0; p < m; p++)
            {
                byte c = st[p];
                int v = level;
                if ((level & 1) == 0)
                {
                    if (c == R) v += 1;
                    else if (c == AN || c == EN) v += 2;
                }
                else if (c == L || c == AN || c == EN) v += 1;
                levels[seq[p]] = (byte)v;
            }
        }

        static byte StrongOf(byte c) => c == L ? L : R; // EN, AN count as R in N0-N2

        // BD16, N0
        static void ResolveBrackets(Scratch s, ReadOnlySpan<int> text, int m, byte sos, byte embedding)
        {
            byte[] st = s.SeqTypes, cls = s.Cls;
            int[] seq = s.Seq, pair = s.Pair;
            Span<int> closers = stackalloc int[BracketStackSize];
            Span<int> positions = stackalloc int[BracketStackSize];
            int sp = 0;
            bool any = false;

            for (int p = 0; p < m; p++) pair[p] = -1;
            for (int p = 0; p < m; p++)
            {
                if (st[p] != ON) continue;
                int cp = text[seq[p]];
                var type = UnicodeData.Bracket(cp);
                if (type == BidiPairedBracketType.Open)
                {
                    if (sp == BracketStackSize)
                    {
                        if (any) for (int q = 0; q < m; q++) pair[q] = -1;
                        return;
                    }
                    closers[sp] = CanonicalBracket(UnicodeData.Mirror(cp));
                    positions[sp++] = p;
                }
                else if (type == BidiPairedBracketType.Close)
                {
                    int canon = CanonicalBracket(cp);
                    for (int k = sp - 1; k >= 0; k--)
                    {
                        if (closers[k] != canon) continue;
                        pair[positions[k]] = p;
                        any = true;
                        sp = k;
                        break;
                    }
                }
            }
            if (!any) return;

            byte opposite = embedding == L ? R : L;
            byte context = sos;
            for (int p = 0; p < m; p++)
            {
                int close = pair[p];
                if (close >= 0)
                {
                    bool foundEmbedding = false, foundOpposite = false;
                    for (int q = p + 1; q < close; q++)
                    {
                        byte c = st[q];
                        if (c == L || c == R || c == EN || c == AN)
                        {
                            if (StrongOf(c) == embedding) { foundEmbedding = true; break; }
                            foundOpposite = true;
                        }
                    }
                    if (foundEmbedding || foundOpposite)
                    {
                        byte dir = foundEmbedding ? embedding : context == opposite ? opposite : embedding; // N0 b, c
                        st[p] = dir;
                        st[close] = dir;
                        for (int q = p + 1; q < m && cls[seq[q]] == NSM; q++) st[q] = dir;
                        for (int q = close + 1; q < m && cls[seq[q]] == NSM; q++) st[q] = dir;
                    }
                }
                byte t = st[p];
                if (t == L || t == R || t == EN || t == AN) context = StrongOf(t);
            }
        }

        public static void ResolveLineLevels(ReadOnlySpan<int> text, ReadOnlySpan<byte> paragraphLevels, int start, int end, int paragraphLevel, Span<byte> lineLevels)
        {
            if (start < 0) start = 0;
            end = Math.Min(end, Math.Min(text.Length, paragraphLevels.Length));
            int count = Math.Min(end - start, lineLevels.Length);
            bool trailing = true;
            for (int k = count - 1; k >= 0; k--)
            {
                byte c = (byte)UnicodeData.Bidi(text[start + k]);
                byte v = paragraphLevels[start + k];
                if (c == S || c == B) { v = (byte)paragraphLevel; trailing = true; } // L1
                else if (c == WS || c == PDI || IsInitiator(c) || IsRemoved(c)) { if (trailing) v = (byte)paragraphLevel; }
                else trailing = false;
                lineLevels[k] = v;
            }
        }

        public static void ReorderLine(ReadOnlySpan<byte> lineLevels, Span<int> visualToLogical)
        {
            int n = Math.Min(lineLevels.Length, visualToLogical.Length);
            int max = 0, minOdd = int.MaxValue;
            for (int i = 0; i < n; i++)
            {
                visualToLogical[i] = i;
                int v = lineLevels[i];
                if (v > max) max = v;
                if ((v & 1) != 0 && v < minOdd) minOdd = v;
            }
            // L2
            for (int level = max; level >= minOdd; level--)
            {
                for (int i = 0; i < n; i++)
                {
                    if (lineLevels[visualToLogical[i]] < level) continue;
                    int e = i + 1;
                    while (e < n && lineLevels[visualToLogical[e]] >= level) e++;
                    for (int a = i, b = e - 1; a < b; a++, b--)
                    {
                        int t = visualToLogical[a];
                        visualToLogical[a] = visualToLogical[b];
                        visualToLogical[b] = t;
                    }
                    i = e;
                }
            }
        }
    }
}
