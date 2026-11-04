using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>LaTeX tables in text: what is read, how columns size against the room, and that nothing runs past the text or hangs.</summary>
    public class TableTests : TextFixture
    {
        [SetUp]
        public void Fresh() => LetteraTableCache.Clear();

        [TearDown]
        public void Back() => LetteraTableCache.Clear();

        static TableModel Parse(string source)
        {
            TableModel model = LetteraTableParser.Parse(source);
            Assert.IsNotNull(model);
            return model;
        }

        static string Words(TableCell cell)
        {
            var text = new StringBuilder();
            foreach (TableRun run in cell.Runs)
            {
                if (run.Kind == TableRunKind.Space) text.Append(' ');
                else if (run.Kind == TableRunKind.Text) text.Append(run.Text);
                else if (run.Kind == TableRunKind.Math) text.Append('$').Append(run.Text).Append('$');
                else if (run.Kind == TableRunKind.Table) text.Append("[table]");
                else if (run.Kind == TableRunKind.Break) text.Append('/');
            }
            return text.ToString();
        }

        static FontFace Face => Latin.LoadFace();

        static LetteraMathLayout Lay(TableModel model, float room) => LetteraTableTypesetter.Lay(model, room, Face, null, "Table");

        // ---------------------------------------------------------------- reading

        [Test]
        public void ItReadsColumnsRulesAndCells()
        {
            TableModel model = Parse(@"\begin{tabular}{|l|c||r|}\hline a & b & c \\ \hline d & \textbf{e f} & $x^2$ \\ \hline\hline\end{tabular}");
            Assert.AreEqual(3, model.Columns.Count);
            Assert.AreEqual("lcr", "" + model.Columns[0].Align + model.Columns[1].Align + model.Columns[2].Align);
            Assert.AreEqual(1, model.Columns[0].RulesBefore);
            Assert.AreEqual(1, model.Columns[1].RulesBefore);
            Assert.AreEqual(2, model.Columns[2].RulesBefore);
            Assert.AreEqual(1, model.RulesAfter);
            Assert.AreEqual(2, model.Rows.Count);
            Assert.AreEqual("e f", Words(model.Rows[1].Cells[1]));
            Assert.AreEqual(LetteraTableParser.Bold, model.Rows[1].Cells[1].Runs[0].Style);
            Assert.AreEqual("$x^2$", Words(model.Rows[1].Cells[2]));
            var boundaries = new List<int>();
            foreach (TableRule rule in model.Rules) boundaries.Add(rule.Boundary);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 2 }, boundaries, "one above, one between, two under");
        }

        [Test]
        public void SpansAcrossColumnsAndRows()
        {
            TableModel model = Parse(@"\begin{tabular}{lll}\multicolumn{2}{|c|}{wide} & c \\ \multirow{2}{*}{tall} & b & c \\ & e & f \end{tabular}");
            TableCell wide = model.Rows[0].Cells[0];
            Assert.AreEqual(2, wide.Span);
            Assert.AreEqual('c', wide.Align);
            Assert.AreEqual(1, wide.RulesLeft);
            Assert.AreEqual(1, wide.RulesRight);
            Assert.AreEqual(2, model.Rows[0].Cells[1].Column, "the next cell comes after the span");
            Assert.AreEqual(2, model.Rows[1].Cells[0].RowSpan);
            Assert.AreEqual("tall", Words(model.Rows[1].Cells[0]));
            Assert.AreEqual(3, model.Rows.Count);
        }

        [Test]
        public void ColumnSpecsReadAsLaTeXDoes()
        {
            TableModel model = Parse(@"\begin{tabular}{@{}>{\bfseries}l*{3}{c}p{2cm}m{10pt}@{ : }r@{}}x\end{tabular}");
            Assert.AreEqual(7, model.Columns.Count);
            Assert.IsFalse(model.Columns[0].PadBefore, "@{} takes the space before");
            Assert.AreEqual(LetteraTableParser.Bold, model.Columns[0].Style);
            Assert.AreEqual(1, model.Columns[4].Kind);
            Assert.AreEqual(2 * 2.845f, model.Columns[4].Width, 0.001f);
            Assert.AreEqual('m', model.Columns[5].Vertical);
            Assert.AreEqual(1f, model.Columns[5].Width, 0.001f);
            Assert.AreEqual(" : ", model.Columns[6].Before);
            Assert.IsFalse(model.Columns[5].PadAfter);
            Assert.IsFalse(model.PadAfterLast);

            TableModel x = Parse(@"\begin{tabularx}{0.5\textwidth}{lX>{\centering\arraybackslash}X}a\end{tabularx}");
            Assert.AreEqual(0.5f, x.TargetFactor, 0.001f);
            Assert.AreEqual(2, x.Columns[1].Kind);
            Assert.AreEqual('c', x.Columns[2].Align);
            TableModel star = Parse(@"\begin{tabular*}{20em}{@{\extracolsep{\fill}}ll}a & b\end{tabular*}");
            Assert.IsTrue(star.Fill);
            Assert.AreEqual(20f, star.TargetEm, 0.001f);
            TableModel y = Parse(@"\begin{tabulary}{\linewidth}{LCRJ}a\end{tabulary}");
            Assert.AreEqual(3, y.Columns[0].Kind);
            Assert.AreEqual('c', y.Columns[1].Align);
        }

        [Test]
        public void ALongtableShowsItsFirstHeadAndLastFoot()
        {
            TableModel model = Parse(@"\begin{longtable}{ll}
\caption{Results}\\
\hline First & head \\ \hline
\endfirsthead
Again & head \\
\endhead
Continued \\
\endfoot
\hline Last & foot \\
\endlastfoot
1 & 2 \\
3 & 4 \\
\end{longtable}");
            Assert.IsTrue(model.Block);
            Assert.IsTrue(model.Centered);
            Assert.AreEqual("Results", Join(model.Caption));
            Assert.IsFalse(model.CaptionBelow);
            var first = new List<string>();
            foreach (TableRow row in model.Rows) first.Add(Words(row.Cells[0]));
            CollectionAssert.AreEqual(new[] { "First", "1", "3", "Last" }, first);
        }

        static string Join(List<TableRun> runs)
        {
            var cell = new TableCell();
            cell.Runs.AddRange(runs);
            return Words(cell);
        }

        [Test]
        public void AFloatGivesItsTableACaptionAndCentring()
        {
            TableModel model = Parse(@"\begin{table}[h]\centering\begin{tabular}{cc}a & b\end{tabular}\caption{Two letters}\label{t:x}\end{table}");
            Assert.IsTrue(model.Block);
            Assert.IsTrue(model.Centered);
            Assert.AreEqual("Two letters", Join(model.Caption));
            Assert.IsTrue(model.CaptionBelow);
            Assert.AreEqual(2, model.Columns.Count);
            Assert.AreEqual(1, model.Rows.Count);
        }

        [Test]
        public void TablesNest()
        {
            TableModel model = Parse(@"\begin{tabular}{|c|c|}\hline outer & \begin{tabular}{|c|}\hline in \\ \hline side \\ \hline\end{tabular} \\ \hline\end{tabular}");
            Assert.AreEqual(1, model.Rows.Count);
            TableRun inner = model.Rows[0].Cells[1].Runs[0];
            Assert.AreEqual(TableRunKind.Table, inner.Kind);
            Assert.AreEqual(2, inner.Table.Rows.Count);
            LetteraMathLayout plain = Lay(Parse(@"\begin{tabular}{|c|c|}\hline outer & in \\ \hline\end{tabular}"), float.PositiveInfinity);
            LetteraMathLayout nested = Lay(model, float.PositiveInfinity);
            Assert.Greater(Rules(nested), Rules(plain), "the inner table draws its own rules");
            Assert.Greater(nested.Ascent + nested.Descent, plain.Ascent + plain.Descent, "and is two rows tall");
        }

        static int Rules(LetteraMathLayout layout)
        {
            int count = 0;
            foreach (LetteraMathItem item in layout.Items)
                if (item.Kind == MathItemKind.Rule)
                    count++;
            return count;
        }

        // ---------------------------------------------------------------- widths

        static float Natural(string text)
        {
            LetteraMathLayout one = Lay(Parse(@"\begin{tabular}{@{}l@{}}" + text + @"\end{tabular}"), float.PositiveInfinity);
            return one.Width;
        }

        [Test]
        public void ColumnsTakeTheirWidestWhenItFits()
        {
            float a = Natural("alpha beta"), b = Natural("gamma");
            LetteraMathLayout layout = Lay(Parse(@"\begin{tabular}{ll}alpha beta & gamma\end{tabular}"), 100);
            Assert.AreEqual(a + b + 4 * LetteraTableTypesetter.Padding, layout.Width, 0.01f);
        }

        [Test]
        public void ColumnsShareTheRoomAsCssAutomaticLayoutDoes()
        {
            // Column one: narrowest 'alpha', widest 'alpha alpha alpha'. Column two: 'gamma' both ways.
            float min1 = Natural("alpha"), max1 = Natural("alpha alpha alpha"), two = Natural("gamma");
            TableModel model = Parse(@"\begin{tabular}{ll}alpha alpha alpha & gamma\end{tabular}");
            float gaps = 4 * LetteraTableTypesetter.Padding;
            float room = gaps + min1 + two + (max1 - min1) * 0.5f;
            LetteraMathLayout layout = Lay(model, room);
            Assert.AreEqual(room, layout.Width, 0.01f, "it fills the room between its narrowest and widest");
            // At half the spare room, column one takes half its spare: room - gaps - two of content.
            float narrow = gaps + min1 + two;
            Assert.AreEqual(narrow, Lay(model, narrow).Width, 0.01f, "at the narrowest, every column at its narrowest");
            Assert.LessOrEqual(Lay(model, narrow * 0.6f).Width, narrow * 0.6f + 0.001f, "narrower still, it still keeps to the room");
        }

        [Test]
        public void SetWidthsFillTheirTarget()
        {
            LetteraMathLayout x = Lay(Parse(@"\begin{tabularx}{\textwidth}{|l|X|X|}a & b & c\end{tabularx}"), 30);
            Assert.AreEqual(30, x.Width, 0.01f, "tabularx at the text's width");
            LetteraMathLayout half = Lay(Parse(@"\begin{tabularx}{0.5\textwidth}{lX}a & b\end{tabularx}"), 30);
            Assert.AreEqual(15, half.Width, 0.01f);
            LetteraMathLayout star = Lay(Parse(@"\begin{tabular*}{20em}{@{\extracolsep{\fill}}lll}a & b & c\end{tabular*}"), 30);
            Assert.AreEqual(20, star.Width, 0.01f, "tabular* spreads its columns to its width");
            TableModel proportional = Parse(@"\begin{tabulary}{12em}{LL}short & a much much longer sentence of words here\end{tabulary}");
            LetteraMathLayout y = Lay(proportional, 30);
            Assert.LessOrEqual(y.Width, 12.001f, "tabulary within its width");
            LetteraMathLayout fits = Lay(Parse(@"\begin{tabulary}{30em}{LL}a & b\end{tabulary}"), 40);
            Assert.Less(fits.Width, 10, "tabulary that fits keeps its natural width");
            LetteraMathLayout wider = Lay(Parse(@"\begin{tabularx}{50em}{lX}a & b\end{tabularx}"), 30);
            Assert.LessOrEqual(wider.Width, 30.001f, "a set width past the text's is held to the text's");
        }

        [Test]
        public void AFixedColumnWrapsItsWords()
        {
            TableModel model = Parse(@"\begin{tabular}{p{3em}}one two three four five six\end{tabular}");
            LetteraMathLayout layout = Lay(model, 100);
            Assert.AreEqual(3 + 2 * LetteraTableTypesetter.Padding, layout.Width, 0.01f);
            LetteraMathLayout single = Lay(Parse(@"\begin{tabular}{l}one\end{tabular}"), 100);
            Assert.Greater(layout.Ascent + layout.Descent, 2.5f * (single.Ascent + single.Descent), "several lines");
        }

        [Test]
        public void NothingRunsPastTheRoom()
        {
            string[] tables =
            {
                @"\begin{tabular}{|l|l|l|}\hline averyveryverylongwordthatcannotbreak & another & third \\ \hline\end{tabular}",
                @"\begin{tabular}{" + new string('c', 60) + "}" + string.Join(" & ", new string[60]).Replace(" ", "x ") + @"\end{tabular}",
                @"\begin{tabular}{p{100cm}}x\end{tabular}",
                @"\begin{tabular}{ll}\multicolumn{2}{c}{a very long spanning heading over both of the columns} \\ a & b\end{tabular}",
                @"\begin{tabular}{c}\begin{tabular}{ccc}nested & wide & table of words here\end{tabular}\end{tabular}",
                @"\begin{table}\caption{A caption that is a great deal longer than the table under it, so it wraps}\begin{tabular}{c}x\end{tabular}\end{table}",
            };
            foreach (string source in tables)
                foreach (float room in new[] { 4f, 9f, 25f })
                {
                    LetteraMathLayout layout = Lay(Parse(source), room);
                    Assert.LessOrEqual(layout.Width, room + 0.001f, source + " at " + room);
                    foreach (LetteraMathItem item in layout.Items)
                    {
                        float right = item.X + (item.Kind == MathItemKind.Rule && !item.Vertical ? item.Length : 0);
                        Assert.LessOrEqual(right, room + 0.05f, source + " at " + room + ": an item past the room");
                        Assert.GreaterOrEqual(item.X, -0.05f);
                    }
                }
        }

        [Test]
        public void HugeSizesAreCutBackWithAWarning()
        {
            TableModel wide = Parse(@"\begin{tabular}{*{1000000}{c}}x\end{tabular}");
            Assert.AreEqual(LetteraTableLimits.Columns, wide.Columns.Count);
            StringAssert.Contains("columns", wide.Warning);
            TableModel spans = Parse(@"\begin{tabular}{c}\multicolumn{99999999}{c}{x} \\ \multirow{99999999}{*}{y}\end{tabular}");
            Assert.AreEqual(LetteraTableLimits.Columns, spans.Rows[0].Cells[0].Span);
            Assert.AreEqual(LetteraTableLimits.Rows, spans.Rows[1].Cells[0].RowSpan);
            Assert.Less(Lay(spans, 40).Width, 40.001f);
            TableModel space = Parse(@"\begin{tabular}{p{99999cm}}x \\[99999cm] y\end{tabular}");
            Assert.AreEqual(LetteraTableLimits.LengthEm, space.Columns[0].Width, 0.001f);
            Assert.AreEqual(LetteraTableLimits.LengthEm, space.Rows[0].Space, 0.001f);
            var rows = new StringBuilder(@"\begin{tabular}{c}");
            for (int i = 0; i < LetteraTableLimits.Rows + 50; i++) rows.Append("x\\\\");
            rows.Append(@"\end{tabular}");
            TableModel tall = Parse(rows.ToString());
            Assert.AreEqual(LetteraTableLimits.Rows, tall.Rows.Count);
            StringAssert.Contains("rows", tall.Warning);
            TableModel deep = Parse(Nest(LetteraTableLimits.Depth + 3));
            StringAssert.Contains("nest", deep.Warning);
            TableModel huge = Parse(@"\begin{tabular}{c}" + new string('x', LetteraTableLimits.SourceLength) + @"\end{tabular}");
            StringAssert.Contains("longer", huge.Warning);
        }

        static string Nest(int depth)
        {
            var text = new StringBuilder();
            for (int i = 0; i < depth; i++) text.Append(@"\begin{tabular}{c}");
            text.Append('x');
            for (int i = 0; i < depth; i++) text.Append(@"\end{tabular}");
            return text.ToString();
        }

        static double Cost(string source)
        {
            double best = double.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                var clock = Stopwatch.StartNew();
                Lay(LetteraTableParser.Parse(source), 40);
                clock.Stop();
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        [TestCase("x & ")]
        [TestCase("x \\\\ ")]
        [TestCase("\\hline ")]
        [TestCase("{")]
        [TestCase("}")]
        [TestCase("$")]
        [TestCase("word ")]
        [TestCase("\\begin{tabular}{c}")]
        [TestCase("\\end{tabular}")]
        [TestCase("\\multicolumn{9}{c}{x} & ")]
        [TestCase("\\multirow{99}{*}{x} \\\\ ")]
        [TestCase("\\textbf{")]
        [TestCase("averyveryverylongunbreakableword")]
        [TestCase("\\cline{1-2} x \\\\ ")]
        public void AdversarialTablesStayLinear(string token)
        {
            string Body(int length)
            {
                var text = new StringBuilder(@"\begin{tabular}{|l|c|p{3em}|}", length + 64);
                while (text.Length + token.Length < length) text.Append(token);
                return text.Append(@"\end{tabular}").ToString();
            }
            string small = Body(8000), large = Body(16000);
            Cost(small);
            double a = Cost(small), b = Cost(large);
            Assert.Less(b, Math.Max(a * 2.5, a + 2), $"{token}: {a:0.000} ms then {b:0.000} ms");
        }

        [Test]
        public void RandomInputNeverThrows()
        {
            var random = new System.Random(4321);
            string[] tokens = { "\\begin{tabular}{", "}", "{", "l", "c", "|", "p{", "3cm", "&", "\\\\", "\\hline", "\\cline{1-2}", "\\multicolumn{2}{c}{", "\\multirow{2}{*}{", "$", "x", " ", "\\end{tabular}", "\\textbf{", "*{3}{", "@{", ">{\\bfseries}", "\\caption{", "\\endhead", "[t]", "\\begin{longtable}{", "\\end{longtable}", "%", "~", "--" };
            for (int run = 0; run < 3000; run++)
            {
                var text = new StringBuilder(run % 2 == 0 ? @"\begin{tabular}{lc}" : "");
                int count = random.Next(0, 30);
                for (int i = 0; i < count; i++)
                {
                    if (random.Next(6) == 0) text.Append((char)random.Next(1, 0xFFFF));
                    else text.Append(tokens[random.Next(tokens.Length)]);
                }
                if (run % 2 == 0) text.Append(@"\end{tabular}");
                string source = text.ToString();
                Assert.DoesNotThrow(() => Lay(LetteraTableParser.Parse(source), run % 3 == 0 ? float.PositiveInfinity : 20), source);
            }
        }

        static double MatchCost(string text)
        {
            var matcher = new LetteraTableMatcher();
            var context = new LetteraMatchContext();
            double best = double.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                context.Begin(text.AsSpan());
                var clock = Stopwatch.StartNew();
                matcher.Find(context);
                clock.Stop();
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        [TestCase("\\begin{tabular}")]
        [TestCase("\\end{tabular}")]
        [TestCase("\\begin{tabular}{c}x\\end{tabular}")]
        [TestCase("\\begin{tabular}\\begin{longtable}\\end{tabular}")]
        [TestCase("\\\\")]
        public void TheMatcherStaysLinear(string token)
        {
            var small = new StringBuilder();
            while (small.Length < 100000) small.Append(token);
            var large = new StringBuilder();
            while (large.Length < 1000000) large.Append(token);
            MatchCost(small.ToString());
            double a = MatchCost(small.ToString()), b = MatchCost(large.ToString());
            Assert.Less(b, Math.Max(a * 15, a + 5), $"{token}: {a:0.000} ms then {b:0.000} ms");
        }

        // ---------------------------------------------------------------- in a text

        static string Shown(LetteraWorldText label)
        {
            var text = new StringBuilder();
            foreach (int cp in label.Engine.ShownCodepoints) text.Append(char.ConvertFromUtf32(cp));
            return text.ToString();
        }

        const string Sample = @"\begin{tabular}{|l|r|}\hline Item & Cost \\ \hline Apples & 1.20 \\ Pears & 0.95 \\ \hline\end{tabular}";

        [Test]
        public void ATableStandsInTheLineAsOneObject()
        {
            LetteraWorldText label = Make("Prices: " + Sample + " today.");
            Assert.AreEqual("Prices: \uFFFC today.", Shown(label));
            Assert.IsTrue(label.Engine.IsFormulaAt(8));
            Assert.AreEqual(Sample, label.Engine.FormulaSourceAt(8), "copying it gives its source");
            Assert.IsTrue(label.Engine.TryInlineBox(8, out Rect box, out _));
            Assert.Greater(box.width, 40);
            Assert.Greater(box.height, 40, "three rows at 20 px");
            LetteraWorldText block = Make("Before" + @"\begin{longtable}{ll}a & b\end{longtable}" + "after");
            Assert.AreEqual("Before\n\uFFFC\nafter", Shown(block), "a longtable stands on a line of its own");
        }

        [Test]
        public void AFormulaTagInACellIsAFormula()
        {
            TableModel model = Parse(@"\begin{tabular}{ll}<math>x^2</math> & $y$\end{tabular}");
            Assert.AreEqual("$x^2$", Words(model.Rows[0].Cells[0]));
            LetteraWorldText label = Make(@"A \begin{tabular}{ll}<math>x^2</math> & b\end{tabular} B");
            Assert.AreEqual("A ￼ B", Shown(label), "the table holds the formula, and not the other way round");
        }

        [Test]
        public void WhatIsNotATableStaysText()
        {
            Assert.AreEqual(@"\begin{tabular}{l}x", Shown(Make(@"\begin{tabular}{l}x")), "no \\end");
            Assert.AreEqual(@"\begin{itemize}x\end{itemize}", Shown(Make(@"\begin{itemize}x\end{itemize}")));
            Assert.AreEqual(@"\begin{tabular}{l}x\end{tabular}", Shown(Make(@"\\begin{tabular}{l}x\end{tabular}")), "an escaped backslash");
        }

        [Test]
        public void ItNeverRunsPastTheTextAndRefitsWhenTheTextResizes()
        {
            string wide = @"\begin{tabular}{|l|l|l|}\hline one two three four & five six seven eight & nine ten eleven twelve \\ \hline\end{tabular}";
            LetteraWorldText label = Make(wide, 1000, 400);
            Assert.IsTrue(label.Engine.TryInlineBox(0, out Rect roomy, out _));
            Assert.Less(roomy.width, 1000);
            ((RectTransform)label.transform).sizeDelta = new Vector2(300, 400);
            label.Engine.SetDirty(DirtyLevel.Layout);
            label.ForceRebuild();
            Assert.IsTrue(label.Engine.TryInlineBox(0, out Rect tight, out _));
            Assert.LessOrEqual(tight.width, 300.01f, "held to the new width");
            Assert.Greater(tight.height, roomy.height, "its words wrapped");
            Assert.LessOrEqual(WidthOf(label), 300.01f, "and the line too");
            ((RectTransform)label.transform).sizeDelta = new Vector2(1000, 400);
            label.Engine.SetDirty(DirtyLevel.Layout);
            label.ForceRebuild();
            Assert.IsTrue(label.Engine.TryInlineBox(0, out Rect again, out _));
            Assert.AreEqual(roomy.width, again.width, 0.01f, "and back");
        }

        [Test]
        public void CaptionsCountUpThroughAText()
        {
            string one = @"\begin{table}\caption{First}\begin{tabular}{c}a\end{tabular}\end{table}";
            string two = @"\begin{table}\caption{Second}\begin{tabular}{c}b\end{tabular}\end{table}";
            LetteraWorldText label = Make(one + two);
            var formulas = new List<LetteraFormulaInfo>();
            label.GetFormulas(formulas);
            Assert.AreEqual(2, formulas.Count);
            Assert.AreEqual(1, LetteraTableCache.Model(one, 1).Number);
            Assert.AreEqual(2, LetteraTableCache.Model(two, 2).Number);
            Assert.Greater(formulas[1].Box.width, Natural("b"), "the caption widens it");
        }

        // ---------------------------------------------------------------- what it looks like

        [Test]
        public void ItDraws()
        {
            string source = "Results in a sentence: " + Sample + @" and on.
\begin{table}\centering\caption{Fruit, with spans, rules and a formula}\begin{tabular}{|l|c|r|}\hline \multicolumn{2}{|c|}{\textbf{Kind}} & Cost \\ \hline \multirow{2}{*}{Apple} & red & $x^2$ \\ \cline{2-3} & green & 1.20 \\ \hline Pear & \begin{tabular}{|c|}\hline in \\ \hline side \\ \hline\end{tabular} & 0.95 \\ \hline\end{tabular}\end{table}
\begin{tabularx}{\textwidth}{|l|X|}\hline Term & A long description that wraps inside its X column, which shares the text's width. \\ \hline\end{tabularx}";
            LetteraWorldText label = Make(source, 760, 560, null, 18);
            label.Color = Color.white;
            label.ForceRebuild();
            Texture2D image = Render(800, 600, Color.black);
            int lit = 0;
            foreach (Color32 pixel in image.GetPixels32())
                if (pixel.r > 128)
                    lit++;
            Assert.Greater(lit, 3000, "it draws");
            Save(image, "tables.png");
        }

        static void Save(Texture2D image, string name)
        {
            string folder = Environment.GetEnvironmentVariable("LETTERA_SHOTS");
            if (string.IsNullOrEmpty(folder)) return;
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name), image.EncodeToPNG());
        }

        [Test]
        public void ALongTableInANarrowTextDraws()
        {
            string source = @"Some text before the table.
\begin{longtable}{@{}lp{9em}r@{}}
\caption{A longtable with booktabs rules}\\
\toprule \textbf{Name} & \textbf{Notes} & \textbf{Count} \\ \midrule
\endfirsthead
\midrule \textbf{Name} & \textbf{Notes} & \textbf{Count} \\ \midrule
\endhead
\bottomrule
\endlastfoot
Alpha & A note long enough to wrap in its fixed column & 12 \\
Beta & Short & 7 \\
\cmidrule{2-3}
Gamma--Delta & An averyveryverylongwordthatmustbreak here & 1\,024 \\
\end{longtable}
And text after it, \begin{tabular}[t]{|c|}\hline top \\ \hline row \\ \hline\end{tabular} with a [t] table and \begin{tabular}[b]{|c|}\hline bottom \\ \hline row \\ \hline\end{tabular} with a [b] one.";
            LetteraWorldText label = Make(source, 300, 560, null, 18);
            label.Color = Color.white;
            label.ForceRebuild();
            Assert.LessOrEqual(WidthOf(label), 300.01f, "within the text's width");
            Texture2D image = Render(340, 600, Color.black);
            int lit = 0;
            foreach (Color32 pixel in image.GetPixels32())
                if (pixel.r > 128)
                    lit++;
            Assert.Greater(lit, 2000, "it draws");
            Save(image, "longtable.png");
        }
    }
}
