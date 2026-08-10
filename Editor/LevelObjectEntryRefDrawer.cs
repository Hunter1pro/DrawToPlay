using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A placement's definition reference as a DROPDOWN of the right table: the sibling
    /// <see cref="LevelObjectDef.kind"/> names a kind row, that row names its
    /// <see cref="LevelObjectKindDef.definitions"/> registry, and this field offers exactly
    /// those rows — so "which grunt?" is a pick, never a typed name beside a raw GUID.
    ///
    /// A kind with no definition table (a door is only its config) has nothing to pick, and
    /// says so instead of offering an empty list.
    /// </summary>
    [CustomPropertyDrawer(typeof(LevelObjectEntryRef))]
    internal sealed class LevelObjectEntryRefDrawer : PropertyDrawer
    {
        private const string k_Unset = "(none)";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            SerializedProperty idProperty = property.FindPropertyRelative("entryId");
            SerializedProperty nameProperty = property.FindPropertyRelative("entryName");
            if (idProperty == null || nameProperty == null)
                return new PropertyField(property);

            StateTreeRegistryAsset definitions = DefinitionsFor(property);
            if (definitions == null)
            {
                var hint = new Label(string.IsNullOrEmpty(nameProperty.stringValue)
                    ? "Entry — this kind has no definition table"
                    : "Entry — " + nameProperty.stringValue
                        + " (this kind has no definition table)");
                hint.style.opacity = 0.7f;
                hint.style.paddingTop = 2f;
                hint.style.paddingBottom = 2f;
                return hint;
            }

            var rows = new List<StateTreeRegistryEntry>();
            for (int i = 0; i < definitions.Count; i++)
            {
                StateTreeRegistryEntry row = definitions.EntryAt(i);
                if (row != null && !string.IsNullOrEmpty(row.name))
                    rows.Add(row);
            }

            var choices = new List<string> { k_Unset };
            for (int i = 0; i < rows.Count; i++)
            {
                if (!choices.Contains(rows[i].name))
                    choices.Add(rows[i].name);
            }

            string current = string.IsNullOrEmpty(nameProperty.stringValue)
                ? k_Unset
                : nameProperty.stringValue;
            if (!choices.Contains(current))
                choices.Add(current); // a row deleted from the table stays visible as a break

            var field = new DropdownField("Entry (" + definitions.name + ")", choices,
                Mathf.Max(0, choices.IndexOf(current)));
            field.RegisterValueChangedCallback(changed =>
            {
                StateTreeRegistryEntry picked = null;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (string.Equals(rows[i].name, changed.newValue,
                        System.StringComparison.Ordinal))
                    {
                        picked = rows[i];
                        break;
                    }
                }
                nameProperty.stringValue = picked != null ? picked.name : "";
                idProperty.stringValue = picked != null ? picked.id : "";
                property.serializedObject.ApplyModifiedProperties();
            });
            return field;
        }

        /// <summary>The table this placement picks from: sibling kind → its row → the row's
        /// definitions registry. Null when the kind is unset or names no table.</summary>
        private static StateTreeRegistryAsset DefinitionsFor(SerializedProperty property)
        {
            string path = property.propertyPath;
            int cut = path.LastIndexOf('.');
            if (cut < 0)
                return null;
            SerializedProperty kind = property.serializedObject.FindProperty(
                path.Substring(0, cut + 1) + "kind");
            if (kind == null)
                return null;

            string kindId = kind.FindPropertyRelative("entryId")?.stringValue;
            string kindName = kind.FindPropertyRelative("entryName")?.stringValue;
            if (string.IsNullOrEmpty(kindId) && string.IsNullOrEmpty(kindName))
                return null;

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(LevelObjectKindRegistry));
            for (int i = 0; i < guids.Length; i++)
            {
                var registry = AssetDatabase.LoadAssetAtPath<LevelObjectKindRegistry>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (registry == null)
                    continue;
                var row = (string.IsNullOrEmpty(kindId)
                    ? registry.FindByName(kindName)
                    : registry.FindById(kindId)) as LevelObjectKindDef;
                if (row != null && row.definitions != null)
                    return row.definitions;
            }
            return null;
        }
    }
}
