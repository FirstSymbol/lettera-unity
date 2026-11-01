using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>What is on the clipboard, in each format. It reads only when asked.</summary>
    internal sealed class LetteraClipboardWindow : LetteraToolWindow
    {
        [MenuItem("Tools/Lettera/Clipboard", false, 23)]
        static void Open() => GetWindow<LetteraClipboardWindow>("Lettera Clipboard").minSize = new Vector2(320, 300);

        protected override string Title => "Clipboard";
        protected override string Subtitle => "Every format a copy wrote";

        VisualElement m_Items;
        Label m_Provider;

        protected override void Build()
        {
            m_Provider = LtUI.Text("", "lt-hint");
            Page.Add(LtUI.Card("Reading", m_Provider, LtUI.Clickable("Read The Clipboard", "lt-add lt-add--small", "Reads what is there now, and only now.", Read)));
            m_Items = LtUI.El("lt-clip");
            Page.Add(LtUI.Card("Formats", m_Items));
            m_Items.Add(LtUI.Text("Press Read The Clipboard.", "lt-hint"));
            m_Provider.text = "Through " + LetteraClipboard.Backend.GetType().Name;
        }

        void Read()
        {
            m_Items.Clear();
            var items = new List<LetteraClipItem>();
            LetteraClipboard.Backend.Read(items);
            foreach (LetteraClipItem item in items)
            {
                var card = LtUI.El("lt-range");
                card.Add(LtUI.Text($"{item.Format}, {item.Bytes?.Length ?? 0} bytes", "lt-row__value"));
                string text = item.Text ?? "";
                Label body = LtUI.Text(text.Length > 400 ? text.Substring(0, 400) + "…" : text, "lt-hint");
                body.style.whiteSpace = WhiteSpace.Normal;
                card.Add(body);
                m_Items.Add(card);
            }
            if (items.Count == 0) m_Items.Add(LtUI.Text("Nothing a text could read.", "lt-hint"));
        }
    }
}
