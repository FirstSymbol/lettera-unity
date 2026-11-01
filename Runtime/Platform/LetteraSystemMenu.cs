using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>The system's menu in place of Lettera's. Every entry still runs Lettera's command, so rules and undo hold.</summary>
    [AddComponentMenu("")]
    internal sealed class LetteraSystemMenu : LetteraMenuPresenter
    {
        ILetteraSystemUi m_Ui;
        Action<int> m_Invoke;
        bool m_Visible;

        static LetteraSystemMenu s_Instance;

        /// <summary>The one instance, when this system can show a menu and the project chose it. Null otherwise.</summary>
        internal static LetteraSystemMenu Resolve(ILetteraSystemUi ui)
        {
            if (ui == null || !ui.MenuSupported) return null;
            if (s_Instance == null)
            {
                var host = new GameObject("Lettera System Menu") { hideFlags = HideFlags.HideAndDontSave };
                s_Instance = host.AddComponent<LetteraSystemMenu>();
            }
            s_Instance.m_Ui = ui;
            return s_Instance;
        }

        public override bool IsVisible => m_Visible;

        public override void Show(in LetteraMenuPlacement placement, IReadOnlyList<LetteraMenuEntry> entries, Action<int> invoke)
        {
            m_Invoke = invoke;
            var titles = new string[entries.Count];
            var enabled = new bool[entries.Count];
            for (int i = 0; i < titles.Length; i++)
            {
                titles[i] = entries[i].Title;
                enabled[i] = entries[i].IsEnabled;
            }
            Rect avoid = placement.Avoid.size != Vector2.zero ? placement.Avoid : new Rect(placement.Point, Vector2.one);
            m_Visible = true;
            m_Ui.ShowMenu(avoid, titles, enabled);
        }

        public override void Hide()
        {
            if (!m_Visible) return;
            m_Visible = false;
            m_Ui.HideMenu();
        }

        void Update()
        {
            if (!m_Visible || m_Ui == null) return;
            int choice = m_Ui.NextMenuChoice();
            if (choice == -2) return;
            m_Visible = false;
            if (choice >= 0) m_Invoke?.Invoke(choice);
        }

        /// <summary>Runs one frame's worth, for a test.</summary>
        internal void TickForTests() => Update();
    }
}
