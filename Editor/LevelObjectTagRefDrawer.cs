using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A placement tag as a DROPDOWN of the vocabulary that level can actually see: the
    /// project's GLOBAL tag registries plus the level's OWN — never another level's private
    /// words, and never a free-typed one that matches nothing.
    ///
    /// "Global" is derived, not declared: a <see cref="WorldTagRegistry"/> is private to a
    /// level when some <see cref="LevelContent.tags"/> points at it, and global otherwise. So
    /// adding a level's own vocabulary needs no bookkeeping anywhere else.
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
            CollectVisibleTags(property.serializedObject.targetObject, choices);

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

        /// <summary>Every tag name this object's level may carry: the global registries, plus
        /// the one the owning level keeps to itself.</summary>
        private static void CollectVisibleTags(Object owner, List<string> into)
        {
            WorldTagRegistry ownTags = null;
            var levelPrivate = new HashSet<WorldTagRegistry>();

            string[] levelGuids = AssetDatabase.FindAssets("t:" + nameof(LevelContent));
            for (int i = 0; i < levelGuids.Length; i++)
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelContent>(
                    AssetDatabase.GUIDToAssetPath(levelGuids[i]));
                if (level == null || level.tags == null)
                    continue;
                levelPrivate.Add(level.tags);
                // The level being edited: the registry these placements live in is its own.
                if (owner != null && level.objects == owner)
                    ownTags = level.tags;
            }

            string[] tagGuids = AssetDatabase.FindAssets("t:" + nameof(WorldTagRegistry));
            for (int i = 0; i < tagGuids.Length; i++)
            {
                var registry = AssetDatabase.LoadAssetAtPath<WorldTagRegistry>(
                    AssetDatabase.GUIDToAssetPath(tagGuids[i]));
                if (registry == null)
                    continue;
                if (levelPrivate.Contains(registry) && registry != ownTags)
                    continue; // another level's private vocabulary
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
