#if !LETTERA_NO_MATH
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>\ce and \pu: what each documented form becomes in the formula notation, and how it lays out with the bundled math font.</summary>
    public class ChemistryTests : TextFixture
    {
        [SetUp]
        public void Fresh()
        {
            LetteraMathCache.Clear();
            LetteraMath.ForgetWarnings();
        }

        [TearDown]
        public void Back() => LetteraMathCache.Clear();

        static string Ce(string content)
        {
            string tex = LetteraChemistry.Translate(content, false, out string error, out _);
            Assert.IsNull(error, content + ": " + error);
            return tex.Replace(" ", "");
        }

        static string Pu(string content)
        {
            string tex = LetteraChemistry.Translate(content, true, out string error, out _);
            Assert.IsNull(error, content + ": " + error);
            return tex.Replace(" ", "");
        }

        // Spaces are dropped from what is compared: they only end command names.
        [TestCase("H2O", @"\mathrm{H}_{2}\mathrm{O}")]
        [TestCase("Sb2O3", @"\mathrm{Sb}_{2}\mathrm{O}_{3}")]
        [TestCase("H+", @"\mathrm{H}^{{+}}")]
        [TestCase("CrO4^2-", @"\mathrm{CrO}_{4}^{2{-}}")]
        [TestCase("[AgCl2]-", @"[\mathrm{AgCl}_{2}]^{{-}}")]
        [TestCase("Y^99+", @"\mathrm{Y}^{99{+}}")]
        [TestCase("Y^{99+}", @"\mathrm{Y}^{99{+}}")]
        [TestCase("Fe^{II}Fe^{III}2O4", @"\mathrm{Fe}^{\mathrm{II}}\mathrm{Fe}_{2}^{\mathrm{III}}\mathrm{O}_{4}")]
        [TestCase("OCO^{.-}", @"\mathrm{OCO}^{\bullet{-}}")]
        [TestCase("2H2O", @"2\,\mathrm{H}_{2}\mathrm{O}")]
        [TestCase("2 H2O", @"2\,\mathrm{H}_{2}\mathrm{O}")]
        [TestCase("0.5 H2O", @"0.5\,\mathrm{H}_{2}\mathrm{O}")]
        [TestCase("1/2 H2O", @"\tfrac{1}{2}\,\mathrm{H}_{2}\mathrm{O}")]
        [TestCase("^{227}_{90}Th+", @"{}^{227}_{\phantom{0}90}\mathrm{Th}^{{+}}")]
        [TestCase("^0_-1n-", @"{}^{\phantom{0}0}_{{-}1}\mathrm{n}^{{-}}")]
        [TestCase("(NH4)2S", @"(\mathrm{NH}_{4})_{2}\mathrm{S}")]
        [TestCase("H2(aq)", @"\mathrm{H}_{2}(\mathrm{aq})")]
        [TestCase("CO3^2-_{(aq)}", @"\mathrm{CO}_{3}^{2{-}}{}_{(\mathrm{aq})}")]
        [TestCase("NaOH(aq,$\\infty$)", @"\mathrm{NaOH}(\mathrm{aq},{\infty})")]
        [TestCase("NO_x", @"\mathrm{NO}_{x}")]
        [TestCase("KCr(SO4)2*12H2O", @"\mathrm{KCr}(\mathrm{SO}_{4})_{2}\cdot12\,\mathrm{H}_{2}\mathrm{O}")]
        [TestCase("KCr(SO4)2.12H2O", @"\mathrm{KCr}(\mathrm{SO}_{4})_{2}\cdot12\,\mathrm{H}_{2}\mathrm{O}")]
        [TestCase("C6H5-CHO", @"\mathrm{C}_{6}\mathrm{H}_{5}{-}\mathrm{CHO}")]
        [TestCase("A-B=C#D", @"\mathrm{A}{-}\mathrm{B}{=}\mathrm{C}{\equiv}\mathrm{D}")]
        [TestCase("A\\bond{-}B\\bond{=}C\\bond{#}D", @"\mathrm{A}{-}\mathrm{B}{=}\mathrm{C}{\equiv}\mathrm{D}")]
        [TestCase("A\\bond{1}B\\bond{2}C\\bond{3}D", @"\mathrm{A}{-}\mathrm{B}{=}\mathrm{C}{\equiv}\mathrm{D}")]
        [TestCase("[Pt(\\eta^2-C2H4)Cl3]-", @"[\mathrm{Pt}(\eta^{2}{-}\mathrm{C}_{2}\mathrm{H}_{4})\mathrm{Cl}_{3}]^{{-}}")]
        [TestCase("\\mu-Cl", @"\mu{-}\mathrm{Cl}")]
        [TestCase("CO2 + C -> 2 CO", @"\mathrm{CO}_{2}+\mathrm{C}\xrightarrow{}2\,\mathrm{CO}")]
        [TestCase("A <=> B", @"\mathrm{A}\xrightleftharpoons{}\mathrm{B}")]
        [TestCase("A <-> B", @"\mathrm{A}\xleftrightarrow{}\mathrm{B}")]
        [TestCase("A <--> B", @"\mathrm{A}\xrightleftarrows{}\mathrm{B}")]
        [TestCase("A <=>> B", @"\mathrm{A}\cequilibriumright{}\mathrm{B}")]
        [TestCase("A <<=> B", @"\mathrm{A}\cequilibriumleft{}\mathrm{B}")]
        [TestCase("A <- B", @"\mathrm{A}\xleftarrow{}\mathrm{B}")]
        [TestCase("A ->[H2O] B", @"\mathrm{A}\xrightarrow{\mathrm{H}_{2}\mathrm{O}}\mathrm{B}")]
        [TestCase("A ->[{text above}][{text below}] B", @"\mathrm{A}\xrightarrow[{\text{textbelow}}]{\text{textabove}}\mathrm{B}")]
        [TestCase("A ->[$x$][$x_i$] B", @"\mathrm{A}\xrightarrow[{{x_i}}]{{x}}\mathrm{B}")]
        [TestCase("SO4^2- + Ba^2+ -> BaSO4 v", @"\mathrm{SO}_{4}^{2{-}}+\mathrm{Ba}^{2{+}}\xrightarrow{}\mathrm{BaSO}_{4}\,\downarrow")]
        [TestCase("A v B (v) -> B ^ B (^)", @"\mathrm{A}\,\downarrow\,\mathrm{B}\,(\downarrow)\xrightarrow{}\mathrm{B}\,\uparrow\,\mathrm{B}\,(\uparrow)")]
        [TestCase("x Na(NH4)HPO4 ->[\\Delta] (NaPO3)_x + x NH3 ^ + x H2O", @"x\,\mathrm{Na}(\mathrm{NH}_{4})\mathrm{HPO}_{4}\xrightarrow{\Delta}(\mathrm{NaPO}_{3})_{x}+x\,\mathrm{NH}_{3}\,\uparrow+x\,\mathrm{H}_{2}\mathrm{O}")]
        [TestCase("H{}^3HO", @"\mathrm{H}{}^{3}\mathrm{HO}")]
        [TestCase("Fe(CN)_{$\\frac{6}{2}$}", @"\mathrm{Fe}(\mathrm{CN})_{{\frac{6}{2}}}")]
        [TestCase("X_$i$^$x$", @"\mathrm{X}_{{i}}^{{x}}")]
        public void ItWritesWhatMhchemDocuments(string content, string expected)
        {
            Assert.AreEqual(expected, Ce(content), content);
        }

        [TestCase("123 kJ", @"123\,\mathrm{kJ}")]
        [TestCase("123 mm2", @"123\,\mathrm{mm}^{2}")]
        [TestCase("123 J s", @"123\,\mathrm{J}\,\mathrm{s}")]
        [TestCase("123 J*s", @"123\,\mathrm{J}\cdot\mathrm{s}")]
        [TestCase("123 kJ/mol", @"123\,\mathrm{kJ}/\mathrm{mol}")]
        [TestCase("123 kJ//mol", @"123\,\frac{\mathrm{kJ}}{\mathrm{mol}}")]
        [TestCase("123 kJ mol-1", @"123\,\mathrm{kJ}\,\mathrm{mol}^{{-}1}")]
        [TestCase("123 kJ*mol-1", @"123\,\mathrm{kJ}\cdot\mathrm{mol}^{{-}1}")]
        [TestCase("1.2e3 kJ", @"1.2\cdot10^{3}\,\mathrm{kJ}")]
        [TestCase("1,2e3 kJ", @"1{,}2\cdot10^{3}\,\mathrm{kJ}")]
        [TestCase("1.2E3 kJ", @"1.2\times10^{3}\,\mathrm{kJ}")]
        [TestCase("1.2e-3 kJ", @"1.2\cdot10^{{-}3}\,\mathrm{kJ}")]
        [TestCase("1234 m", @"1234\,\mathrm{m}")]
        [TestCase("12345.67891 m", @"12\,345.678\,91\,\mathrm{m}")]
        [TestCase("25 °C", @"25\,\mathrm{°C}")]
        [TestCase("90°", @"90\mathrm{°}")]
        [TestCase("9.81 m s^-2", @"9.81\,\mathrm{m}\,\mathrm{s}^{{-}2}")]
        [TestCase("1.5 +- 0.1 µm", @"1.5\pm0.1\,\mathrm{μm}")]
        [TestCase("kJ/mol", @"\mathrm{kJ}/\mathrm{mol}")]
        public void QuantitiesSpaceAndMultiplyAsDocumented(string content, string expected)
        {
            Assert.AreEqual(expected, Pu(content), content);
        }

        // ---------------------------------------------------------------- layout

        static FontFace MathFace()
        {
            LetteraFontAsset bundled = LetteraMathMenu.BundledFont();
            FontFace face = bundled != null ? bundled.LoadFace() : null;
            return face != null && NativeMathData.Available(face) ? face : Latin.LoadFace();
        }

        static LetteraMathLayout Lay(string source, bool display = false)
        {
            FontFace face = MathFace();
            LetteraMathLayout layout = LetteraMathCache.Get(source, display, face, face);
            Assert.IsFalse(layout.Failed, source + ": " + layout.Message);
            return layout;
        }

        static readonly string[] Documented =
        {
            @"\ce{CO2 + C -> 2 CO}", @"\ce{Hg^2+ ->[I-] HgI2 ->[I-] [Hg^{II}I4]^2-}", @"\ce{H2O}", @"\ce{Sb2O3}", @"\ce{H+}", @"\ce{CrO4^2-}", @"\ce{[AgCl2]-}",
            @"\ce{Y^99+}", @"\ce{Y^{99+}}", @"\ce{H2_{(aq)}}", @"\ce{NO3-}", @"\ce{(NH4)2S}", @"\ce{2H2O}", @"\ce{2 H2O}", @"\ce{0.5 H2O}", @"\ce{1/2 H2O}",
            @"\ce{$n$ H2O}", @"\ce{^{227}_{90}Th+}", @"\ce{^227_90Th+}", @"\ce{^{0}_{-1}n^{-}}", @"\ce{^0_-1n-}", @"\ce{H{}^3HO}", @"\ce{H^3HO}",
            @"\ce{A -> B}", @"\ce{A <- B}", @"\ce{A <-> B}", @"\ce{A <--> B}", @"\ce{A <=> B}", @"\ce{A <=>> B}", @"\ce{A <<=> B}",
            @"\ce{A ->[H2O] B}", @"\ce{A ->[{text above}][{text below}] B}", @"\ce{A ->[$x$][$x_i$] B}", @"\ce{(NH4)2S}", @"\ce{[\{(X2)3\}2]^3+}",
            @"\ce{CH4 + 2 $\left( \ce{O2 + 79/21 N2} \right)$}", @"\ce{H2(aq)}", @"\ce{CO3^2-_{(aq)}}", @"\ce{NaOH(aq,$\infty$)}", @"\ce{ZnS($c$)}",
            @"\ce{ZnS($x$)}", @"\ce{NO_x}", @"\ce{x Na(NH4)HPO4 ->[\Delta] (NaPO3)_x + x NH3 ^ + x H2O}", @"\ce{\mu-Cl}",
            @"\ce{[Pt(\eta^2-C2H4)Cl3]-}", @"\ce{\beta +}", @"\ce{^40_18Ar + \gamma{} + \nu_e}", @"\ce{NaOH(aq,$\infty$)}", @"\ce{Fe(CN)_{$\frac{6}{2}$}}",
            @"\ce{X_{$i$}^{$x$}}", @"\ce{X_$i$^$x$}", @"\ce{KCr(SO4)2*12H2O}", @"\ce{KCr(SO4)2.12H2O}", @"\ce{KCr(SO4)2 * 12 H2O}", @"\ce{Fe^{II}Fe^{III}2O4}",
            @"\ce{OCO^{.-}}", @"\ce{NO^{(2.)-}}", @"\ce{C6H5-CHO}", @"\ce{A-B=C#D}", @"\ce{A\bond{-}B\bond{=}C\bond{#}D}", @"\ce{A\bond{1}B\bond{2}C\bond{3}D}",
            @"\ce{A\bond{~}B\bond{~-}C}", @"\ce{A\bond{~--}B\bond{~=}C\bond{-~-}D}", @"\ce{A\bond{...}B\bond{....}C}", @"\ce{A\bond{->}B\bond{<-}C}",
            @"\ce{SO4^2- + Ba^2+ -> BaSO4 v}", @"\ce{A v B (v) -> B ^ B (^)}",
            @"\ce{Zn^2+ <=>[+ 2OH-][+ 2H+] $\underset{\text{amphoteric hydroxide}}{\ce{Zn(OH)2 v}}$ <=>[+ 2OH-][+ 2H+] $\underset{\text{hydroxozincate}}{\ce{[Zn(OH)4]^2-}}$}",
            @"\ce{$K = \frac{[\ce{Hg^2+}][\ce{Hg}]}{[\ce{Hg2^2+}]}$}", @"\ce{Hg^2+ ->[I-] $\underset{\mathrm{red}}{\ce{HgI2}}$ ->[I-] $\underset{\mathrm{red}}{\ce{[Hg^{II}I4]^2-}}$}",
            @"\pu{123 kJ}", @"\pu{123 mm2}", @"\pu{123 J s}", @"\pu{123 J*s}", @"\pu{123 kJ/mol}", @"\pu{123 kJ//mol}", @"\pu{123 kJ mol-1}", @"\pu{123 kJ*mol-1}",
            @"\pu{1.2e3 kJ}", @"\pu{1,2e3 kJ}", @"\pu{1.2E3 kJ}", @"\pu{1,2E3 kJ}", @"\pu{1234.5678 km}", @"\pu{12345.6789 km}", @"\ce{A ->[\pu{25 °C}] B}",
            @"\Delta H = \pu{-285.8 kJ//mol}",
        };

        [Test]
        public void EveryDocumentedExampleLaysOut()
        {
            foreach (string source in Documented) Lay(source);
            Assert.GreaterOrEqual(Documented.Length, 80);
        }

        [Test]
        public void AFormulasNumbersAreScriptsOnItsSymbols()
        {
            LetteraMathLayout water = Lay(@"\ce{H2O}");
            Assert.AreEqual(3, water.Items.Length);
            Assert.AreEqual(1, water.Items[0].Scale, 0.001f, "H");
            Assert.Less(water.Items[1].Scale, 0.9f, "the 2 is smaller");
            Assert.Less(water.Items[1].Y, 0, "and lowered");
            Assert.AreEqual(0, water.Items[2].Y, 0.001f, "O on the baseline");
            Assert.Greater(water.Items[2].X, water.Items[1].X, "O after the 2");

            // The 4 and the charge stand one above the other on the same symbol.
            LetteraMathLayout sulfate = Lay(@"\ce{SO4^2-}");
            float under = float.NaN, over = float.NaN;
            foreach (LetteraMathItem item in sulfate.Items)
            {
                if (item.Scale > 0.95f) continue;
                if (item.Y < 0 && float.IsNaN(under)) under = item.X;
                if (item.Y > 0 && float.IsNaN(over)) over = item.X;
            }
            Assert.IsFalse(float.IsNaN(under), "the 4 is lowered");
            Assert.IsFalse(float.IsNaN(over), "the charge is raised");
            Assert.AreEqual(under, over, 0.05f, "both start where O ends");
        }

        [Test]
        public void AnIsotopesNumbersAreFlushRight()
        {
            LetteraMathLayout thorium = Lay(@"\ce{^{227}_{90}Th}");
            float upRight = float.MinValue, downRight = float.MinValue;
            int up = 0, down = 0;
            foreach (LetteraMathItem item in thorium.Items)
            {
                if (item.Scale > 0.95f) continue;
                if (item.Y > 0)
                {
                    up++;
                    upRight = Math.Max(upRight, item.X);
                }
                else
                {
                    down++;
                    downRight = Math.Max(downRight, item.X);
                }
            }
            Assert.AreEqual(3, up, "227 shows");
            Assert.AreEqual(2, down, "90 shows; its padding does not draw");
            Assert.AreEqual(upRight, downRight, 0.01f, "the last digits stand one above the other");
            float symbol = float.MaxValue;
            foreach (LetteraMathItem item in thorium.Items)
                if (item.Scale > 0.95f)
                    symbol = Math.Min(symbol, item.X);
            Assert.Greater(symbol, upRight, "the symbol comes after its numbers");
        }

        [Test]
        public void AReactionArrowIsLongAndCarriesItsTexts()
        {
            float a = Lay(@"\mathrm{A}").Width, b = Lay(@"\mathrm{B}").Width;
            LetteraMathLayout bare = Lay(@"\ce{A -> B}");
            Assert.GreaterOrEqual(bare.Width, a + b + LetteraChemistry.ArrowEm - 0.001f, "at least its least length");
            LetteraMathLayout plain = Lay(@"\mathrm{A}\xrightarrow{}\mathrm{B}");
            Assert.Greater(bare.Width, plain.Width, "longer than a formula's own arrow");

            LetteraMathLayout texts = Lay(@"\ce{A ->[{a long text above}][below] B}");
            Assert.Greater(texts.Width, bare.Width, "a long text lengthens it");
            Assert.Greater(texts.Ascent, bare.Ascent, "text above");
            Assert.Greater(texts.Descent, bare.Descent, "text below");

            foreach (string arrow in new[] { "<-", "<->", "<-->", "<=>", "<=>>", "<<=>" })
                Assert.GreaterOrEqual(Lay(@"\ce{A " + arrow + " B}").Width, a + b + LetteraChemistry.ArrowEm - 0.001f, arrow);
        }

        [Test]
        public void AnUnbalancedEquilibriumHasAShortHarpoon()
        {
            // Outside \ce the long-harpoon names are no commands.
            Assert.IsNotNull(LetteraMathParser.Parse(@"\cequilibriumright{}").Error);
            LetteraMathLayout even = Lay(@"\ce{A <=> B}"), right = Lay(@"\ce{A <=>> B}");
            Assert.AreEqual(even.Width, right.Width, 0.01f, "the long one sets the width");
            Assert.IsNotNull(LetteraMathParser.Parse(@"\ce{A <<=> B}"));
        }

        [Test]
        public void ChargesAndBondsAreDifferentThings()
        {
            // A bond stands on the baseline between atoms, a charge rises over the last one.
            LetteraMathLayout bond = Lay(@"\ce{C-H}");
            LetteraMathLayout charge = Lay(@"\ce{CH-}");
            Assert.AreEqual(0, bond.Items[1].Y, 0.001f);
            Assert.AreEqual(1, bond.Items[1].Scale, 0.001f);
            Assert.AreEqual(3, charge.Items.Length);
            LetteraMathItem mark = charge.Items[0].Scale < 0.9f ? charge.Items[0] : charge.Items[1].Scale < 0.9f ? charge.Items[1] : charge.Items[2];
            Assert.Greater(mark.Y, 0);
            Assert.Less(mark.Scale, 0.9f);
        }

        [Test]
        public void QuantitiesKeepTheirSpacing()
        {
            float number = Lay("123").Width, unit = Lay(@"\mathrm{kJ}").Width;
            Assert.AreEqual(number + 3f / 18 + unit, Lay(@"\pu{123 kJ}").Width, 0.01f, "a thin space between number and unit");
            float dot = Lay(@"\pu{J*s}").Width, space = Lay(@"\pu{J s}").Width;
            Assert.Greater(dot, space, "the multiplication dot takes room of its own");
            LetteraMathLayout fraction = Lay(@"\pu{123 kJ//mol}");
            bool bar = false;
            foreach (LetteraMathItem item in fraction.Items) bar |= item.Kind == MathItemKind.Rule;
            Assert.IsTrue(bar, "// makes a fraction");
            LetteraMathLayout power = Lay(@"\pu{1.2e3 kJ}");
            Assert.Greater(power.Ascent, Lay("1.2").Ascent, "the power of ten rises");
            LetteraMathLayout grouped = Lay(@"\pu{12345 m}");
            Assert.AreEqual(Lay("12345").Width + 2 * 3f / 18 + Lay(@"\mathrm{m}").Width, grouped.Width, 0.01f, "12 345, then a thin space, then m");
        }

        [Test]
        public void ItWorksInAText()
        {
            LetteraWorldText text = Make("Water: <math>\\ce{2H2 + O2 -> 2H2O}</math>, energy <math>\\pu{-285.8 kJ//mol}</math>.");
            var formulas = new List<LetteraFormulaInfo>();
            text.GetFormulas(formulas);
            Assert.AreEqual(2, formulas.Count);
            foreach (LetteraFormulaInfo formula in formulas)
            {
                Assert.IsFalse(formula.Failed, formula.Source + ": " + formula.Message);
                Assert.Greater(formula.Box.width, 0);
            }
        }

        [Test]
        public void ItDraws()
        {
            LetteraFontAsset saved = LetteraSettings.Instance.Math.Font;
            try
            {
                LetteraSettings.Instance.Math.Font = LetteraMathMenu.BundledFont();
                LetteraMathCache.Clear();
                string source = string.Join("\n", new[]
                {
                    @"<math>\ce{CO2 + C -> 2 CO}</math>   <math>\ce{SO4^2- + Ba^2+ -> BaSO4 v}</math>",
                    @"<math>\ce{^{227}_{90}Th+}</math>   <math>\ce{[AgCl2]-}</math>   <math>\ce{KCr(SO4)2*12H2O}</math>   <math>\ce{C6H5-CHO}</math>   <math>\ce{A-B=C#D}</math>",
                    @"<math>\ce{Zn^2+ <=>[+ 2OH-][+ 2H+] Zn(OH)2 v}</math>   <math>\ce{A ->[{text above}][{text below}] B}</math>",
                    @"<math>\ce{A <=>> B <<=> C <--> D <-> E <- F}</math>",
                    @"<math>\ce{NaOH(aq,$\infty$)}</math>   <math>\ce{x Na(NH4)HPO4 ->[\Delta] (NaPO3)_x + x NH3 ^}</math>",
                    @"<math>\pu{123 kJ//mol}</math>   <math>\pu{1.2e3 kJ*mol-1}</math>   <math>\pu{12345.6789 km}</math>   <math>\pu{25 °C}</math>   <math>\pu{9.81 m s^-2}</math>",
                });
                LetteraWorldText label = Make(source, 760, 560, null, 24);
                label.Color = Color.white;
                label.ForceRebuild();
                var formulas = new List<LetteraFormulaInfo>();
                label.GetFormulas(formulas);
                foreach (LetteraFormulaInfo formula in formulas) Assert.IsFalse(formula.Failed, formula.Source + ": " + formula.Message);
                Texture2D image = Render(800, 600, Color.black);
                int lit = 0;
                foreach (Color32 pixel in image.GetPixels32())
                    if (pixel.r > 128)
                        lit++;
                Assert.Greater(lit, 3000, "it draws");
                string folder = System.Environment.GetEnvironmentVariable("LETTERA_SHOTS");
                if (!string.IsNullOrEmpty(folder))
                {
                    System.IO.Directory.CreateDirectory(folder);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, "chemistry.png"), image.EncodeToPNG());
                }
            }
            finally
            {
                LetteraSettings.Instance.Math.Font = saved;
                LetteraMathCache.Clear();
            }
        }

        // ---------------------------------------------------------------- what it refuses

        [TestCase(@"\ce{A $x}", "\\ce")]
        [TestCase(@"\ce{A ->[x B}", "bracket")]
        [TestCase(@"\ce{A^}", "nothing after")]
        [TestCase(@"\pu{1 $m}", "\\pu")]
        [TestCase(@"\ce{\nosuch}", "no command")]
        [TestCase(@"\ce{A \\ B}", "line break")]
        [TestCase(@"\ce", "group")]
        public void AFailureNamesTheCommandAndItsPlace(string source, string part)
        {
            MathTree tree = LetteraMathParser.Parse("x + " + source);
            Assert.IsNotNull(tree.Error, source);
            StringAssert.Contains(part, tree.Error);
            Assert.AreEqual(4, tree.ErrorPosition, "where the command stands in what was written");
        }

        [Test]
        public void NestingIsBounded()
        {
            var deep = new StringBuilder("A ");
            for (int i = 0; i < 40; i++) deep.Append("->[");
            for (int i = 0; i < 40; i++) deep.Append(']');
            Assert.IsNotNull(LetteraMathParser.Parse(@"\ce{" + deep + "}").Error);
            var nested = new StringBuilder();
            for (int i = 0; i < 40; i++) nested.Append(@"\ce{$");
            for (int i = 0; i < 40; i++) nested.Append(@"$}");
            Assert.IsNotNull(LetteraMathParser.Parse(nested.ToString()).Error, "chemistry in math in chemistry, too deep");
            Assert.IsNull(LetteraMathParser.Parse(@"\ce{A ->[->[B]] C}").Error);
        }

        [Test]
        public void RandomInputNeverThrows()
        {
            var random = new System.Random(1234);
            string[] tokens = { "H", "2", "O", "+", "-", "=", "#", "^", "_", "{", "}", "[", "]", "(", ")", "$", "->", "<=>", " ", ".", "*", "\\bond", "\\mu", "v", "e", "1/2", "/", "//", "°", "\\ce{", "\\pu{", "'", "\\" };
            for (int run = 0; run < 20000; run++)
            {
                var text = new StringBuilder(run % 2 == 0 ? "\\ce{" : "\\pu{");
                int count = random.Next(0, 24);
                for (int i = 0; i < count; i++)
                {
                    if (random.Next(5) == 0) text.Append((char)random.Next(1, 0xFFFF));
                    else text.Append(tokens[random.Next(tokens.Length)]);
                }
                text.Append('}');
                string source = text.ToString();
                Assert.DoesNotThrow(() => LetteraMathParser.Parse(source), source);
                Assert.DoesNotThrow(() => LetteraChemistry.Translate(source, run % 3 == 0, out _, out _), source);
            }
        }

        static double Cost(string source)
        {
            double best = double.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                var clock = Stopwatch.StartNew();
                LetteraMathParser.Parse(source);
                clock.Stop();
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        static string Repeat(string open, string token, string close, int length)
        {
            var text = new StringBuilder(open, length + token.Length);
            while (text.Length + token.Length + close.Length <= length) text.Append(token);
            return text.Append(close).ToString();
        }

        [TestCase("\\ce{", "H2O + ", "}")]
        [TestCase("\\ce{", "->[", "}")]
        [TestCase("\\ce{", "{", "}")]
        [TestCase("\\ce{", "$", "}")]
        [TestCase("\\ce{", "^", "}")]
        [TestCase("\\ce{", "-", "}")]
        [TestCase("\\ce{", "[", "}")]
        [TestCase("\\ce{", "\\ce{", "}")]
        [TestCase("\\pu{", "1", "}")]
        [TestCase("\\pu{", "m2 ", "}")]
        [TestCase("\\pu{", "//", "}")]
        public void AdversarialChemistryStaysLinear(string open, string token, string close)
        {
            string small = Repeat(open, token, close, 8000), large = Repeat(open, token, close, 16000);
            Cost(small);
            double a = Cost(small), b = Cost(large);
            Assert.Less(b, Math.Max(a * 2.5, a + 2), $"{token}: {a:0.000} ms then {b:0.000} ms");
        }
    }
}
#endif
