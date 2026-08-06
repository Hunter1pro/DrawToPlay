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

        private VisualElement m_Root;

        public override VisualElement CreateInspectorGUI()
        {
            m_Root = new VisualElement();

            // Add/remove mutate the list through Undo.RecordObject, so an undo lands outside
            // the binding system's sight — rebuild to catch up. Unhooked with the panel.
            Undo.undoRedoPerformed += Rebuild;
            m_Root.RegisterCallback<DetachFromPanelEvent>(_ =>
                Undo.undoRedoPerformed -= Rebuild);

            Rebuild();
            return m_Root;
        }

        private void Rebuild()
        {
            m_Root.Clear();
            serializedObject.Update();

            var registry = (StateTreeRegistryAsset)target;

            var title = new Label($"{registry.entryType.Name} registry · {registry.Count} "
                + "entr" + (registry.Count == 1 ? "y" : "ies"));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4f;
            m_Root.Add(title);

            var hint = new Label("Rows are the data; trees list this asset in their Data "
                + "section and pick entries with ⛃. Rename freely — references follow by id. "
                + "The group path sections this list and the pickers' submenus.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.opacity = 0.75f;
            hint.style.marginBottom = 6f;
            m_Root.Add(hint);

            SerializedProperty entries = serializedObject.FindProperty("entries");
            if (entries == null)
            {
                m_Root.Add(new HelpBox("This registry serializes no 'entries' list — the "
                    + "asset class must derive from StateTreeRegistry<TEntry>.",
                    HelpBoxMessageType.Error));
                return;
            }

            // Sectioned by group, rows in list order inside each — the order the asset
            // serializes and the executor searches.
            foreach (KeyValuePair<string, List<int>> section in Sections(registry))
            {
                if (!string.IsNullOrEmpty(section.Key))
                {
                    var header = new Label(section.Key);
                    header.style.unityFontStyleAndWeight = FontStyle.Bold;
                    header.style.marginTop = 6f;
                    m_Root.Add(header);
                }

                foreach (int index in section.Value)
                    m_Root.Add(BuildRow(entries, index));
            }

            var add = new Button(AddEntry) { text = "Add Entry" };
            add.style.marginTop = 6f;
            add.tooltip = "Add a row. Its id is minted now and never changes — that id is "
                + "what every typed reference in every tree stores.";
            m_Root.Add(add);
        }

        private IEnumerable<KeyValuePair<string, List<int>>> Sections(
            StateTreeRegistryAsset registry)
        {
            var order = new List<string>();
            var sections = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var i = 0; i < registry.Count; ++i)
            {
                StateTreeRegistryEntry entry = registry.EntryAt(i);
                var group = entry != null ? entry.group ?? string.Empty : string.Empty;
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
