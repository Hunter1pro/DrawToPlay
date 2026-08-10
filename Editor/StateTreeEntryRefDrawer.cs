using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Every typed reference (<see cref="StateTreeEntryRef{TEntry}"/>) as a PICKER wherever the
    /// regular Inspector draws one — registry dashboards above all, which draw entries field by
    /// field and would otherwise show a raw id/name pair for a reference that is supposed to be
    /// a known-list choice.
    ///
    /// The entry TYPE comes from the field's own generic argument, and the rows come from every
    /// registry asset in the project whose <see cref="StateTreeRegistryAsset.entryType"/>
    /// matches — so a new registry kind gets this picker with no editor work, exactly like the
    /// dashboard itself. Picking writes id AND name (the id is the reference, the name is the
    /// display cache the rename rule keeps fresh); a reference whose row is gone shows as
    /// itself rather than silently resetting.
    /// </summary>
    [CustomPropertyDrawer(typeof(StateTreeEntryRef<>))]
    internal sealed class StateTreeEntryRefDrawer : PropertyDrawer
    {
        private const string k_Unset = "(none)";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            SerializedProperty idProperty = property.FindPropertyRelative("entryId");
            SerializedProperty nameProperty = property.FindPropertyRelative("entryName");
            Type entryType = EntryTypeOf(fieldInfo?.FieldType);
            if (idProperty == null || nameProperty == null || entryType == null)
                return new PropertyField(property);

            var rows = new List<StateTreeRegistryEntry>();
            CollectRows(entryType, rows);

            var choices = new List<string> { k_Unset };
            for (int i = 0; i < rows.Count; i++)
            {
                if (!string.IsNullOrEmpty(rows[i].name) && !choices.Contains(rows[i].name))
                    choices.Add(rows[i].name);
            }

            string current = string.IsNullOrEmpty(nameProperty.stringValue)
                ? k_Unset
                : nameProperty.stringValue;
            if (!choices.Contains(current))
                choices.Add(current); // a deleted row stays visible as the break it is

            var field = new DropdownField(NiceLabel(property, entryType), choices,
                Mathf.Max(0, choices.IndexOf(current)));
            field.RegisterValueChangedCallback(changed =>
            {
                StateTreeRegistryEntry picked = null;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (string.Equals(rows[i].name, changed.newValue, StringComparison.Ordinal))
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

        private static string NiceLabel(SerializedProperty property, Type entryType)
        {
            string label = ObjectNames.NicifyVariableName(property.name);
            return label + "  (" + entryType.Name.Replace("Def", "") + ")";
        }

        /// <summary>The concrete entry type behind the field — the generic argument of
        /// <see cref="StateTreeEntryRef{TEntry}"/>, walked up through list/array element types
        /// so a List&lt;ref&gt; element draws like a lone one.</summary>
        private static Type EntryTypeOf(Type fieldType)
        {
            for (Type walk = fieldType; walk != null; walk = walk.BaseType)
            {
                if (walk.IsGenericType
                    && walk.GetGenericTypeDefinition() == typeof(StateTreeEntryRef<>))
                    return walk.GetGenericArguments()[0];
            }
            if (fieldType != null && fieldType.IsArray)
                return EntryTypeOf(fieldType.GetElementType());
            if (fieldType != null && fieldType.IsGenericType
                && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                return EntryTypeOf(fieldType.GetGenericArguments()[0]);
            return null;
        }

        private static void CollectRows(Type entryType, List<StateTreeRegistryEntry> into)
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(StateTreeRegistryAsset));
            for (int i = 0; i < guids.Length; i++)
            {
                var registry = AssetDatabase.LoadAssetAtPath<StateTreeRegistryAsset>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (registry == null || registry.entryType != entryType)
                    continue;
                for (int j = 0; j < registry.Count; j++)
                {
                    StateTreeRegistryEntry row = registry.EntryAt(j);
                    if (row != null)
                        into.Add(row);
                }
            }
        }
    }
}
