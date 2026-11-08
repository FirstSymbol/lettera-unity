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
        readonly Dictionary<string, FoldedGroup> m_Groups = new Dictionary<string, FoldedGroup>();

        // Slots a descriptor puts in a group (the paint settings of a layer) sit under one heading, closed until one of them is set.
        sealed class FoldedGroup
        {
            public string Name;
            public Label Head;
            public VisualElement Body;
            public bool Open;
            public int Set, Count;
        }

        /// <summary>A slot was changed: its position and the new token, or null for inherit.</summary>
        public event Action<int, string> SlotChanged;
        public event Action<bool> Holding;

        /// <summary>The names to offer for a text slot that has some (an inline item's key), or null.</summary>
        public Func<LetteraParamInfo, IReadOnlyList<string>> Suggest;

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
            foreach (FoldedGroup group in m_Groups.Values) group.Set = 0;
            for (int i = 0; i < rows.Count; i++)
                if (!string.IsNullOrEmpty(rows[i].Info.Group) && rows[i].State != LetteraSlotState.Inherit && m_Groups.TryGetValue(rows[i].Info.Group, out FoldedGroup folded))
                    folded.Set++;
            foreach (FoldedGroup group in m_Groups.Values)
            {
                if (group.Set > 0) group.Open = true;
                ShowGroup(group);
            }
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
            m_Groups.Clear();
            foreach (LetteraSlotRow model in rows)
            {
                var row = new LtSlotRow(model.Info, m_EmPixels, Suggest);
                row.Changed += (r, token) => SlotChanged?.Invoke(r.Info.Slot, token);
                row.Holding += hold => Holding?.Invoke(hold);
                VisualElement parent = m_List;
                if (!string.IsNullOrEmpty(model.Info.Group))
                {
                    if (!m_Groups.TryGetValue(model.Info.Group, out FoldedGroup group))
                    {
                        group = new FoldedGroup { Name = model.Info.Group };
                        FoldedGroup captured = group;
                        group.Head = LtUI.Clickable("", "lt-tagform__grouphead", "Show or hide these slots.", () =>
                        {
                            captured.Open = !captured.Open;
                            ShowGroup(captured);
                        });
                        group.Body = LtUI.El("lt-tagform__groupbody");
                        m_List.Add(group.Head);
                        m_List.Add(group.Body);
                        m_Groups[model.Info.Group] = group;
                    }
                    group.Count++;
                    parent = group.Body;
                }
                parent.Add(row);
                m_Rows.Add(row);
            }
            foreach (FoldedGroup group in m_Groups.Values) ShowGroup(group);
        }

        static void ShowGroup(FoldedGroup group)
        {
            group.Head.text = (group.Open ? "▾ " : "▸ ") + group.Name + " settings" + (group.Set > 0 ? " (" + group.Set + " set)" : "") + "  ·  " + group.Count + " slots";
            group.Body.style.display = group.Open ? DisplayStyle.Flex : DisplayStyle.None;
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

        public void FocusSlot(int slot)
        {
            LtSlotRow row = RowOf(slot) ?? (m_Rows.Count > 0 ? m_Rows[0] : null);
            if (row != null && !string.IsNullOrEmpty(row.Info.Group) && m_Groups.TryGetValue(row.Info.Group, out FoldedGroup group) && !group.Open)
            {
                group.Open = true;
                ShowGroup(group);
            }
            row?.FocusControl();
        }

        /// <summary>Whether a folded group of rows is shown, for tests.</summary>
        public bool GroupOpen(string name) => m_Groups.TryGetValue(name, out FoldedGroup group) && group.Open;
    }
}
