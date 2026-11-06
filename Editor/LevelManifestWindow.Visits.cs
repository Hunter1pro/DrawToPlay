using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The visit half of the Level Manifest: which visit of the level is edited (picked beside the level's name, shared
    /// with the Scene view's overlay), what the window edits on it — the visit's own rows, with the level's shown dimmed
    /// as "from" the first visit and the hidden ones crossed out — and the scene drawing what stands on that visit.
    /// </summary>
    internal sealed partial class LevelManifestWindow
    {
        public const string VisitClass = "dtp-manifest-visit";
        public const string BaseRowClass = "dtp-manifest-base-row";
        public const string BaseHiddenClass = "dtp-manifest-base-hidden";

        private List<LevelVisitsEditor.Visit> m_Visits;
        private LevelLinks m_VisitsLinks;
        private readonly List<(VisualElement line, LevelObjectDef row)> m_BaseLines =
            new List<(VisualElement, LevelObjectDef)>();

        /// <summary>The level's rows picked under "From Hall", by id — what Copy and Replace take.</summary>
        private readonly List<string> m_BasePicked = new List<string>();

        internal IReadOnlyList<string> basePicked => m_BasePicked;

        /// <summary>The level's visits, found once per level and again after a pick, an undo or a project change.</summary>
        internal List<LevelVisitsEditor.Visit> Visits()
        {
            if (m_Level == null)
                return new List<LevelVisitsEditor.Visit>();
            if (m_Visits != null && m_VisitsLinks == m_Links && m_Links != null)
                return m_Visits;
            m_Links ??= LevelLinks.Of(m_Level);
            m_VisitsLinks = m_Links;
            return m_Visits = LevelVisitsEditor.Of(m_Links);
        }

        internal LevelVisitsEditor.Visit pickedVisit => LevelVisitsEditor.PickedOf(m_Level, Visits());

        /// <summary>The manifest edited: the picked visit's own, else the level's.</summary>
        private LevelObjectRegistry Objects
        {
            get
            {
                if (m_Level == null)
                    return null;
                LevelVisitsEditor.Visit visit = pickedVisit;
                return visit != null && visit.manifest != null ? visit.manifest : m_Level.objects;
            }
        }

        /// <summary>For tests: the manifest the window edits.</summary>
        internal LevelObjectRegistry edited => Objects;

        /// <summary>A visit was picked, or given a manifest: every open window follows.</summary>
        internal static void VisitPicked()
        {
            foreach (LevelManifestWindow window in Resources.FindObjectsOfTypeAll<LevelManifestWindow>())
            {
                window.m_Visits = null;
                window.m_Selected = -1;
                window.m_Picked.Clear();
                window.m_BasePicked.Clear();
                window.Rebuild();
            }
        }

        /// <summary>"Visit: Hall_2 ▾" — the visits of the level and "+ New visit". Nothing when it is in no episode.</summary>
        private VisualElement BuildVisitRow()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            row.AddToClassList(VisitClass);
            List<LevelVisitsEditor.Visit> visits = Visits();
            if (visits.Count == 0)
                return row;
            LevelVisitsEditor.Visit picked = pickedVisit;
            row.Add(new Label("Visit") { style = { width = 64f, opacity = 0.7f } });
            Button choose = null;
            choose = new Button(() => VisitMenu(m_Level, visits, picked).DropDown(choose.worldBound))
            {
                text = picked.label + "  ▾",
                tooltip = "Which visit of this level you are editing. The scene comes back later in the story: a visit "
                    + "adds things and hides the level's own. The Scene view shows the visit picked here."
            };
            choose.style.flexGrow = 1f;
            choose.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(choose);
            if (Objects != null && Objects.isVisit)
            {
                var note = new VisualElement();
                note.Add(row);
                note.Add(Hint(picked.label + " stands as " + visits[0].label + "'s rows less the hidden, then its own. "
                    + "Right-click a row from " + visits[0].label + " to hide or show it on this visit; the palette adds to "
                    + picked.label + "."));
                return note;
            }
            return row;
        }

        /// <summary>The visits to pick, the picked one ticked, and "+ New visit".</summary>
        internal static GenericMenu VisitMenu(LevelContent level, List<LevelVisitsEditor.Visit> visits,
            LevelVisitsEditor.Visit picked)
        {
            var menu = new GenericMenu();
            foreach (LevelVisitsEditor.Visit visit in visits)
            {
                string id = visit.node.nodeId;
                menu.AddItem(new GUIContent(visit.label), visit == picked, () => LevelVisitsEditor.Pick(level, id));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("+ New visit"), false, () =>
            {
                if (LevelVisitsEditor.NewVisit(level, out string report) == null)
                    Debug.LogWarning("[Level Manifest] No new visit: " + report + ".");
                else
                    Debug.Log("[Level Manifest] " + report + ".");
            });
            return menu;
        }

        /// <summary>A visit after the first that has no manifest of its own stands as the plain level: nothing here edits
        /// it until it has one. The note and its button, when so.</summary>
        private bool VisitLacksManifest(out VisualElement shown)
        {
            shown = null;
            LevelVisitsEditor.Visit visit = pickedVisit;
            if (visit == null || !visit.lacksManifest)
                return false;
            shown = new VisualElement();
            shown.Add(new HelpBox(visit.label + " has no manifest of its own, so on this visit the level stands as it is. "
                + "Make its manifest to add things for " + visit.label + " and hide the level's.", HelpBoxMessageType.Info));
            var make = new Button(() =>
            {
                LevelVisitsEditor.MakeOwnManifest(m_Level, visit);
                Rebuild();
            }) { text = "Make " + visit.label + "'s manifest" };
            make.AddToClassList(VisitClass);
            make.style.alignSelf = Align.FlexStart;
            shown.Add(make);
            return true;
        }

        /// <summary>The level's manifest whose rows stand on the picked visit: under its own manifest, or as the plain
        /// level while it has none. Null on the first visit.</summary>
        private LevelObjectRegistry VisitBase(out bool lacking)
        {
            LevelVisitsEditor.Visit picked = pickedVisit;
            lacking = picked != null && picked.lacksManifest;
            if (lacking)
                return m_Level.objects;
            LevelObjectRegistry visit = Objects;
            return visit != null && visit.isVisit ? visit.visitOf : null;
        }

        /// <summary>The level's rows under a fold "From Hall": dimmed, the hidden ones crossed out; click, Shift and
        /// Ctrl/Cmd pick them, right-click hides, shows, copies or replaces them on this visit.</summary>
        private void AddVisitBase(VisualElement list)
        {
            m_BaseLines.Clear();
            LevelObjectRegistry level = VisitBase(out bool lacking);
            if (level == null)
                return;
            LevelObjectRegistry visit = lacking ? null : Objects;
            List<LevelVisitsEditor.Visit> visits = Visits();
            string from = visits.Count > 0 ? visits[0].label : level.name;
            string label = pickedVisit != null ? pickedVisit.label : "";
            m_BasePicked.RemoveAll(id => level.FindById(id) == null);
            var fold = new Foldout { text = "From " + from + "  (" + level.entries.Count + ")", value = true };
            fold.style.marginTop = 6f;
            foreach (LevelObjectDef row in level.entries)
            {
                if (row == null)
                    continue;
                bool hidden = visit != null && visit.Hides(row);
                string name = string.IsNullOrEmpty(row.name) ? "(unnamed)" : row.name;
                var line = new Label((hidden ? "<s>" + name + "</s>" : name) + "   " + (hidden ? "hidden here" : "from " + from))
                {
                    tooltip = row.id + "\nRight-click: " + (hidden ? "show it" : "hide it") + " on this visit, replace it with "
                        + "a copy of the visit's own to change there, or copy it.",
                    userData = row.id
                };
                line.AddToClassList(hidden ? BaseHiddenClass : BaseRowClass);
                line.style.opacity = hidden ? 0.4f : 0.6f;
                line.style.paddingLeft = 4f;
                line.style.paddingTop = 2f;
                line.style.paddingBottom = 2f;
                string id = row.id;
                if (m_BasePicked.Contains(id))
                    DressPicked(line);
                line.RegisterCallback<MouseDownEvent>(down =>
                {
                    if (down.button != 0)
                        return;
                    PickBase(id, down.modifiers);
                    down.StopPropagation();
                });
                line.AddManipulator(new ContextualMenuManipulator(menu =>
                {
                    if (!m_BasePicked.Contains(id))
                    {
                        m_BasePicked.Clear();
                        m_BasePicked.Add(id);
                        rootVisualElement.schedule.Execute(Rebuild);
                    }
                    DropdownMenuAction.Status editable = lacking ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
                    menu.menu.AppendAction("Hide in this visit", _ => SetHiddenInVisit(id, true),
                        hidden ? DropdownMenuAction.Status.Disabled : editable);
                    menu.menu.AppendAction("Show in this visit", _ => SetHiddenInVisit(id, false),
                        hidden ? editable : DropdownMenuAction.Status.Disabled);
                    menu.menu.AppendSeparator();
                    int count = BasePickedRows(false).Count;
                    menu.menu.AppendAction(lacking
                            ? "Replace in this visit (Make " + label + "'s manifest first)"
                            : count > 1 ? "Replace " + count + " objects in this visit" : "Replace in this visit",
                        _ => ReplaceInVisit(m_BasePicked),
                        lacking || count == 0 ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                    menu.menu.AppendSeparator();
                    int picked = BasePickedRows(true).Count;
                    menu.menu.AppendAction(picked > 1 ? "Copy " + picked + " objects" : "Copy", _ => CopyBaseRows());
                    menu.menu.AppendAction("Paste", _ => Paste(),
                        !lacking && LevelManifestClipboard.HasRows() ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                }));
                fold.Add(line);
                m_BaseLines.Add((line, row));
            }
            list.Add(fold);
        }

        /// <summary>A click on one of the level's rows: alone, Shift for the range from the last picked, Ctrl/Cmd to add
        /// or take one. The visit's own rows are let go.</summary>
        internal void PickBase(string id, EventModifiers modifiers)
        {
            LevelObjectRegistry level = VisitBase(out _);
            if (level == null || level.FindById(id) == null)
                return;
            m_Note = null;
            m_Picked.Clear();
            m_Selected = -1;
            bool add = (modifiers & (EventModifiers.Control | EventModifiers.Command)) != 0;
            bool range = (modifiers & EventModifiers.Shift) != 0;
            var order = new List<string>();
            foreach (LevelObjectDef row in level.entries)
            {
                if (row != null)
                    order.Add(row.id);
            }
            int from = range && m_BasePicked.Count > 0 ? order.IndexOf(m_BasePicked[m_BasePicked.Count - 1]) : -1;
            int to = order.IndexOf(id);
            if (from >= 0)
            {
                if (!add)
                    m_BasePicked.Clear();
                for (int i = Mathf.Min(from, to); i <= Mathf.Max(from, to); i++)
                {
                    if (!m_BasePicked.Contains(order[i]))
                        m_BasePicked.Add(order[i]);
                }
            }
            else if (add)
            {
                if (!m_BasePicked.Remove(id))
                    m_BasePicked.Add(id);
            }
            else
            {
                m_BasePicked.Clear();
                m_BasePicked.Add(id);
            }
            Rebuild();
        }

        /// <summary>The level's rows picked, in its order; <paramref name="withHidden"/> false leaves out the ones the
        /// visit hides already.</summary>
        private List<LevelObjectDef> BasePickedRows(bool withHidden)
        {
            var rows = new List<LevelObjectDef>();
            LevelObjectRegistry level = VisitBase(out bool lacking);
            if (level == null)
                return rows;
            LevelObjectRegistry visit = lacking ? null : Objects;
            foreach (LevelObjectDef row in level.entries)
            {
                if (row != null && m_BasePicked.Contains(row.id) && (withHidden || visit == null || !visit.Hides(row)))
                    rows.Add(row);
            }
            return rows;
        }

        /// <summary>The level's rows picked onto the clipboard; how many.</summary>
        internal int CopyBaseRows()
        {
            List<LevelObjectDef> rows = BasePickedRows(true);
            return rows.Count > 0 && LevelManifestClipboard.Copy(VisitBase(out _), LevelKey(), rows) != null ? rows.Count : 0;
        }

        /// <summary>"Replace in this visit": the level's rows <paramref name="ids"/> hidden on the visit and pasted into
        /// it as copies at their poses — one undo step, the copies picked. Nothing while the visit has no manifest.</summary>
        internal List<LevelObjectDef> ReplaceInVisit(IEnumerable<string> ids)
        {
            var added = new List<LevelObjectDef>();
            LevelObjectRegistry visit = Objects;
            if (m_Level == null || visit == null || !visit.isVisit || VisitLacksManifest(out _) || ids == null)
                return added;
            var wanted = new HashSet<string>(ids);
            var rows = visit.visitOf.entries.FindAll(row => row != null && wanted.Contains(row.id));
            var refused = new List<string>();
            added = LevelManifestClipboard.Replace(visit, Kinds(), LevelKey(), rows, refused);
            PickAdded(added, refused);
            return added;
        }

        /// <summary>The level's row hidden (or shown again) on the visit being edited — one undo step.</summary>
        internal bool SetHiddenInVisit(string id, bool hide)
        {
            bool changed = LevelVisitsEditor.SetHidden(Objects, id, hide);
            if (changed)
            {
                Rebuild();
                SceneView.RepaintAll();
            }
            return changed;
        }

        /// <summary>For tests: the level's rows as the visit shows them — name and class.</summary>
        internal List<(string text, bool hidden)> ShownBaseRows()
        {
            var shown = new List<(string, bool)>();
            foreach (var entry in m_BaseLines)
                shown.Add((((Label)entry.line).text, entry.line.ClassListContains(BaseHiddenClass)));
            return shown;
        }

        private void FilterVisitBase()
        {
            foreach ((VisualElement line, LevelObjectDef row) in m_BaseLines)
                line.style.display = LevelManifestEdits.Matches(row, m_RowFilter) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>On a visit, the level's rows that stand on it, as dimmed ghosts with no handle — they are edited on
        /// the first visit. The hidden ones are not drawn.</summary>
        private void DrawVisitBase(Plane[] frustum)
        {
            LevelObjectRegistry visit = Objects;
            if (visit == null || !visit.isVisit)
                return;
            List<LevelVisitsEditor.Visit> visits = Visits();
            string from = visits.Count > 0 ? visits[0].label : visit.visitOf.name;
            foreach (LevelObjectDef row in visit.visitOf.entries)
            {
                if (row == null || visit.Hides(row))
                    continue;
                Vector3 world = visit.visitOf.ToWorld(row.position);
                DrawHologram(visit.visitOf, row, world, false, frustum);
                if (Event.current.type != EventType.Repaint)
                    continue;
                float size = HandleUtility.GetHandleSize(world) * 0.12f;
                using (new Handles.DrawingScope(new Color(0.75f, 0.75f, 0.75f, 0.6f)))
                    Handles.Label(world + new Vector3(size * 1.4f, size * 1.2f, 0f), row.name + "  (from " + from + ")");
            }
        }
    }
}
