using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>The names of what a clipboard can hold.</summary>
    public static class LetteraClipFormat
    {
        public const string PlainText = "text/plain", Html = "text/html", Markdown = "text/markdown", Lettera = "application/x-lettera-text+json", Url = "text/uri-list", Png = "image/png", Jpeg = "image/jpeg", Gif = "image/gif", Files = "text/x-lettera-file-list";
    }

    /// <summary>One format of a clipboard's content.</summary>
    public struct LetteraClipItem
    {
        public string Format;
        public byte[] Bytes;

        public LetteraClipItem(string format, byte[] bytes)
        {
            Format = format;
            Bytes = bytes;
        }

        public LetteraClipItem(string format, string text)
        {
            Format = format;
            Bytes = Encoding.UTF8.GetBytes(text ?? "");
        }

        public string Text => Bytes != null ? Encoding.UTF8.GetString(Bytes) : "";
    }

    /// <summary>A way to reach a clipboard. A write puts all formats in at once.</summary>
    public interface ILetteraClipboardBackend
    {
        void Write(IReadOnlyList<LetteraClipItem> items);

        /// <summary>Adds what the clipboard holds.</summary>
        void Read(List<LetteraClipItem> items);

        /// <summary>A browser can only read later: the callback gets what the clipboard holds.</summary>
        void ReadAsync(Action<List<LetteraClipItem>> done);
    }

    /// <summary>
    /// The system clipboard as Unity offers it, which is plain text. The last copy's other formats are kept in the process, and
    /// count when the system's text is still that copy's: so styled text survives between fields, but never leaves the app.
    /// </summary>
    public sealed class LetteraSystemClipboard : ILetteraClipboardBackend
    {
        readonly List<LetteraClipItem> m_Shadow = new List<LetteraClipItem>();
        string m_ShadowPlain;

        public void Write(IReadOnlyList<LetteraClipItem> items)
        {
            m_Shadow.Clear();
            m_ShadowPlain = "";
            foreach (LetteraClipItem item in items)
            {
                m_Shadow.Add(item);
                if (item.Format == LetteraClipFormat.PlainText) m_ShadowPlain = item.Text;
            }
            GUIUtility.systemCopyBuffer = m_ShadowPlain;
        }

        public void Read(List<LetteraClipItem> items)
        {
            string plain = GUIUtility.systemCopyBuffer ?? "";
            if (m_ShadowPlain != null && NormalizedEquals(plain, m_ShadowPlain))
            {
                items.AddRange(m_Shadow);
                return;
            }
            if (plain.Length > 0) items.Add(new LetteraClipItem(LetteraClipFormat.PlainText, plain));
        }

        // A system may give line endings back in its own form.
        static bool NormalizedEquals(string a, string b) => a == b || a.Replace("\r\n", "\n") == b.Replace("\r\n", "\n");

        public void ReadAsync(Action<List<LetteraClipItem>> done)
        {
            var items = new List<LetteraClipItem>();
            Read(items);
            done(items);
        }
    }

    /// <summary>Writes and reads text with its formats.</summary>
    public static class LetteraClipboard
    {
        static ILetteraClipboardBackend s_Backend;

        /// <summary>Where copies go. Replace it for a platform or a test.</summary>
        public static ILetteraClipboardBackend Backend
        {
            get => s_Backend ?? (s_Backend = LetteraPlatformClipboard.CreateDefault() ?? new LetteraSystemClipboard());
            set => s_Backend = value;
        }

        /// <summary>Raised after Lettera wrote to the clipboard.</summary>
        public static event Action Written;

        internal static void Write(IReadOnlyList<LetteraClipItem> items)
        {
            Backend.Write(items);
            Written?.Invoke();
        }

        /// <summary>True when the clipboard holds text.</summary>
        public static bool HasText
        {
            get
            {
                // Reading could show a system's notice, so a clipboard that can tell without reading is asked first. Not knowing counts as yes: the Paste item shows.
                if (Backend is ILetteraClipboardProbe probe)
                {
                    LetteraClipPresence presence = probe.Probe();
                    return presence == LetteraClipPresence.Unknown || presence == LetteraClipPresence.Text || presence == LetteraClipPresence.Rich;
                }
                var items = new List<LetteraClipItem>();
                Backend.Read(items);
                foreach (LetteraClipItem item in items)
                    if (item.Format == LetteraClipFormat.PlainText || item.Format == LetteraClipFormat.Html || item.Format == LetteraClipFormat.Markdown || item.Format == LetteraClipFormat.Lettera)
                        return true;
                return false;
            }
        }

        /// <summary>True when a paste would carry more than plain text.</summary>
        public static bool HasRichText
        {
            get
            {
                if (Backend is ILetteraClipboardProbe probe) return probe.Probe() == LetteraClipPresence.Rich;
                var items = new List<LetteraClipItem>();
                Backend.Read(items);
                foreach (LetteraClipItem item in items)
                    if (item.Format == LetteraClipFormat.Html || item.Format == LetteraClipFormat.Markdown || item.Format == LetteraClipFormat.Lettera)
                        return true;
                return false;
            }
        }

        internal static void WriteSelection(LetteraSelectable selectable)
        {
            if (selectable.Selection.IsCaret) return;
            Write(SelectionItems(selectable));
        }

        internal static List<LetteraClipItem> SelectionItems(LetteraSelectable selectable)
        {
            LetteraSelectionRange s = selectable.Selection;
            TextEngine engine = selectable.Engine;
            engine.EnsureParsed();
            string plain = selectable.SelectedPlainText();
            var items = new List<LetteraClipItem>(4) { new LetteraClipItem(LetteraClipFormat.PlainText, plain) };
            // Rich formats carry the source, so text under a cover goes out only as what plain text made of it.
            // An inline item goes as its words too, since its spans would not line up with them.
            bool literal = engine.Literal || engine.RevealSource >= 0 || selectable.ConcealsSelection || engine.SpanHasInline(s.Start, s.End);
            if (!literal)
            {
                var spans = new List<LetteraPastedSpan>();
                CollectSpans(engine, s.Start, s.End, spans);
                items.Add(new LetteraClipItem(LetteraClipFormat.Lettera, LetteraTextCodec.Write(plain, spans)));
                items.Add(new LetteraClipItem(LetteraClipFormat.Html, LetteraHtmlCodec.Write(plain, spans)));
                items.Add(new LetteraClipItem(LetteraClipFormat.Markdown, LetteraMarkdownCodec.Write(plain, spans)));
            }
            return items;
        }

        static void CollectSpans(TextEngine engine, int start, int end, List<LetteraPastedSpan> spans)
        {
            IReadOnlyList<LetteraMatchContext.RangeRecord> ranges = engine.ParsedRanges;
            for (int r = 0; r < ranges.Count; r++)
            {
                LetteraMatchContext.RangeRecord range = ranges[r];
                int a = Math.Max(range.RenderedStart, start), b = Math.Min(range.RenderedEnd, end);
                if (a >= b) continue;
                LetteraEffect effect = engine.EffectiveStyles[range.StyleIndex]?.Effect;
                if (effect == null || range.WholeText) continue;
                spans.Add(new LetteraPastedSpan { Start = a - start, End = b - start, EffectKey = LetteraClipSchemas.KeyOf(effect), Raw = range.Raw ?? range.Primary, Label = range.Label });
            }
        }
    }

    /// <summary>How an effect shows up in formats other than Lettera's own.</summary>
    public sealed class LetteraClipSchema
    {
        /// <summary>Elements that mean this effect, the first of which is written.</summary>
        public string[] HtmlElements;

        /// <summary>A CSS property on a span, for effects with a value.</summary>
        public string CssProperty;
        public Func<string, string> ToCss;
        public Func<string, string> FromCss;

        /// <summary>For an effect with several elements: which one a value writes, and what value one reads as.</summary>
        public Func<string, string> HtmlElementFromRaw, RawFromElement;

        /// <summary>An attribute that holds the parameter, such as href.</summary>
        public string HtmlAttribute;
        public string MarkdownMarker;
        public bool MarkdownLink;
    }

    /// <summary>What each effect type means on the clipboard. Registration is explicit, so code stripping cannot affect it.</summary>
    public static class LetteraClipSchemas
    {
        static readonly Dictionary<string, LetteraClipSchema> s_Schemas = new Dictionary<string, LetteraClipSchema>();

        static LetteraClipSchemas()
        {
            s_Schemas[typeof(SuperSubEffect).FullName] = new LetteraClipSchema { HtmlElements = new[] { "sup", "sub" }, HtmlElementFromRaw = raw => raw == "sub" ? "sub" : "sup", RawFromElement = element => element };
            s_Schemas[typeof(SuperSubEffect).FullName + ":Super"] = new LetteraClipSchema { HtmlElements = new[] { "sup" } };
            s_Schemas[typeof(SuperSubEffect).FullName + ":Sub"] = new LetteraClipSchema { HtmlElements = new[] { "sub" } };
            Register<BoldEffect>(new LetteraClipSchema { HtmlElements = new[] { "b", "strong" }, CssProperty = "font-weight", ToCss = _ => "700", FromCss = v => IsBoldWeight(v) ? "" : null, MarkdownMarker = "**" });
            Register<ItalicEffect>(new LetteraClipSchema { HtmlElements = new[] { "i", "em" }, CssProperty = "font-style", ToCss = _ => "italic", FromCss = v => v == "italic" || v == "oblique" ? "" : null, MarkdownMarker = "*" });
            Register<UnderlineEffect>(new LetteraClipSchema { HtmlElements = new[] { "u" }, CssProperty = "text-decoration", ToCss = _ => "underline", FromCss = v => v.Contains("underline") ? "" : null });
            Register<StrikethroughEffect>(new LetteraClipSchema { HtmlElements = new[] { "s", "strike", "del" }, CssProperty = "text-decoration", ToCss = _ => "line-through", FromCss = v => v.Contains("line-through") ? "" : null, MarkdownMarker = "~~" });
            Register<ColorEffect>(new LetteraClipSchema { CssProperty = "color", ToCss = v => v, FromCss = NormalizeColor });
            Register<SizeEffect>(new LetteraClipSchema { CssProperty = "font-size", ToCss = v => LengthToCss(v), FromCss = LengthFromCss });
            Register<LinkEffect>(new LetteraClipSchema { HtmlElements = new[] { "a" }, HtmlAttribute = "href", MarkdownLink = true });
                        Register<UppercaseEffect>(new LetteraClipSchema { CssProperty = "text-transform", ToCss = _ => "uppercase", FromCss = v => v == "uppercase" ? "" : null });
            Register<LowercaseEffect>(new LetteraClipSchema { CssProperty = "text-transform", ToCss = _ => "lowercase", FromCss = v => v == "lowercase" ? "" : null });
        }

        /// <summary>Tells how an effect type shows in HTML and Markdown. A repeat does nothing.</summary>
        public static void Register<T>(LetteraClipSchema schema) where T : LetteraEffect
        {
            string key = typeof(T).FullName;
            if (!s_Schemas.ContainsKey(key)) s_Schemas[key] = schema;
        }

        internal static IEnumerable<KeyValuePair<string, LetteraClipSchema>> All => s_Schemas;
        internal static LetteraClipSchema Find(string key) => key != null && s_Schemas.TryGetValue(key, out LetteraClipSchema schema) ? schema : null;

        /// <summary>An effect's identity on the clipboard: its type, or for a composite its ordered children.</summary>
        public static string KeyOf(LetteraEffect effect)
        {
            if (effect is CompositeEffect)
            {
                var children = new List<LetteraEffect>();
                effect.CollectChildren(children);
                foreach (LetteraEffect child in children)
                    if (child is LinkEffect)
                        return typeof(LinkEffect).FullName;
                var builder = new StringBuilder();
                foreach (LetteraEffect child in children)
                {
                    if (builder.Length > 0) builder.Append('+');
                    builder.Append(KeyOf(child));
                }
                return builder.ToString();
            }
            if (effect is SuperSubEffect placed) return effect.GetType().FullName + ":" + placed.Placement;
            return effect.GetType().FullName;
        }

        static bool IsBoldWeight(string value)
        {
            if (value == "bold" || value == "bolder") return true;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float weight) && weight >= 700;
        }

        internal static string NormalizeColor(string value)
        {
            value = value.Trim().ToLowerInvariant();
            if (value.StartsWith("#", StringComparison.Ordinal)) return value.ToUpperInvariant();
            if (value.StartsWith("rgb", StringComparison.Ordinal))
            {
                int open = value.IndexOf('('), close = value.IndexOf(')');
                if (open < 0 || close < open) return null;
                string[] parts = value.Substring(open + 1, close - open - 1).Split(',', '/', ' ');
                var numbers = new List<float>(4);
                foreach (string part in parts)
                {
                    string p = part.Trim();
                    if (p.Length == 0) continue;
                    if (!float.TryParse(p.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out float n)) return null;
                    numbers.Add(p.EndsWith("%", StringComparison.Ordinal) ? n * 2.55f : n);
                }
                if (numbers.Count < 3) return null;
                int a = numbers.Count > 3 ? Mathf.RoundToInt((numbers[3] <= 1 ? numbers[3] * 255 : numbers[3])) : 255;
                return "#" + Hex(numbers[0]) + Hex(numbers[1]) + Hex(numbers[2]) + (a < 255 ? Hex(a) : "");
            }
            return LetteraSlots.TryColor(value, out Color32 named) ? "#" + Hex(named.r) + Hex(named.g) + Hex(named.b) : null;
        }

        static string Hex(float value) => Mathf.Clamp(Mathf.RoundToInt(value), 0, 255).ToString("X2", CultureInfo.InvariantCulture);

        static string LengthToCss(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.EndsWith("%", StringComparison.Ordinal) || value.EndsWith("em", StringComparison.Ordinal) || value.EndsWith("px", StringComparison.Ordinal)) return value;
            return value + "px";
        }

        // pt as 4/3 px, rem from a root of 16. Other units stay relative.
        internal static string LengthFromCss(string value)
        {
            value = value.Trim().ToLowerInvariant();
            if (value.EndsWith("pt", StringComparison.Ordinal) && float.TryParse(value.Substring(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out float pt)) return (pt * 4f / 3f).ToString("0.###", CultureInfo.InvariantCulture);
            if (value.EndsWith("rem", StringComparison.Ordinal) && float.TryParse(value.Substring(0, value.Length - 3), NumberStyles.Float, CultureInfo.InvariantCulture, out float rem)) return (rem * 16f).ToString("0.###", CultureInfo.InvariantCulture);
            if (value.EndsWith("px", StringComparison.Ordinal)) return value.Substring(0, value.Length - 2);
            if (value.EndsWith("em", StringComparison.Ordinal) || value.EndsWith("%", StringComparison.Ordinal)) return value;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? value : null;
        }
    }

    /// <summary>Text and its ranges, parsed from a format.</summary>
    public sealed class LetteraParsedClip
    {
        public string Text = "";
        public readonly List<LetteraPastedSpan> Spans = new List<LetteraPastedSpan>();
    }

    /// <summary>Lettera's own format: JSON with the plain text and the exact ranges.</summary>
    public static class LetteraTextCodec
    {
        [Serializable] class Doc { public int version = 1; public string text; public List<Span> spans = new List<Span>(); }
        [Serializable] class Span { public int start, end; public string effect, raw, label; public bool hasRaw; }

        public static string Write(string plain, List<LetteraPastedSpan> spans)
        {
            var doc = new Doc { text = plain };
            foreach (LetteraPastedSpan s in spans) doc.spans.Add(new Span { start = s.Start, end = s.End, effect = s.EffectKey, raw = s.Raw ?? "", hasRaw = s.Raw != null, label = s.Label ?? "" });
            return JsonUtility.ToJson(doc);
        }

        /// <summary>False for what is not this format, or a later version of it.</summary>
        public static bool TryRead(string json, LetteraParsedClip clip)
        {
            Doc doc;
            try
            {
                doc = JsonUtility.FromJson<Doc>(json);
            }
            catch (ArgumentException)
            {
                return false;
            }
            if (doc == null || doc.version != 1 || doc.text == null) return false;
            clip.Text = doc.text;
            foreach (Span s in doc.spans)
                if (s.end > s.start)
                    clip.Spans.Add(new LetteraPastedSpan { Start = s.start, End = s.end, EffectKey = s.effect, Raw = s.hasRaw ? s.raw : null, Label = string.IsNullOrEmpty(s.label) ? null : s.label });
            return true;
        }
    }

    /// <summary>Reads and writes HTML: the formatting people mean, without the page around it. Linear in its input.</summary>
    public static class LetteraHtmlCodec
    {
        const int MaxDepth = 64;
        const string OwnAttribute = "data-lettera";

        /// <summary>Puts Lettera's own format on an attribute, where a browser has no way to carry a format of its own.</summary>
        internal static string CarryOwnFormat(string html, string own) => "<span " + OwnAttribute + "=\"" + Convert.ToBase64String(Encoding.UTF8.GetBytes(own)) + "\">" + html + "</span>";

        /// <summary>
        /// Lettera's own format from that attribute. It counts only when it says the same text the HTML shows,
        /// so what is pasted is always what could be seen. Hostile input stays within the HTML's own limit.
        /// </summary>
        internal static bool TryReadCarried(string html, LetteraParsedClip clip)
        {
            int at = html.IndexOf(OwnAttribute + "=\"", StringComparison.Ordinal);
            if (at < 0) return false;
            at += OwnAttribute.Length + 2;
            int end = html.IndexOf('"', at);
            if (end < 0) return false;
            byte[] data;
            try
            {
                data = Convert.FromBase64String(html.Substring(at, end - at));
            }
            catch (FormatException)
            {
                return false;
            }
            var carried = new LetteraParsedClip();
            if (!LetteraTextCodec.TryRead(Encoding.UTF8.GetString(data), carried)) return false;
            if (Comparable(carried.Text) != Comparable(Read(html).Text)) return false;
            clip.Text = carried.Text;
            clip.Spans.AddRange(carried.Spans);
            return true;
        }

        // HTML loses runs of spaces and tells line breaks in its own ways, so only the words are compared.
        static string Comparable(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (char c in text)
                if (!char.IsWhiteSpace(c))
                    builder.Append(c);
            return builder.ToString();
        }

        static void Escape(StringBuilder builder, string text, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '&': builder.Append("&amp;"); break;
                    case '<': builder.Append("&lt;"); break;
                    case '>': builder.Append("&gt;"); break;
                    case '"': builder.Append("&quot;"); break;
                    case (char)0x2028: builder.Append("<br>"); break;
                    default: builder.Append(c); break;
                }
            }
        }

        public static string Write(string plain, List<LetteraPastedSpan> spans)
        {
            var builder = new StringBuilder(plain.Length + 32);
            // Cut at every range edge, then wrap each piece in everything over it: always valid, however ranges overlap.
            var cuts = new SortedSet<int> { 0 };
            int cps = 0;
            var offsets = new List<int>(plain.Length + 1);
            for (int i = 0; i < plain.Length; i++)
            {
                offsets.Add(i);
                cps++;
                if (char.IsHighSurrogate(plain[i]) && i + 1 < plain.Length) i++;
            }
            offsets.Add(plain.Length);
            cuts.Add(cps);
            foreach (LetteraPastedSpan s in spans)
            {
                cuts.Add(Mathf.Clamp(s.Start, 0, cps));
                cuts.Add(Mathf.Clamp(s.End, 0, cps));
            }
            for (int i = 0; i < cps; i++)
                if (plain[offsets[i]] == '\n')
                {
                    cuts.Add(i);
                    cuts.Add(i + 1);
                }

            builder.Append("<p>");
            int previous = -1;
            var close = new List<string>();
            foreach (int cut in cuts)
            {
                if (previous >= 0 && cut > previous)
                {
                    if (plain[offsets[previous]] == '\n')
                    {
                        builder.Append("</p><p>");
                    }
                    else
                    {
                        close.Clear();
                        foreach (LetteraPastedSpan s in spans)
                        {
                            if (s.Start > previous || s.End < cut) continue;
                            LetteraClipSchema schema = LetteraClipSchemas.Find(s.EffectKey);
                            if (schema == null) continue;
                            OpenElement(builder, schema, s, close);
                        }
                        Escape(builder, plain, offsets[previous], offsets[cut]);
                        for (int c = close.Count - 1; c >= 0; c--) builder.Append(close[c]);
                    }
                }
                previous = cut;
            }
            builder.Append("</p>");
            return builder.ToString();
        }

        static void OpenElement(StringBuilder builder, LetteraClipSchema schema, in LetteraPastedSpan span, List<string> close)
        {
            if (schema.HtmlElements != null && schema.HtmlElements.Length > 0)
            {
                string element = schema.HtmlElementFromRaw != null ? schema.HtmlElementFromRaw(span.Raw) : schema.HtmlElements[0];
                builder.Append('<').Append(element);
                if (schema.HtmlAttribute != null)
                {
                    builder.Append(' ').Append(schema.HtmlAttribute).Append("=\"");
                    string value = span.Raw ?? "";
                    Escape(builder, value, 0, value.Length);
                    builder.Append('"');
                }
                builder.Append('>');
                close.Add("</" + element + ">");
            }
            else if (schema.CssProperty != null)
            {
                string css = schema.ToCss != null ? schema.ToCss(span.Raw ?? "") : span.Raw;
                builder.Append("<span style=\"").Append(schema.CssProperty).Append(':');
                Escape(builder, css ?? "", 0, (css ?? "").Length);
                builder.Append("\">");
                close.Add("</span>");
            }
        }

        struct Open
        {
            public int NameHash;
            public string Name;
            public int SpanCount;
            public bool Pre, Block;
        }

        static readonly HashSet<string> s_Block = new HashSet<string> { "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "table", "tr", "blockquote", "section", "article", "header", "footer", "pre" };
        static readonly HashSet<string> s_Dropped = new HashSet<string> { "style", "script", "head", "title", "meta", "template", "xml" };
        static readonly HashSet<string> s_Void = new HashSet<string> { "br", "hr", "img", "meta", "link", "input", "col", "area", "base", "embed", "source", "track", "wbr" };

        /// <summary>The text and ranges of an HTML fragment. Windows' fragment markers count when present.</summary>
        public static LetteraParsedClip Read(string html)
        {
            var clip = new LetteraParsedClip();
            int from = 0, to = html.Length;
            int startMark = html.IndexOf("<!--StartFragment", StringComparison.OrdinalIgnoreCase);
            if (startMark >= 0)
            {
                int after = html.IndexOf("-->", startMark, StringComparison.Ordinal);
                int endMark = html.IndexOf("<!--EndFragment", StringComparison.OrdinalIgnoreCase);
                if (after >= 0 && endMark > after)
                {
                    from = after + 3;
                    to = endMark;
                }
            }

            var text = new StringBuilder(Math.Min(to - from, 1024));
            var stack = new List<Open>();
            var openSpans = new List<(int stackDepth, LetteraPastedSpan span)>();
            int cps = 0;
            bool pendingSpace = false, atLineStart = true, pendingBreak = false, anyText = false;
            int preDepth = 0;

            void Emit(int cp)
            {
                text.Append(cp > 0xFFFF ? char.ConvertFromUtf32(cp) : ((char)cp).ToString());
                cps++;
            }

            void EmitText(string decoded)
            {
                for (int k = 0; k < decoded.Length; k++)
                {
                    char c = decoded[k];
                    int cp = c;
                    if (char.IsHighSurrogate(c) && k + 1 < decoded.Length && char.IsLowSurrogate(decoded[k + 1])) cp = char.ConvertToUtf32(c, decoded[++k]);
                    if (preDepth == 0 && (cp == ' ' || cp == '\t' || cp == '\n' || cp == '\r' || cp == '\f'))
                    {
                        if (!atLineStart) pendingSpace = true;
                        continue;
                    }
                    if (pendingBreak)
                    {
                        if (anyText) Emit('\n');
                        pendingBreak = false;
                        pendingSpace = false;
                    }
                    if (pendingSpace)
                    {
                        Emit(' ');
                        pendingSpace = false;
                    }
                    Emit(cp);
                    atLineStart = false;
                    anyText = true;
                }
            }

            void BlockEdge()
            {
                pendingSpace = false;
                atLineStart = true;
                if (anyText) pendingBreak = true;
            }

            void PopTo(int depth)
            {
                while (stack.Count > depth)
                {
                    Open top = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    if (top.Pre) preDepth--;
                    if (top.Block) BlockEdge();
                    for (int s = openSpans.Count - 1; s >= 0; s--)
                    {
                        if (openSpans[s].stackDepth != stack.Count) continue;
                        LetteraPastedSpan span = openSpans[s].span;
                        openSpans.RemoveAt(s);
                        span.End = cps;
                        if (span.End > span.Start) AddMerged(clip.Spans, span);
                    }
                }
            }

            int at = from;
            var entity = new StringBuilder();
            while (at < to)
            {
                int lt = html.IndexOf('<', at, to - at);
                int stop = lt >= 0 ? lt : to;
                if (stop > at) EmitText(DecodeEntities(html, at, stop, entity));
                if (lt < 0) break;
                at = lt;

                if (StartsWith(html, at, to, "<!--"))
                {
                    int end = html.IndexOf("-->", at + 4, to - at - 4, StringComparison.Ordinal);
                    at = end < 0 ? to : end + 3;
                    continue;
                }
                if (StartsWith(html, at, to, "<!") || StartsWith(html, at, to, "<?"))
                {
                    int end = html.IndexOf('>', at, to - at);
                    at = end < 0 ? to : end + 1;
                    continue;
                }
                bool closing = at + 1 < to && html[at + 1] == '/';
                int nameStart = at + (closing ? 2 : 1), nameEnd = nameStart;
                while (nameEnd < to && IsNameChar(html[nameEnd])) nameEnd++;
                if (nameEnd == nameStart)
                {
                    // A lone '<' is text.
                    EmitText("<");
                    at++;
                    continue;
                }
                string name = html.Substring(nameStart, nameEnd - nameStart).ToLowerInvariant();
                int tagEnd = FindTagEnd(html, nameEnd, to);
                if (tagEnd < 0)
                {
                    // Cut off in a tag: nothing more to read.
                    break;
                }
                bool selfClosed = html[tagEnd - 1] == '/';
                int attributesEnd = selfClosed ? tagEnd - 1 : tagEnd;
                at = tagEnd + 1;

                if (closing)
                {
                    for (int d = stack.Count - 1; d >= 0; d--)
                    {
                        if (stack[d].Name != name) continue;
                        PopTo(d);
                        break;
                    }
                    continue;
                }
                if (name == "br")
                {
                    pendingSpace = false;
                    if (pendingBreak) { if (anyText) Emit('\n'); pendingBreak = false; }
                    Emit(0x2028);
                    atLineStart = true;
                    continue;
                }
                if (s_Dropped.Contains(name))
                {
                    // What these hold is not text. A name made only of prefixes such as o:p comes here too through the colon below.
                    int close = IndexOfCloseTag(html, at, to, name);
                    at = close < 0 ? to : close;
                    continue;
                }
                if (name.IndexOf(':') >= 0 || s_Void.Contains(name) || selfClosed) continue;

                if (stack.Count >= MaxDepth)
                {
                    // Too deep to mean anything: read on as flowing text.
                    continue;
                }
                bool block = s_Block.Contains(name);
                bool pre = name == "pre";
                if (block) BlockEdge();
                stack.Add(new Open { Name = name, Pre = pre, Block = block });
                if (pre) preDepth++;
                AddSpansOfElement(name, html, nameEnd, attributesEnd, cps + (pendingBreak && anyText ? 1 : 0) + (pendingSpace && !pendingBreak ? 1 : 0), stack.Count - 1, openSpans);
            }
            PopTo(0);
            clip.Text = text.ToString();
            clip.Spans.Sort((a, b) => a.Start.CompareTo(b.Start));
            return clip;
        }

        static void AddMerged(List<LetteraPastedSpan> spans, LetteraPastedSpan span)
        {
            for (int i = spans.Count - 1; i >= 0; i--)
            {
                LetteraPastedSpan other = spans[i];
                if (other.End == span.Start && other.EffectKey == span.EffectKey && other.Raw == span.Raw)
                {
                    other.End = span.End;
                    spans[i] = other;
                    return;
                }
            }
            spans.Add(span);
        }

        static bool StartsWith(string text, int at, int to, string value) => at + value.Length <= to && string.CompareOrdinal(text, at, value, 0, value.Length) == 0;

        static bool IsNameChar(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ':' || c == '-' || c == '_';

        // The '>' that ends a tag, past quoted attribute values.
        static int FindTagEnd(string html, int from, int to)
        {
            char quote = '\0';
            for (int i = from; i < to; i++)
            {
                char c = html[i];
                if (quote != 0)
                {
                    if (c == quote) quote = '\0';
                }
                else if (c == '"' || c == '\'')
                {
                    quote = c;
                }
                else if (c == '>')
                {
                    return i;
                }
            }
            return -1;
        }

        // The position after </name>, or -1. Case-insensitive.
        static int IndexOfCloseTag(string html, int from, int to, string name)
        {
            string close = "</" + name;
            int at = from;
            while (at < to)
            {
                int found = html.IndexOf(close, at, to - at, StringComparison.OrdinalIgnoreCase);
                if (found < 0) return -1;
                int end = html.IndexOf('>', found, to - found);
                return end < 0 ? -1 : end + 1;
            }
            return -1;
        }

        static void AddSpansOfElement(string name, string html, int from, int to, int at, int depth, List<(int, LetteraPastedSpan)> open)
        {
            string style = null, href = null;
            int i = from;
            while (i < to)
            {
                while (i < to && !IsNameChar(html[i])) i++;
                int a = i;
                while (i < to && IsNameChar(html[i])) i++;
                if (a == i) break;
                string attribute = html.Substring(a, i - a).ToLowerInvariant();
                while (i < to && char.IsWhiteSpace(html[i])) i++;
                if (i >= to || html[i] != '=') continue;
                i++;
                while (i < to && char.IsWhiteSpace(html[i])) i++;
                if (i >= to) break;
                string value;
                if (html[i] == '"' || html[i] == '\'')
                {
                    char quote = html[i++];
                    int close = html.IndexOf(quote, i, to - i);
                    if (close < 0) close = to;
                    value = html.Substring(i, close - i);
                    i = close + 1;
                }
                else
                {
                    int v = i;
                    while (i < to && !char.IsWhiteSpace(html[i])) i++;
                    value = html.Substring(v, i - v);
                }
                if (attribute == "style") style = value;
                else if (attribute == "href") href = DecodeEntities(value, 0, value.Length, new StringBuilder());
            }

            bool weightOverride = false;
            if (style != null)
            {
                string decoded = DecodeEntities(style, 0, style.Length, new StringBuilder());
                foreach (string declaration in decoded.Split(';'))
                {
                    int colon = declaration.IndexOf(':');
                    if (colon <= 0) continue;
                    string property = declaration.Substring(0, colon).Trim().ToLowerInvariant();
                    string value = declaration.Substring(colon + 1).Replace("!important", "").Trim().ToLowerInvariant();
                    if (property == "font-weight" && (value == "normal" || value == "400")) weightOverride = true;
                    if (property == "text-decoration-line") property = "text-decoration";
                    foreach (KeyValuePair<string, LetteraClipSchema> entry in LetteraClipSchemas.All)
                    {
                        LetteraClipSchema schema = entry.Value;
                        if (schema.CssProperty != property || schema.FromCss == null) continue;
                        string raw = schema.FromCss(value);
                        if (raw == null) continue;
                        open.Add((depth, new LetteraPastedSpan { Start = at, EffectKey = entry.Key, Raw = raw.Length == 0 ? null : raw }));
                    }
                }
            }
            foreach (KeyValuePair<string, LetteraClipSchema> entry in LetteraClipSchemas.All)
            {
                LetteraClipSchema schema = entry.Value;
                if (schema.HtmlElements == null || Array.IndexOf(schema.HtmlElements, name) < 0) continue;
                // An element Docs wraps a whole document in with its weight turned off means nothing.
                if (weightOverride && name == "b") continue;
                string raw = schema.RawFromElement?.Invoke(name);
                if (schema.HtmlAttribute != null)
                {
                    if (schema.HtmlAttribute == "href")
                    {
                        if (href == null || !IsSafeLink(href)) continue;
                        raw = href;
                    }
                }
                open.Add((depth, new LetteraPastedSpan { Start = at, EffectKey = entry.Key, Raw = raw }));
            }
        }

        internal static bool IsSafeLink(string url)
        {
            url = url.Trim();
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                   url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);
        }

        static readonly Dictionary<string, string> s_Entities = new Dictionary<string, string>
        {
            { "amp", "&" }, { "lt", "<" }, { "gt", ">" }, { "quot", "\"" }, { "apos", "'" }, { "nbsp", " " }, { "copy", "©" }, { "reg", "®" }, { "trade", "™" },
            { "mdash", "—" }, { "ndash", "–" }, { "hellip", "…" }, { "laquo", "«" }, { "raquo", "»" }, { "ldquo", "“" }, { "rdquo", "”" },
            { "lsquo", "‘" }, { "rsquo", "’" }, { "bull", "•" }, { "middot", "·" }, { "euro", "€" }, { "pound", "£" }, { "yen", "¥" }, { "cent", "¢" },
            { "sect", "§" }, { "para", "¶" }, { "deg", "°" }, { "plusmn", "±" }, { "times", "×" }, { "divide", "÷" }, { "shy", "­" }, { "zwj", "‍" }, { "zwnj", "‌" },
        };

        static string DecodeEntities(string html, int from, int to, StringBuilder builder)
        {
            int amp = html.IndexOf('&', from, to - from);
            if (amp < 0) return html.Substring(from, to - from);
            builder.Clear();
            int at = from;
            while (at < to)
            {
                amp = html.IndexOf('&', at, to - at);
                if (amp < 0)
                {
                    builder.Append(html, at, to - at);
                    break;
                }
                builder.Append(html, at, amp - at);
                at = amp + 1;
                // A name is short. An unclosed entity flood must stay linear.
                int semi = -1;
                for (int i = at; i < to && i < at + 32; i++)
                {
                    char c = html[i];
                    if (c == ';') { semi = i; break; }
                    if (!(char.IsLetterOrDigit(c) || c == '#')) break;
                }
                if (semi < 0)
                {
                    builder.Append('&');
                    continue;
                }
                string body = html.Substring(at, semi - at);
                string decoded = null;
                if (body.Length > 1 && body[0] == '#')
                {
                    bool hex = body[1] == 'x' || body[1] == 'X';
                    if (int.TryParse(hex ? body.Substring(2) : body.Substring(1), hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out int cp) && cp > 0 && cp <= 0x10FFFF && (cp < 0xD800 || cp > 0xDFFF))
                        decoded = char.ConvertFromUtf32(cp);
                }
                else
                {
                    s_Entities.TryGetValue(body, out decoded);
                }
                if (decoded == null)
                {
                    builder.Append('&');
                    continue;
                }
                builder.Append(decoded);
                at = semi + 1;
            }
            return builder.ToString();
        }
    }

    /// <summary>Reads and writes the part of CommonMark inline syntax the schemas declare. Plain text is never taken for Markdown.</summary>
    public static class LetteraMarkdownCodec
    {
        const string Reserved = "\\`*_{}[]()#+-.!~<>";

        static void Escape(StringBuilder builder, string text, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                if (Reserved.IndexOf(text[i]) >= 0) builder.Append('\\');
                builder.Append(text[i]);
            }
        }

        public static string Write(string plain, List<LetteraPastedSpan> spans)
        {
            var offsets = new List<int>(plain.Length + 1);
            for (int i = 0; i < plain.Length; i++)
            {
                offsets.Add(i);
                if (char.IsHighSurrogate(plain[i]) && i + 1 < plain.Length) i++;
            }
            offsets.Add(plain.Length);
            int cps = offsets.Count - 1;
            var events = new List<(int at, bool open, string text, int order)>();
            int order = 0;
            foreach (LetteraPastedSpan s in spans)
            {
                LetteraClipSchema schema = LetteraClipSchemas.Find(s.EffectKey);
                if (schema == null) continue;
                int start = Mathf.Clamp(s.Start, 0, cps), end = Mathf.Clamp(s.End, 0, cps);
                if (end <= start) continue;
                if (schema.MarkdownLink)
                {
                    events.Add((start, true, "[", order));
                    events.Add((end, false, "](" + (s.Raw ?? "") + ")", order));
                }
                else if (schema.MarkdownMarker != null)
                {
                    events.Add((start, true, schema.MarkdownMarker, order));
                    events.Add((end, false, schema.MarkdownMarker, order));
                }
                order++;
            }
            // At one spot closers come first, innermost first, and then openers, so markers stay nested.
            events.Sort((a, b) => a.at != b.at ? a.at.CompareTo(b.at) : a.open != b.open ? a.open.CompareTo(b.open) : a.open ? a.order.CompareTo(b.order) : b.order.CompareTo(a.order));
            var builder = new StringBuilder(plain.Length + events.Count * 4);
            int cp = 0, next = 0;
            while (cp <= cps)
            {
                while (next < events.Count && events[next].at == cp) builder.Append(events[next++].text);
                if (cp == cps) break;
                Escape(builder, plain, offsets[cp], offsets[cp + 1]);
                cp++;
            }
            return builder.ToString();
        }

        public static LetteraParsedClip Read(string markdown)
        {
            var clip = new LetteraParsedClip();
            var text = new StringBuilder(markdown.Length);
            int cps = 0;
            var open = new Dictionary<string, (int start, string key)>();
            var links = new Stack<int>();
            int i = 0;

            void Add(char c)
            {
                text.Append(c);
                if (!char.IsHighSurrogate(c)) cps++;
            }

            while (i < markdown.Length)
            {
                char c = markdown[i];
                if (c == '\\' && i + 1 < markdown.Length && Reserved.IndexOf(markdown[i + 1]) >= 0)
                {
                    Add(markdown[i + 1]);
                    i += 2;
                    continue;
                }
                if (c == '[')
                {
                    links.Push(cps);
                    i++;
                    continue;
                }
                if (c == ']' && links.Count > 0)
                {
                    int start = links.Pop();
                    if (i + 1 < markdown.Length && markdown[i + 1] == '(')
                    {
                        int close = markdown.IndexOf(')', i + 2);
                        if (close > 0)
                        {
                            string url = markdown.Substring(i + 2, close - i - 2);
                            if (LetteraHtmlCodec.IsSafeLink(url) && cps > start) clip.Spans.Add(new LetteraPastedSpan { Start = start, End = cps, EffectKey = typeof(LinkEffect).FullName, Raw = url });
                            i = close + 1;
                            continue;
                        }
                    }
                    i++;
                    continue;
                }
                string marker = MarkerAt(markdown, i, out string key);
                if (marker != null)
                {
                    if (open.TryGetValue(marker, out (int start, string key) opened))
                    {
                        open.Remove(marker);
                        if (cps > opened.start) clip.Spans.Add(new LetteraPastedSpan { Start = opened.start, End = cps, EffectKey = opened.key });
                    }
                    else
                    {
                        open[marker] = (cps, key);
                    }
                    i += marker.Length;
                    continue;
                }
                Add(c);
                i++;
            }
            clip.Text = text.ToString();
            clip.Spans.Sort((a, b) => a.Start.CompareTo(b.Start));
            return clip;
        }

        static string MarkerAt(string text, int at, out string key)
        {
            key = null;
            string best = null;
            foreach (KeyValuePair<string, LetteraClipSchema> entry in LetteraClipSchemas.All)
            {
                string marker = entry.Value.MarkdownMarker;
                if (marker == null || (best != null && marker.Length <= best.Length)) continue;
                if (at + marker.Length <= text.Length && string.CompareOrdinal(text, at, marker, 0, marker.Length) == 0)
                {
                    best = marker;
                    key = entry.Key;
                }
            }
            return best;
        }
    }

    /// <summary>What arrived that is not text: probe its formats and files, and set Handled to take it.</summary>
    public sealed class LetteraAttachment
    {
        public enum Origin { Paste, Drop, Picker }

        readonly IReadOnlyList<LetteraClipItem> m_Items;

        internal LetteraAttachment(IReadOnlyList<LetteraClipItem> items, Origin source)
        {
            m_Items = items;
            Source = source;
        }

        public Origin Source { get; }
        public bool Handled { get; set; }

        public bool TryGetData(string format, out byte[] bytes)
        {
            foreach (LetteraClipItem item in m_Items)
            {
                if (item.Format != format) continue;
                bytes = item.Bytes;
                return true;
            }
            bytes = null;
            return false;
        }
    }

    /// <summary>A base for rules that take images and files.</summary>
    [Serializable]
    public abstract class AttachmentRule : LetteraFieldRule
    {
        public abstract override void OfferAttachment(LetteraAttachment attachment);
    }

    /// <summary>Everything pasted arrives as plain text.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Paste, "Paste Plain")]
    public class PastePlainRule : LetteraFieldRule
    {
        bool m_Old;
        protected override void OnEnable()
        {
            m_Old = Field.PasteKeepsStyles;
            Field.PasteKeepsStyles = false;
        }

        protected override void OnDisable() => Field.PasteKeepsStyles = m_Old;
    }

    /// <summary>A URL pasted over a selection makes the selection a link, with the field's link style.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Paste, "Paste Link")]
    public class PasteLinkRule : LetteraFieldRule
    {
        [SerializeField] List<string> m_AllowedSchemes = new List<string> { "http", "https", "mailto" };

        public List<string> AllowedSchemes => m_AllowedSchemes;

        public override LetteraFieldRule Clone()
        {
            var copy = (PasteLinkRule)base.Clone();
            copy.m_AllowedSchemes = new List<string>(m_AllowedSchemes);
            return copy;
        }

        public override void FilterEdit(ref LetteraEditProposal edit)
        {
            if (edit.Reason != "user.paste" || edit.RemovedLength == 0 || string.IsNullOrEmpty(edit.Text)) return;
            string text = edit.Text.Trim();
            int colon = text.IndexOf(':');
            if (colon <= 0 || text.IndexOf(' ') >= 0 || !m_AllowedSchemes.Contains(text.Substring(0, colon).ToLowerInvariant())) return;
            if (edit.Field.ApplyEffect(typeof(LinkEffect), text, "user.paste")) edit.Reject();
        }
    }

    public partial class LetteraEditable
    {
        readonly List<LetteraClipItem> m_ClipItems = new List<LetteraClipItem>();

        /// <summary>Copies the selection and deletes it, as one step.</summary>
        public bool Cut()
        {
            if (m_ReadOnly || !CanCopy) return false;
            CommitComposition();
            LetteraSelectionRange s = Selectable.Selection;
            if (s.IsCaret) return false;
            LetteraClipboard.WriteSelection(Selectable);
            return ProposeEdit(s.Start, s.End, "", "user.cut", null);
        }

        /// <summary>Pastes what the clipboard holds, by the field's paste settings.</summary>
        public bool Paste()
        {
            if (m_ReadOnly) return false;
            if (LetteraClipboard.Backend is ILetteraClipboardAsyncOnly)
            {
                if (!s_WarnedAsyncPaste) Debug.LogWarning("Lettera: this clipboard can only be read later, so Paste did nothing. Use PasteAsync.");
                s_WarnedAsyncPaste = true;
                return false;
            }
            m_ClipItems.Clear();
            LetteraClipboard.Backend.Read(m_ClipItems);
            return PasteItems(m_ClipItems);
        }

        /// <summary>Pastes as plain text that takes the style at the caret.</summary>
        public bool PastePlain()
        {
            if (m_ReadOnly) return false;
            if (LetteraClipboard.Backend is ILetteraClipboardAsyncOnly)
            {
                LetteraClipboard.Backend.ReadAsync(items => PastePlainItems(items));
                return true;
            }
            m_ClipItems.Clear();
            LetteraClipboard.Backend.Read(m_ClipItems);
            return PastePlainItems(m_ClipItems);
        }

        bool PastePlainItems(IReadOnlyList<LetteraClipItem> items)
        {
            foreach (LetteraClipItem item in items)
                if (item.Format == LetteraClipFormat.PlainText)
                    return InsertPlain(item.Text, LetteraPlainPaste.AsText);
            return false;
        }

        static bool s_WarnedAsyncPaste;

        // The way a command or a key pastes: at once where it can be, and later where only that works.
        internal void RunPaste()
        {
            if (LetteraClipboard.Backend is ILetteraClipboardAsyncOnly) PasteAsync();
            else Paste();
        }

        /// <summary>The second half of a cut whose copy a browser's own event already made.</summary>
        internal void DeleteSelectionForCut()
        {
            if (m_ReadOnly) return;
            CommitComposition();
            LetteraSelectionRange s = Selectable.Selection;
            if (!s.IsCaret) ProposeEdit(s.Start, s.End, "", "user.cut", null);
        }

        /// <summary>For a clipboard that reads later, as in a browser.</summary>
        public void PasteAsync() => LetteraClipboard.Backend.ReadAsync(items => PasteItems(items));

        /// <summary>Pastes given items: the formats' priority is Lettera's own, HTML, Markdown, then plain text.</summary>
        public bool PasteItems(IReadOnlyList<LetteraClipItem> items)
        {
            if (m_ReadOnly || items == null) return false;
            CommitComposition();
            if (OfferAttachment(items)) return true;

            if (m_PasteKeepsStyles && !Engine.Literal)
            {
                int html = -1, markdown = -1, own = -1;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Format == LetteraClipFormat.Lettera) own = i;
                    else if (items[i].Format == LetteraClipFormat.Html) html = i;
                    else if (items[i].Format == LetteraClipFormat.Markdown) markdown = i;
                }
                var clip = new LetteraParsedClip();
                if (own >= 0 && LetteraTextCodec.TryRead(items[own].Text, clip)) return InsertClip(clip);
                if (html >= 0 && items[html].Bytes.Length <= LetteraSettings.Instance.Editing.PasteHtmlMaxBytes)
                {
                    string markup = items[html].Text;
                    return InsertClip(LetteraHtmlCodec.TryReadCarried(markup, clip) ? clip : LetteraHtmlCodec.Read(markup));
                }
                if (markdown >= 0) return InsertClip(LetteraMarkdownCodec.Read(items[markdown].Text));
            }
            for (int i = 0; i < items.Count; i++)
                if (items[i].Format == LetteraClipFormat.PlainText)
                    return InsertPlain(items[i].Text, m_PlainPaste);
            return false;
        }

        bool OfferAttachment(IReadOnlyList<LetteraClipItem> items)
        {
            bool offer = false;
            foreach (LetteraClipItem item in items)
                offer |= item.Format != LetteraClipFormat.PlainText && item.Format != LetteraClipFormat.Html && item.Format != LetteraClipFormat.Markdown && item.Format != LetteraClipFormat.Lettera;
            if (!offer) return false;
            var attachment = new LetteraAttachment(items, LetteraAttachment.Origin.Paste);
            foreach (LetteraFieldRule rule in m_ActiveRules)
            {
                if (!rule.Enabled) continue;
                rule.OfferAttachment(attachment);
                if (attachment.Handled) return true;
            }
            return false;
        }

        bool InsertClip(LetteraParsedClip clip)
        {
            LetteraSelectionRange s = Selectable.Selection;
            return ProposeEdit(s.Start, s.End, clip.Text, "user.paste", clip.Spans);
        }

        bool InsertPlain(string text, LetteraPlainPaste mode)
        {
            LetteraSelectionRange s = Selectable.Selection;
            bool literal = mode == LetteraPlainPaste.AsText || (mode == LetteraPlainPaste.Auto && m_MarkupView != LetteraMarkupView.Source);
            m_PendingWritesLiteral = literal;
            bool done = ProposeEdit(s.Start, s.End, text, "user.paste", null);
            m_PendingWritesLiteral = false;
            return done;
        }
    }
}
