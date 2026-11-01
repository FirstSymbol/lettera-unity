using System.Linq;
using FirstSymbol.Lettera.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.PropertyVariants.TrackedObjects;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Localization.Editor.Tests
{
    public sealed class LocalizeTests
    {
        GameObject m_Object;
        LetteraText m_Text;

        [SetUp]
        public void SetUp()
        {
            m_Object = new GameObject("Localized", typeof(RectTransform));
            m_Text = m_Object.AddComponent<LetteraText>();
            m_Text.Text = "Saved words";
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(m_Object);

        [Test]
        public void LocalizeWiresTheEventToSetTextAndLeavesTheSavedText()
        {
            LetteraLocalizeMenus.Setup(m_Text);
            var localize = m_Object.GetComponent<LocalizeStringEvent>();
            Assert.IsNotNull(localize);
            Assert.AreEqual(1, localize.OnUpdateString.GetPersistentEventCount());
            Assert.AreEqual(m_Text, localize.OnUpdateString.GetPersistentTarget(0));
            Assert.AreEqual("SetText", localize.OnUpdateString.GetPersistentMethodName(0));
            Assert.AreEqual(UnityEventCallState.EditorAndRuntime, localize.OnUpdateString.GetPersistentListenerState(0));
            localize.OnUpdateString.Invoke("From a table");
            m_Text.ForceRebuild();
            Assert.AreEqual("From a table", m_Text.RenderedSourceText);
            Assert.AreEqual("Saved words", m_Text.Text);
        }

        [Test]
        public void LocalizeTwiceAddsOne()
        {
            LetteraLocalizeMenus.Setup(m_Text);
            LetteraLocalizeMenus.Setup(m_Text);
            Assert.AreEqual(1, m_Object.GetComponents<LocalizeStringEvent>().Length);
        }

        [Test]
        public void OneUndoTakesItAway()
        {
            Undo.IncrementCurrentGroup();
            LetteraLocalizeMenus.Setup(m_Text);
            Undo.PerformUndo();
            Assert.IsNull(m_Object.GetComponent<LocalizeStringEvent>());
        }

        [Test]
        public void TheCardOffersItAndAClickAddsIt()
        {
            VisualElement card = LetteraEditingMenus.InteractionCard(new Object[] { m_Text });
            var chip = card.Query<Label>().ToList().FirstOrDefault(l => l.text == "+ Localize");
            Assert.IsNotNull(chip, "The chip is on the card.");
            Assert.IsNotNull(card.Query<Label>().ToList().FirstOrDefault(l => l.text == "+ Font By Locale"));
            LetteraEditingMenus.ExtraChips.First(c => c.Title == "Localize").Add(m_Text);
            Assert.IsNotNull(m_Object.GetComponent<LocalizeStringEvent>());
            VisualElement after = LetteraEditingMenus.InteractionCard(new Object[] { m_Text });
            Assert.IsNull(after.Query<Label>().ToList().FirstOrDefault(l => l.text == "+ Localize"), "Once there it offers to go to it.");
            Assert.IsNotNull(after.Query<Label>().ToList().FirstOrDefault(l => l.text == "Localize"));
        }

        [Test]
        public void TheContextMenuHoldsIt()
        {
            foreach (string kind in new[] { "LetteraText", "LetteraWorldText", "LetteraText3D" })
                Assert.IsTrue(Menu.GetEnabled("CONTEXT/" + kind + "/Localize") || Unsupported.GetSubmenus("CONTEXT/" + kind).Any(m => m.EndsWith("/Localize")), kind);
        }

        [Test]
        public void TrackedTextAppliesAFieldWrittenWithNoSetter()
        {
            var tracked = new LetteraTrackedText { Target = m_Text };
            m_Text.ForceRebuild();
            JsonUtility.FromJsonOverwrite("{\"m_Data\":{\"Text\":\"Other\",\"FontSize\":61.0}}", m_Text);
            System.Reflection.MethodInfo post = typeof(LetteraTrackedTextBase).GetMethod("PostApplyTrackedProperties", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            post.Invoke(tracked, null);
            m_Text.ForceRebuild();
            Assert.AreEqual("Other", m_Text.RenderedSourceText);
            Assert.AreEqual(61f, m_Text.FontSize);
        }

        [Test]
        public void TheFontComponentLeavesASceneAloneInEditMode()
        {
            var font = m_Object.AddComponent<LetteraLocalizeFont>();
            LetteraFontAsset before = m_Text.Font;
            System.Reflection.MethodInfo update = typeof(LetteraLocalizeFont).GetMethod("UpdateAsset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var other = ScriptableObject.CreateInstance<LetteraFontStack>();
            update.Invoke(font, new object[] { other });
            Object.DestroyImmediate(other);
            Assert.AreEqual(before, m_Text.Font);
            Assert.IsNull(m_Text.FontStack);
        }
    }
}
