using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Tests
{
    public class CanvasTextTests
    {
        Canvas m_Canvas;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            m_Canvas = go.GetComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(m_Canvas.gameObject);
        }

        LetteraText Make(string content)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(m_Canvas.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(400, 100);
            var text = go.AddComponent<LetteraText>();
            text.Text = content;
            return text;
        }

        [UnityTest]
        public IEnumerator TextRebuildsOnItsOwn()
        {
            LetteraText text = Make("Hello");
            yield return null;
            yield return null;
            Assert.AreEqual(5, text.GlyphCount);
            Assert.Greater(text.RebuildCount, 0);
            Assert.IsNotNull(text.GetComponent<CanvasRenderer>());
        }

        [UnityTest]
        public IEnumerator CanvasGetsTheShaderChannelTheTextNeeds()
        {
            Make("x");
            yield return null;
            Assert.AreNotEqual((AdditionalCanvasShaderChannels)0, m_Canvas.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1);
        }

        [UnityTest]
        public IEnumerator StyledTextAsksForTheStreamsItNeeds()
        {
            const AdditionalCanvasShaderChannels extra = AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;
            Make("plain");
            yield return null;
            Assert.AreEqual((AdditionalCanvasShaderChannels)0, m_Canvas.additionalShaderChannels & extra, "Plain text does not need them.");
            LetteraText styled = Make("<inner-shadow>x</inner-shadow>");
            yield return null;
            yield return null;
            Assert.AreEqual(extra, m_Canvas.additionalShaderChannels & extra);
            Assert.IsTrue(styled.materialForRendering.IsKeywordEnabled("LETTERA_STYLED"));
        }

        [UnityTest]
        public IEnumerator DifferentPaintsShareOneMaterial()
        {
            var a = new FillEffect();
            a.SetPaint(LetteraPaint.Of(LetteraGradient.Of(Color.red, Color.blue)));
            var b = new FillEffect();
            b.SetPaint(LetteraPaint.Of(LetteraGradient.Of(Color.green, Color.white)));
            LetteraText first = Make("");
            first.Styles.Add(LetteraStyle.ForWholeText(a));
            first.Text = "one";
            LetteraText second = Make("");
            second.Styles.Add(LetteraStyle.ForWholeText(b));
            second.Text = "two";
            yield return null;
            yield return null;
            Assert.AreSame(first.materialForRendering, second.materialForRendering);
        }

        [UnityTest]
        public IEnumerator EachStretchGetsItsOwnMaterialInOrder()
        {
            var texture = new Texture2D(1, 1);
            var fill = new FillEffect();
            fill.SetPaint(LetteraPaint.Of(texture));
            LetteraText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(fill, "k"));
            text.Text = "a<k>b</k>c";
            yield return null;
            yield return null;
            CanvasRenderer renderer = text.GetComponent<CanvasRenderer>();
            Assert.AreEqual(3, renderer.materialCount);
            Assert.IsFalse(renderer.GetMaterial(0).GetTexture("_LetteraPaintTex") == texture);
            Assert.AreSame(texture, renderer.GetMaterial(1).GetTexture("_LetteraPaintTex"));
            text.Text = "abc";
            yield return null;
            yield return null;
            Assert.AreEqual(1, renderer.materialCount, "Back to one when the texture is gone.");
            Object.Destroy(texture);
        }

        [UnityTest]
        public IEnumerator SegmentsKeepTheirStencilUnderAMask()
        {
            var maskObject = new GameObject("Mask", typeof(RectTransform), typeof(Image), typeof(Mask));
            maskObject.transform.SetParent(m_Canvas.transform, false);
            var texture = new Texture2D(1, 1);
            var fill = new FillEffect();
            fill.SetPaint(LetteraPaint.Of(texture));
            LetteraText text = Make("");
            text.transform.SetParent(maskObject.transform, false);
            text.Styles.Add(LetteraStyle.ForTag(fill, "k"));
            text.Text = "a<k>b</k>";
            yield return null;
            yield return null;
            CanvasRenderer renderer = text.GetComponent<CanvasRenderer>();
            Assert.AreEqual(2, renderer.materialCount);
            Assert.Greater(renderer.GetMaterial(1).GetFloat("_Stencil"), 0f);
            Object.Destroy(texture);
        }

        [UnityTest]
        public IEnumerator SteadyFramesDoNotRebuild()
        {
            LetteraText text = Make("Steady");
            yield return null;
            yield return null;
            int rebuilds = text.RebuildCount;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(rebuilds, text.RebuildCount);
        }

        [UnityTest]
        public IEnumerator AlphaAnimationKeepsGeometry()
        {
            LetteraText text = Make("Fade");
            LetteraChanges last = LetteraChanges.None;
            yield return null;
            text.Rebuilt += c => last = c;
            for (int i = 1; i <= 5; i++)
            {
                text.color = new Color(1, 1, 1, 1f - i * 0.1f);
                yield return null;
                Assert.AreEqual(LetteraChanges.Mesh, last, "only vertex colors are rewritten");
            }
        }

        [UnityTest]
        public IEnumerator ContentSizeFitterSizesTheText()
        {
            LetteraText text = Make("Fit me");
            text.WordWrap = false;
            var fitter = text.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            yield return null;
            yield return null;
            var rect = (RectTransform)text.transform;
            Assert.Greater(rect.rect.width, 10);
            Assert.AreEqual(text.preferredWidth, rect.rect.width, 0.5f);
            Assert.AreEqual(text.preferredHeight, rect.rect.height, 0.5f);
        }

        [UnityTest]
        public IEnumerator LayoutGroupReadsPreferredSizes()
        {
            var group = m_Canvas.gameObject.AddComponent<VerticalLayoutGroup>();
            group.childControlHeight = true;
            group.childControlWidth = true;
            LetteraText a = Make("One");
            LetteraText b = Make("Two");
            yield return null;
            yield return null;
            Assert.Greater(((RectTransform)a.transform).rect.height, 10);
            Assert.AreNotEqual(((RectTransform)a.transform).anchoredPosition.y, ((RectTransform)b.transform).anchoredPosition.y);
        }

        [UnityTest]
        public IEnumerator HiddenTextIgnoresPointers()
        {
            LetteraText text = Make("Hidden");
            yield return null;
            Assert.IsTrue(text.Raycast(Vector2.zero, null));
            text.Hide();
            Assert.IsFalse(text.Raycast(Vector2.zero, null));
            text.Show();
            Assert.IsTrue(text.Raycast(Vector2.zero, null));
        }

        [UnityTest]
        public IEnumerator DisablingAndEnablingRebuilds()
        {
            LetteraText text = Make("Toggle");
            yield return null;
            yield return null;
            text.enabled = false;
            yield return null;
            text.enabled = true;
            yield return null;
            yield return null;
            Assert.AreEqual(6, text.GlyphCount);
        }

        [UnityTest]
        public IEnumerator WorldTextsMergeUnlessStandalone()
        {
            var a = NewWorldText("a");
            var b = NewWorldText("b");
            yield return null;
            yield return null;
            Assert.AreEqual(1, a.GlyphCount);
            Assert.AreEqual(1, b.GlyphCount);
            Object.Destroy(a.gameObject);
            Object.Destroy(b.gameObject);
        }

        static LetteraWorldText NewWorldText(string content)
        {
            var go = new GameObject("World", typeof(RectTransform));
            var text = go.AddComponent<LetteraWorldText>();
            text.Text = content;
            return text;
        }

        [UnityTest]
        public IEnumerator UpdatingALabelEveryFrameLeavesNoErrors()
        {
            LetteraText text = Make("0");
            for (int i = 0; i < 30; i++)
            {
                text.SetText(i.ToString());
                yield return null;
            }
            Assert.AreEqual("29".Length, text.GlyphCount);
        }
    }
}
