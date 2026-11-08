using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>
    /// Groups the undo records of one gesture into one step. A drag, a colour picker session and typing are many writes; a click is one.
    /// A step closes when the pointer is released, or after a pause in the writes.
    /// </summary>
    internal sealed class LtGesture
    {
        readonly VisualElement m_Owner;
        readonly int m_IdleMs;
        int m_Group;
        bool m_Open, m_Held;
        IVisualElementScheduledItem m_Idle;

        public LtGesture(VisualElement owner, int idleMs = 600)
        {
            m_Owner = owner;
            m_IdleMs = idleMs;
        }

        public bool IsOpen => m_Open;
        public bool IsHeld => m_Held;

        /// <summary>While held (a pointer is down on a slider) the step stays open.</summary>
        public void Hold(bool held)
        {
            m_Held = held;
            if (!held && m_Open) Arm();
        }

        /// <summary>Runs a write inside the step. A discrete one (a click, a pick) closes it at once.</summary>
        public void Run(Action write, bool discrete = false)
        {
            if (m_Open && discrete) Close();
            if (!m_Open)
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Edit tag parameter");
                m_Group = Undo.GetCurrentGroup();
                m_Open = true;
            }
            write();
            if (discrete) Close();
            else if (!m_Held) Arm();
        }

        void Arm()
        {
            m_Idle?.Pause();
            m_Idle = m_Owner.schedule.Execute(Close);
            m_Idle.ExecuteLater(m_IdleMs);
        }

        public void Close()
        {
            m_Idle?.Pause();
            if (!m_Open) return;
            m_Open = false;
            Undo.CollapseUndoOperations(m_Group);
        }
    }

    /// <summary>The rows of one tag's slots. The panel and the style card share it; each decides where an edit is written.</summary>
    internal sealed class LtTagForm : VisualElement
    {
        readonly Func<float> m_EmPixels;
        readonly VisualElement m_List;
        readonly VisualElement m_Extras;
        readonly List<LtSlotRow> m_Rows = new List<LtSlotRow>();
        IReadOnlyList<LetteraParamInfo> m_Described;

        /// <summary>A slot was changed: its position and the new token, or null for inherit.</summary>
        public event Action<int, string> SlotChanged;
        public event Action<bool> Holding;

        public IReadOnlyList<LtSlotRow> Rows => m_Rows;

        public LtTagForm(Func<float> emPixels)
        {
            m_EmPixels = emPixels;
            AddToClassList("lt-tagform");
            m_List = LtUI.El("lt-tagform__rows");
            m_Extras = LtUI.El("lt-tagform__extras");
            Add(m_List);
            Add(m_Extras);
        }

        public void Show(IReadOnlyList<LetteraParamInfo> described, List<LetteraSlotRow> rows, List<string> extras)
        {
            if (!ReferenceEquals(described, m_Described) || m_Rows.Count != rows.Count) Rebuild(described, rows);
            for (int i = 0; i < rows.Count; i++) m_Rows[i].Bind(rows[i]);
            m_Extras.Clear();
            if (extras != null)
                foreach (string extra in extras)
                {
                    Label chip = LtUI.Text(extra, "lt-chip lt-tagform__extra");
                    chip.tooltip = "A value past the slots this tag describes. It is kept as written.";
                    m_Extras.Add(chip);
                }
        }

        void Rebuild(IReadOnlyList<LetteraParamInfo> described, List<LetteraSlotRow> rows)
        {
            m_Described = described;
            m_List.Clear();
            m_Rows.Clear();
            foreach (LetteraSlotRow model in rows)
            {
                var row = new LtSlotRow(model.Info, m_EmPixels);
                row.Changed += (r, token) => SlotChanged?.Invoke(r.Info.Slot, token);
                row.Holding += hold => Holding?.Invoke(hold);
                m_List.Add(row);
                m_Rows.Add(row);
            }
        }

        public LtSlotRow RowOf(int slot)
        {
            foreach (LtSlotRow row in m_Rows)
                if (row.Info.Slot == slot)
                    return row;
            return null;
        }

        public bool Editing
        {
            get
            {
                foreach (LtSlotRow row in m_Rows)
                    if (row.Editing)
                        return true;
                return false;
            }
        }

        public void Mark(int slot)
        {
            foreach (LtSlotRow row in m_Rows) row.EnableInClassList("lt-slot--caret", row.Info.Slot == slot);
        }

        public void FocusSlot(int slot) => (RowOf(slot) ?? (m_Rows.Count > 0 ? m_Rows[0] : null))?.FocusControl();
    }
}
