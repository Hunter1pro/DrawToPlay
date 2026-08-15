using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The DASHBOARD every registry asset gets for free (M13): entries as rows, sectioned by
    /// their group path, each row the entry's own C# fields drawn by reflection — so
    /// declaring a new registry KIND (one entry class + one one-line asset class) needs no
    /// editor work at all. The id column does not exist: ids are minted here when a row is
    /// added and never edited, because every typed reference in every tree stores one.
    /// Renaming an entry is safe the same way renaming a declared key is — references are
    /// id-wired and display the current name.
    /// </summary>
    [CustomEditor(typeof(StateTreeRegistryAsset), true)]
    public sealed class StateTreeRegistryEditor : UnityEditor.Editor
    {
        private const string k_AddUndo = "Add Registry Entry";
        private const string k_RemoveUndo = "Remove Registry Entry";
        private const string k_AllGroupsLabel = "all groups";
        private const string k_NoGroupLabel = "(no group)";

        private VisualElement m_Root;

        /// <summary>The list half of the pane — the only part <see cref="Rebuild"/> replaces.
        /// The toolbar above it is built once and never torn down, which is what lets the
        /// search field keep keyboard focus while every keystroke refilters the rows.</summary>
        private VisualElement m_ListRoot;

        private TextField m_SearchField;
        private DropdownField m_GroupField;

        /// <summary>View state, not data: the search text, and the group filter — null shows
        /// everything, empty string shows the ungrouped section, anything else one path.</summary>
        private string m_Search = string.Empty;
        private string m_GroupFilter;

        /// <summary>The dropdown's rows in display order, index-matched to its choices — the
        /// same index-addressing every dropdown in the tree window uses.</summary>
        private readonly List<string> m_GroupChoices = new List<string>();

        public override VisualElement CreateInspectorGUI()
        {
            m_Root = new VisualElement();

            // Add/remove mutate the list through Undo.RecordObject, so an undo lands outside
            // the binding system's sight — rebuild to catch up. Unhooked with the panel.
            Undo.undoRedoPerformed += Rebuild;
            m_Root.RegisterCallback<DetachFromPanelEvent>(_ =>
                Undo.undoRedoPerformed -= Rebuild);

            m_Root.Add(BuildAssetFields());
            m_Root.Add(BuildToolbar());
            m_ListRoot = new VisualElement();
            m_Root.Add(m_ListRoot);

            Rebuild();
            return m_Root;
        }

        /// <summary>The registry's OWN fields — everything it declares beside its rows: the
        /// level catalog's kinds, a manifest's tag vocabularies. Drawn by reflection like the
        /// rows are, so a registry kind that carries configuration still needs no editor
        /// work. Without this the fields exist, serialize, and drive pickers while being
        /// invisible in the one place an author looks — which is how a list nobody can see
        /// ends up "how does it even know?".</summary>
        private VisualElement BuildAssetFields()
        {
            var box = new VisualElement();
            SerializedProperty property = serializedObject.GetIterator();
            var enter = true;
            while (property.NextVisible(enter))
            {
                enter = false;
                if (property.name == "m_Script" || property.name == "entries")
                    continue;
                box.Add(new PropertyField(property.Copy()));
            }
            if (box.childCount > 0)
            {
                box.style.marginBottom = 6f;
                box.Bind(serializedObject);
            }
            return box;
        }

        /// <summary>Search + group filter. Both MANUAL fields (the bound-field-with-rebuild
        /// -handler trap), and both persistent — rebuilt controls lose focus mid-word.</summary>
        private VisualElement BuildToolbar()
        {
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.marginBottom = 4f;

            m_SearchField = new TextField();
            m_SearchField.style.flexGrow = 1f;
            m_SearchField.style.flexBasis = 0f;
            m_SearchField.textEdition.placeholder = "search entries";
            m_SearchField.tooltip = "Filters rows by ANY of the entry's text fields — name, "
                + "group, display name, whatever the entry class declares.";
            m_SearchField.RegisterValueChangedCallback(evt =>
            {
                m_Search = evt.newValue ?? string.Empty;
                Rebuild();
            });
            toolbar.Add(m_SearchField);

            m_GroupField = new DropdownField();
            m_GroupField.style.width = 120f;
            m_GroupField.style.flexShrink = 0f;
            m_GroupField.style.marginLeft = 2f;
            m_GroupField.tooltip = "Show one group's rows, or all.";
            m_GroupField.RegisterValueChangedCallback(evt =>
            {
                if (m_GroupField.index >= 0 && m_GroupField.index < m_GroupChoices.Count + 1)
                {
                    m_GroupFilter = m_GroupField.index == 0
                        ? null
                        : m_GroupChoices[m_GroupField.index - 1];
                    Rebuild();
                }
            });
            toolbar.Add(m_GroupField);

            return toolbar;
        }

        private void Rebuild()
        {
            m_ListRoot.Clear();
            serializedObject.Update();

            var registry = (StateTreeRegistryAsset)target;
            UpdateGroupChoices(registry);

            SerializedProperty entries = serializedObject.FindProperty("entries");
            if (entries == null)
            {
                m_ListRoot.Add(new HelpBox("This registry serializes no 'entries' list — the "
                    + "asset class must derive from StateTreeRegistry<TEntry>.",
                    HelpBoxMessageType.Error));
                return;
            }

            var shown = 0;
            var sections = new List<KeyValuePair<string, List<int>>>(Sections(registry));
            foreach (KeyValuePair<string, List<int>> section in sections)
                shown += section.Value.Count;

            var filtered = shown != registry.Count;
            var title = new Label($"{registry.entryType.Name} registry · "
                + (filtered ? $"{shown} of {registry.Count}" : registry.Count.ToString())
                + " entr" + (shown == 1 ? "y" : "ies"));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4f;
            m_ListRoot.Add(title);

            var hint = new Label("Rows are the data; trees list this asset in their Data "
                + "section and pick entries with ⛃. Rename freely — references follow by id. "
                + "The group path sections this list and the pickers' submenus.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.opacity = 0.75f;
            hint.style.marginBottom = 6f;
            m_ListRoot.Add(hint);

            // Sectioned by group, rows in list order inside each — the order the asset
            // serializes and the executor searches.
            foreach (KeyValuePair<string, List<int>> section in sections)
            {
                if (!string.IsNullOrEmpty(section.Key))
                {
                    var header = new Label(section.Key);
                    header.style.unityFontStyleAndWeight = FontStyle.Bold;
                    header.style.marginTop = 6f;
                    m_ListRoot.Add(header);
                }

                foreach (int index in section.Value)
                    m_ListRoot.Add(BuildRow(entries, index));
            }

            if (shown == 0 && registry.Count > 0)
            {
                m_ListRoot.Add(new HelpBox("No entries match the search/filter.",
                    HelpBoxMessageType.Info));
            }

            var add = new Button(AddEntry) { text = "Add Entry" };
            add.style.marginTop = 6f;
            add.tooltip = "Add a row. Its id is minted now and never changes — that id is "
                + "what every typed reference in every tree stores."
                + (m_GroupFilter != null
                    ? " With a group filtered, the new row lands in that group."
                    : string.Empty);
            m_ListRoot.Add(add);
        }

        /// <summary>Keep the dropdown honest against the data: distinct groups in section
        /// order, the current filter preserved when its group still exists and reset to "all"
        /// when it does not (deleting a group's last row must not leave an empty view).</summary>
        private void UpdateGroupChoices(StateTreeRegistryAsset registry)
        {
            m_GroupChoices.Clear();
            for (var i = 0; i < registry.Count; ++i)
            {
                StateTreeRegistryEntry entry = registry.EntryAt(i);
                var group = entry != null ? entry.group ?? string.Empty : string.Empty;
                if (!m_GroupChoices.Contains(group))
                    m_GroupChoices.Add(group);
            }

            if (m_GroupFilter != null && !m_GroupChoices.Contains(m_GroupFilter))
                m_GroupFilter = null;

            var labels = new List<string> { k_AllGroupsLabel };
            for (var i = 0; i < m_GroupChoices.Count; ++i)
            {
                labels.Add(m_GroupChoices[i].Length == 0
                    ? k_NoGroupLabel
                    : m_GroupChoices[i].Replace('/', '∕'));
            }

            m_GroupField.choices = labels;
            m_GroupField.SetValueWithoutNotify(m_GroupFilter == null
                ? k_AllGroupsLabel
                : labels[m_GroupChoices.IndexOf(m_GroupFilter) + 1]);
        }

        private IEnumerable<KeyValuePair<string, List<int>>> Sections(
            StateTreeRegistryAsset registry)
        {
            var order = new List<string>();
            var sections = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var i = 0; i < registry.Count; ++i)
            {
                StateTreeRegistryEntry entry = registry.EntryAt(i);
                if (entry == null || !PassesFilter(entry))
                    continue;

                var group = entry.group ?? string.Empty;
                if (!sections.TryGetValue(group, out List<int> rows))
                {
                    rows = new List<int>();
                    sections.Add(group, rows);
                    order.Add(group);
                }
                rows.Add(i);
            }

            foreach (var group in order)
                yield return new KeyValuePair<string, List<int>>(group, sections[group]);
        }

        /// <summary>The search matches ANY public string field of the entry — name and group
        /// from the base, display names and descriptions from whatever the entry class adds —
        /// so the dashboard needs no knowledge of the kind it is showing.</summary>
        private bool PassesFilter(StateTreeRegistryEntry entry)
        {
            if (m_GroupFilter != null
                && !string.Equals(entry.group ?? string.Empty, m_GroupFilter,
                    StringComparison.Ordinal))
                return false;

            if (string.IsNullOrEmpty(m_Search))
                return true;

            var fields = entry.GetType().GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            for (var i = 0; i < fields.Length; ++i)
            {
                if (fields[i].FieldType != typeof(string) || fields[i].Name == "id")
                    continue;
                var value = fields[i].GetValue(entry) as string;
                if (!string.IsNullOrEmpty(value)
                    && value.IndexOf(m_Search, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>One entry: identity row (name, group, remove), then the entry class's own
        /// fields — everything except the base's id/name/group, drawn as the plain property
        /// fields they are.</summary>
        private VisualElement BuildRow(SerializedProperty entries, int index)
        {
            SerializedProperty element = entries.GetArrayElementAtIndex(index);

            var container = new VisualElement();
            container.style.marginBottom = 4f;
            container.style.paddingLeft = 4f;
            container.style.borderLeftWidth = 2f;
            container.style.borderLeftColor = new Color(0.5f, 0.5f, 0.5f, 0.35f);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            container.Add(row);

            SerializedProperty id = element.FindPropertyRelative("id");
            SerializedProperty name = element.FindPropertyRelative("name");
            var nameField = new TextField { isDelayed = true };
            nameField.BindProperty(name);
            nameField.style.flexGrow = 1f;
            nameField.style.flexBasis = 0f;
            nameField.tooltip = "The runtime string (inventory keys, routing, logs) and the "
                + "display name in one. Safe to rename: references store the id ("
                + (id != null ? id.stringValue : "") + ").";
            row.Add(nameField);

            // NOT bound: a bound field fires its change event on the binding's own initial
            // sync, and this one's handler rebuilds the pane — bound, that loop rebuilds the
            // inspector every frame and every button dies mid-click. Manual value + manual
            // write means only a real user edit re-sections the dashboard.
            SerializedProperty groupProp = element.FindPropertyRelative("group");
            var groupField = new TextField
            {
                value = groupProp != null ? groupProp.stringValue : string.Empty,
                isDelayed = true
            };
            groupField.style.flexGrow = 0.7f;
            groupField.style.flexBasis = 0f;
            groupField.style.marginLeft = 2f;
            groupField.textEdition.placeholder = "group/path";
            groupField.tooltip = "Organization only: sections this dashboard and the ⛃ "
                + "pickers' submenus.";
            var elementPath = element.propertyPath;
            groupField.RegisterValueChangedCallback(evt =>
            {
                serializedObject.Update();
                SerializedProperty live = serializedObject.FindProperty(elementPath);
                SerializedProperty liveGroup = live?.FindPropertyRelative("group");
                if (liveGroup == null
                    || liveGroup.stringValue == (evt.newValue ?? string.Empty))
                    return;
                liveGroup.stringValue = evt.newValue ?? string.Empty;
                serializedObject.ApplyModifiedProperties();
                m_Root.schedule.Execute(Rebuild).ExecuteLater(0);
            });
            row.Add(groupField);

            var remove = new Button(() => RemoveEntry(index)) { text = "✕" };
            remove.style.width = 22f;
            remove.style.flexShrink = 0f;
            remove.tooltip = "Delete this entry. References to it (in any tree) warn in place "
                + "until re-picked — deleting here cannot reach them.";
            row.Add(remove);

            SerializedProperty child = element.Copy();
            SerializedProperty end = element.GetEndProperty();
            var enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                var leaf = child.name;
                if (leaf == "id" || leaf == "name" || leaf == "group")
                    continue;
                var fieldRow = new PropertyField(child.Copy());
                fieldRow.style.marginLeft = 8f;
                container.Add(fieldRow);
            }

            // The row's own one-line meaning (Describe) — how a progression row announces
            // its span without anyone opening the curve.
            var registryTarget = (StateTreeRegistryAsset)target;
            StateTreeRegistryEntry described = registryTarget.EntryAt(index);
            var summary = described != null ? described.Describe() : null;
            if (!string.IsNullOrEmpty(summary))
            {
                var line = new Label(summary);
                line.style.marginLeft = 8f;
                line.style.opacity = 0.6f;
                line.style.whiteSpace = WhiteSpace.Normal;
                container.Add(line);
            }

            // THE REVERSE WIRE (M23 review: "see who used the ability"): where this row is
            // referenced, from the cached project scan. Click lists every use; picking one
            // pings the asset that holds it. An unused row says so — that is a finding.
            if (described != null)
            {
                AssetWireScan.Index wireIndex = AssetWireScan.Get();
                List<AssetWireScan.WireUse> uses =
                    AssetWireScan.UsersOfRow(wireIndex, described);
                // Follow through holding rows here too — a cue's user list should reach
                // the tree that applies the effect, not stop at the effect row.
                var visited = new HashSet<string>(StringComparer.Ordinal);
                visited.Add(!string.IsNullOrEmpty(described.id)
                    ? described.id : "name:" + described.name);
                var lines = new List<AssetWireScan.ChainLine>();
                AssetWireScan.CollectChain(wireIndex, uses, lines, 0, visited);

                var usage = new Label(lines.Count == 0
                    ? "⛓ unused — nothing in assets references this row"
                    : "⛓ used in " + lines.Count + " place" + (lines.Count == 1 ? "" : "s")
                        + " — click to list");
                usage.style.marginLeft = 8f;
                usage.style.opacity = lines.Count == 0 ? 0.45f : 0.6f;
                usage.tooltip = "Computed from a project scan of registries, trees and "
                    + "prefabs (scene objects are not walked); uses held by registry rows "
                    + "are followed through (↳). Refreshes on project changes.";
                if (lines.Count > 0)
                {
                    usage.RegisterCallback<ClickEvent>(_ =>
                    {
                        var menu = new GenericMenu();
                        foreach (AssetWireScan.ChainLine line in lines)
                        {
                            UnityEngine.Object ping = line.context;
                            var indent = line.depth > 0
                                ? new string(' ', line.depth * 3) + "↳ "
                                : string.Empty;
                            menu.AddItem(new GUIContent(
                                (indent + line.description).Replace('/', '∕')), false,
                                () => EditorGUIUtility.PingObject(ping));
                        }
                        menu.ShowAsContext();
                    });
                }
                container.Add(usage);
            }

            container.Bind(serializedObject);
            return container;
        }

        /// <summary>Rows are appended through the LIVE list rather than arraySize++ — Unity's
        /// array-grow duplicates the last element, which would duplicate its ID, and two rows
        /// sharing an id is the one corruption this model cannot shrug off.</summary>
        private void AddEntry()
        {
            var registry = (StateTreeRegistryAsset)target;
            IList list = EntriesList(registry);
            if (list == null)
                return;

            Undo.RecordObject(registry, k_AddUndo);
            var entry = (StateTreeRegistryEntry)Activator.CreateInstance(registry.entryType);
            entry.id = Guid.NewGuid().ToString("N");
            entry.name = UniqueName(registry);
            // A new row must be VISIBLE where the author is looking: with a group filtered it
            // joins that group, and a search that would hide it is cleared.
            entry.group = m_GroupFilter ?? string.Empty;
            if (!string.IsNullOrEmpty(m_Search))
            {
                m_Search = string.Empty;
                m_SearchField.SetValueWithoutNotify(string.Empty);
            }
            list.Add(entry);
            EditorUtility.SetDirty(registry);
            serializedObject.Update();
            Rebuild();
        }

        private void RemoveEntry(int index)
        {
            var registry = (StateTreeRegistryAsset)target;
            IList list = EntriesList(registry);
            if (list == null || index < 0 || index >= list.Count)
                return;

            Undo.RecordObject(registry, k_RemoveUndo);
            list.RemoveAt(index);
            EditorUtility.SetDirty(registry);
            serializedObject.Update();
            Rebuild();
        }

        private static IList EntriesList(StateTreeRegistryAsset registry)
        {
            var field = registry.GetType().GetField("entries");
            return field != null ? field.GetValue(registry) as IList : null;
        }

        private static string UniqueName(StateTreeRegistryAsset registry)
        {
            const string stem = "entry";
            if (registry.FindByName(stem) == null)
                return stem;
            for (var i = 2; i < 1000; ++i)
            {
                if (registry.FindByName(stem + i) == null)
                    return stem + i;
            }

            return stem + Guid.NewGuid().ToString("N").Substring(0, 6);
        }
    }
}
