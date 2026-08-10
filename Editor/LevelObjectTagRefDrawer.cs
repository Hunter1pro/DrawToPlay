using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A placement tag as a DROPDOWN of the project's tag vocabulary — every
    /// <see cref="WorldTagRegistry"/> there is, so a tag is always a declared word and never
    /// a free-typed one that matches nothing.
    ///
    /// Deliberately NOT scoped per level. Tags are mostly global, a level owning its own
    /// vocabulary is the exception, and the wiring that scoping would need (a
    /// <see cref="LevelContent"/> pointing at both this registry and its own tags) is
    /// usually absent while authoring — a picker that silently offers less because an asset
    /// reference has not been set yet is worse than one that offers a word from the wrong
    /// level. What matters is that every choice comes from data.
    /// </summary>
    [CustomPropertyDrawer(typeof(LevelObjectTagRef))]
    internal sealed class LevelObjectTagRefDrawer : PropertyDrawer
    {
        private const string k_Unset = "(none)";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            SerializedProperty tagProperty = property.FindPropertyRelative("tag");
            if (tagProperty == null)
                return new PropertyField(property);

            var choices = new List<string> { k_Unset };
            CollectDeclaredTags(choices);

            string current = string.IsNullOrEmpty(tagProperty.stringValue)
                ? k_Unset
                : tagProperty.stringValue;
            if (!choices.Contains(current))
                choices.Add(current); // a tag whose row is gone stays visible as the break

            var field = new DropdownField(choices, Mathf.Max(0, choices.IndexOf(current)));
            field.RegisterValueChangedCallback(changed =>
            {
                tagProperty.stringValue = changed.newValue == k_Unset ? "" : changed.newValue;
                property.serializedObject.ApplyModifiedProperties();
            });
            return field;
        }

        /// <summary>Every tag the project declares, from every tag registry — no level
        /// wiring consulted, so this answers the same whether or not the level has been
        /// hooked up yet.</summary>
        private static void CollectDeclaredTags(List<string> into)
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(WorldTagRegistry));
            for (int i = 0; i < guids.Length; i++)
            {
                var registry = AssetDatabase.LoadAssetAtPath<WorldTagRegistry>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (registry == null)
                    continue;
                for (int j = 0; j < registry.entries.Count; j++)
                {
                    WorldTagDef row = registry.entries[j];
                    if (row != null && !string.IsNullOrEmpty(row.name) && !into.Contains(row.name))
                        into.Add(row.name);
                }
            }
        }
    }
}
