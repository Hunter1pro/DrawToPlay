using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// One manifest row in the Inspector — the authoring half of "a kind is a project
    /// definition, not a typed word": <see cref="LevelObjectDef.kind"/> draws as a DROPDOWN of
    /// the project's <see cref="LevelObjectKindRegistry"/> rows, id-wired on pick, so a kind
    /// nobody defined is not representable. Everything else draws normally.
    ///
    /// The registry is found by asset search rather than through the level: a level's content
    /// asset has no back-reference to the catalog that lists it, and the kinds are project-wide
    /// by definition. More than one registry in the project means all their rows are offered,
    /// grouped by asset.
    /// </summary>
    [CustomPropertyDrawer(typeof(LevelObjectDef))]
    internal sealed class LevelObjectDefDrawer : PropertyDrawer
    {
        private const string k_Unset = "(pick a kind)";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var container = new VisualElement();

            SerializedProperty kind = property.FindPropertyRelative("kind");
            SerializedProperty kindName = kind?.FindPropertyRelative("entryName");
            SerializedProperty kindId = kind?.FindPropertyRelative("entryId");
            if (kindName == null || kindId == null)
            {
                container.Add(new PropertyField(property));
                return container;
            }

            var rows = new List<LevelObjectKindDef>();
            CollectKindRows(rows);

            var choices = new List<string> { k_Unset };
            for (int i = 0; i < rows.Count; i++)
            {
                if (!string.IsNullOrEmpty(rows[i].name) && !choices.Contains(rows[i].name))
                    choices.Add(rows[i].name);
            }

            string current = string.IsNullOrEmpty(kindName.stringValue)
                ? k_Unset
                : kindName.stringValue;
            // A row whose kind was deleted from the project still shows — as itself, so the
            // break is visible rather than silently reset.
            if (!choices.Contains(current))
                choices.Add(current);

            var kindField = new DropdownField("Kind", choices, Mathf.Max(0, choices.IndexOf(current)));
            kindField.RegisterValueChangedCallback(changed =>
            {
                LevelObjectKindDef picked = null;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (string.Equals(rows[i].name, changed.newValue,
                        System.StringComparison.Ordinal))
                    {
                        picked = rows[i];
                        break;
                    }
                }
                kindName.stringValue = picked != null ? picked.name : "";
                kindId.stringValue = picked != null ? picked.id : "";
                property.serializedObject.ApplyModifiedProperties();
            });
            container.Add(kindField);

            AddField(container, property, "entryName", "Entry");
            AddField(container, property, "position", "Position");
            AddField(container, property, "tags", "Tags");
            AddField(container, property, "config", "Config");
            return container;
        }

        private static void AddField(VisualElement container, SerializedProperty property,
            string relative, string label)
        {
            SerializedProperty child = property.FindPropertyRelative(relative);
            if (child != null)
                container.Add(new PropertyField(child, label));
        }

        private static void CollectKindRows(List<LevelObjectKindDef> into)
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(LevelObjectKindRegistry));
            for (int i = 0; i < guids.Length; i++)
            {
                var registry = AssetDatabase.LoadAssetAtPath<LevelObjectKindRegistry>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (registry == null)
                    continue;
                for (int j = 0; j < registry.entries.Count; j++)
                {
                    if (registry.entries[j] != null)
                        into.Add(registry.entries[j]);
                }
            }
        }
    }
}
