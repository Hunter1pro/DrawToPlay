using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The Level Manifest's selection of several rows and its Copy / Paste / Duplicate: click, Shift-click a range,
    /// Ctrl/Cmd-click one more — in the list or on the handles in the Scene view — then Ctrl/Cmd+C, V, D or the row's
    /// right-click menu. A paste lands in the manifest or visit picked (<see cref="LevelManifestClipboard"/>).
    /// </summary>
    internal sealed partial class LevelManifestWindow
    {
        public const string PickedClass = "dtp-manifest-picked";
        public const string NoteClass = "dtp-manifest-paste-note";

        /// <summary>The picked rows' indices in the edited manifest; <see cref="m_Selected"/> is the one shown.</summary>
        [SerializeField] private List<int> m_Picked = new List<int>();

        /// <summary>What the last paste refused, shown above the list until the next pick or paste.</summary>
        private string m_Note;

        internal IReadOnlyList<int> picked => m_Picked;

        internal string note => m_Note;

        /// <summary>A click on a row: alone, Shift for the range from the shown row, Ctrl/Cmd to add or take one.</summary>
        internal void Pick(int index, EventModifiers modifiers)
        {
            m_Note = null;
            bool add = (modifiers & (EventModifiers.Control | EventModifiers.Command)) != 0;
            bool range = (modifiers & EventModifiers.Shift) != 0;
            SanitizePicked();
            if (range && m_Selected >= 0)
            {
                List<int> shown = ShownOrder();
                int from = shown.IndexOf(m_Selected);
                int to = shown.IndexOf(index);
                if (from >= 0 && to >= 0)
                {
                    if (!add)
                        m_Picked.Clear();
                    for (int i = Mathf.Min(from, to); i <= Mathf.Max(from, to); i++)
                    {
                        if (!m_Picked.Contains(shown[i]))
                            m_Picked.Add(shown[i]);
                    }
                    Rebuild();
                    return;
                }
            }
            if (add)
            {
                if (m_Picked.Count == 0 && m_Selected >= 0)
                    m_Picked.Add(m_Selected);
                if (m_Picked.Remove(index))
                    m_Selected = m_Picked.Count > 0 ? m_Picked[m_Picked.Count - 1] : -1;
                else
                {
                    m_Picked.Add(index);
                    m_Selected = index;
                }
                Rebuild();
                return;
            }
            m_Picked.Clear();
            m_Picked.Add(index);
            m_Selected = index;
            Rebuild();
        }

        private void ClearPicked()
        {
            m_Picked.Clear();
            m_Note = null;
        }

        private bool IsPicked(int index) => index == m_Selected || m_Picked.Contains(index);

        /// <summary>The rows in the order the list shows them — groups, then names.</summary>
        private List<int> ShownOrder()
        {
            var order = new List<int>();
            foreach ((Foldout _, string _, List<(VisualElement line, int index)> lines) in m_Groups)
            {
                foreach ((VisualElement _, int index) in lines)
                    order.Add(index);
            }
            return order;
        }

        private void SanitizePicked()
        {
            int count = Objects != null ? Objects.entries.Count : 0;
            m_Picked.RemoveAll(i => i < 0 || i >= count);
            if (m_Selected >= count)
                m_Selected = -1;
        }

        /// <summary>The picked rows, in the manifest's order; the shown row alone when nothing else is picked.</summary>
        private List<LevelObjectDef> PickedRows()
        {
            var rows = new List<LevelObjectDef>();
            if (m_Level == null || Objects == null)
                return rows;
            SanitizePicked();
            List<LevelObjectDef> entries = Objects.entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && IsPicked(i))
                    rows.Add(entries[i]);
            }
            return rows;
        }

        /// <summary>The picked rows onto the clipboard; how many.</summary>
        internal int CopyPicked()
        {
            List<LevelObjectDef> rows = PickedRows();
            return LevelManifestClipboard.Copy(Objects, LevelKey(), rows) != null ? rows.Count : 0;
        }

        /// <summary>A row of the level, shown on a visit, onto the clipboard.</summary>
        internal bool CopyBaseRow(LevelObjectDef row)
        {
            LevelObjectRegistry visit = Objects;
            return visit != null && visit.isVisit
                && LevelManifestClipboard.Copy(visit.visitOf, LevelKey(), new[] { row }) != null;
        }

        /// <summary>The clipboard's rows into the manifest or visit picked, one undo step; the new rows picked.</summary>
        internal List<LevelObjectDef> Paste() => PasteClip(LevelManifestClipboard.Read());

        /// <summary>The picked rows copied beside themselves, without touching the clipboard.</summary>
        internal List<LevelObjectDef> Duplicate()
        {
            List<LevelObjectDef> rows = PickedRows();
            if (rows.Count == 0)
                return new List<LevelObjectDef>();
            var clip = new LevelManifestClipboard.Clip
            {
                source = LevelManifestClipboard.SourceOf(Objects),
                levelKey = LevelKey(),
                rows = rows
            };
            return PasteClip(clip);
        }

        private List<LevelObjectDef> PasteClip(LevelManifestClipboard.Clip clip)
        {
            var added = new List<LevelObjectDef>();
            if (m_Level == null || Objects == null || clip == null || VisitLacksManifest(out _))
                return added;
            var refused = new List<string>();
            added = LevelManifestClipboard.Paste(Objects, Kinds(), LevelKey(), clip, refused);
            m_Note = refused.Count > 0 ? string.Join("\n", refused) : null;
            if (added.Count > 0)
            {
                m_Picked.Clear();
                foreach (LevelObjectDef row in added)
                {
                    m_Picked.Add(Objects.entries.IndexOf(row));
                    SetFolded(row.kind.entryName, false);
                }
                m_Selected = m_Picked[m_Picked.Count - 1];
            }
            Rebuild();
            SceneView.RepaintAll();
            return added;
        }

        /// <summary>Copy, Paste and Duplicate as the Editor sends them — from the keyboard or the Edit menu — to this
        /// window, or to the Scene view while no GameObject is selected there. True when it was handled.</summary>
        private bool Command(string name, bool execute)
        {
            if (m_Level == null || Objects == null)
                return false;
            switch (name)
            {
                case "Copy":
                    if (PickedRows().Count == 0)
                        return false;
                    if (execute)
                        CopyPicked();
                    return true;
                case "Paste":
                    if (VisitLacksManifest(out _) || !LevelManifestClipboard.HasRows())
                        return false;
                    if (execute)
                        Paste();
                    return true;
                case "Duplicate":
                    if (PickedRows().Count == 0)
                        return false;
                    if (execute)
                        Duplicate();
                    return true;
                default:
                    return false;
            }
        }

        private void RegisterCommands(VisualElement root)
        {
            root.RegisterCallback<ValidateCommandEvent>(evt =>
            {
                if (!InText(evt.target) && Command(evt.commandName, false))
                {
                    evt.StopPropagation();
                    evt.imguiEvent?.Use();
                }
            });
            root.RegisterCallback<ExecuteCommandEvent>(evt =>
            {
                if (!InText(evt.target) && Command(evt.commandName, true))
                {
                    evt.StopPropagation();
                    evt.imguiEvent?.Use();
                }
            });
        }

        /// <summary>A command meant for a text field being typed in — its own copy and paste.</summary>
        private static bool InText(IEventHandler target)
        {
            for (var element = target as VisualElement; element != null; element = element.parent)
            {
                if (element.ClassListContains(TextField.ussClassName) || element.ClassListContains("unity-base-text-field"))
                    return true;
            }
            return false;
        }

        /// <summary>The Scene view's Copy / Paste / Duplicate, while the picked rows stand in for a selection there.</summary>
        private void SceneCommands()
        {
            Event current = Event.current;
            if (current.type != EventType.ValidateCommand && current.type != EventType.ExecuteCommand)
                return;
            if (Selection.gameObjects.Length > 0)
                return;
            if (Command(current.commandName, current.type == EventType.ExecuteCommand))
                current.Use();
        }

        /// <summary>A row's right-click: Copy, Paste, Duplicate.</summary>
        private void RowMenu(VisualElement line, int index)
        {
            line.AddManipulator(new ContextualMenuManipulator(menu =>
            {
                if (!IsPicked(index))
                {
                    m_Note = null;
                    m_Picked.Clear();
                    m_Picked.Add(index);
                    m_Selected = index;
                    rootVisualElement.schedule.Execute(Rebuild);
                }
                int count = PickedRows().Count;
                string what = count > 1 ? " " + count + " objects" : "";
                menu.menu.AppendAction("Copy" + what, _ => CopyPicked());
                menu.menu.AppendAction("Paste", _ => Paste(),
                    LevelManifestClipboard.HasRows() ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                menu.menu.AppendAction("Duplicate" + what, _ => Duplicate());
            }));
        }

        /// <summary>Paste on the list's empty space too — a manifest or visit with no rows yet.</summary>
        private void ListMenu(VisualElement list)
        {
            list.AddManipulator(new ContextualMenuManipulator(menu =>
            {
                if (menu.menu.MenuItems().Count > 0)
                    return;
                menu.menu.AppendAction("Paste", _ => Paste(),
                    LevelManifestClipboard.HasRows() ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }));
        }

        /// <summary>What the last paste refused, inline.</summary>
        private void AddNote(VisualElement list)
        {
            if (string.IsNullOrEmpty(m_Note))
                return;
            var box = new HelpBox(m_Note, HelpBoxMessageType.Info);
            box.AddToClassList(NoteClass);
            list.Add(box);
        }

        /// <summary>A ring under each picked row but the shown one, which has the move handle.</summary>
        private void DrawPicked(LevelObjectRegistry manifest)
        {
            if (Event.current.type != EventType.Repaint || m_Picked.Count == 0)
                return;
            Vector3 up = manifest.ToWorld(Vector2.zero, 1f) - manifest.ToWorld(Vector2.zero);
            using (new Handles.DrawingScope(new Color(1f, 0.85f, 0.3f)))
            {
                foreach (int index in m_Picked)
                {
                    if (index == m_Selected || index < 0 || index >= manifest.entries.Count || manifest.entries[index] == null)
                        continue;
                    Vector3 world = manifest.ToWorld(manifest.entries[index].position);
                    Handles.DrawWireDisc(world, up, HandleUtility.GetHandleSize(world) * 0.25f, 2f);
                }
            }
        }

        /// <summary>A picked row's line, besides the shown one in bold.</summary>
        private static void DressPicked(VisualElement line)
        {
            line.AddToClassList(PickedClass);
            line.style.backgroundColor = new Color(0.24f, 0.48f, 0.9f, 0.35f);
        }
    }
}
