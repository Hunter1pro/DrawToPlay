using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A placement tag as a DROPDOWN of the vocabularies its own manifest lists
    /// (<see cref="LevelObjectRegistry.tags"/>) — read straight off the asset being drawn.
    /// No project scan, no chain walk: the level says which registries it speaks, and this
    /// offers those. A manifest that lists none says so, because an empty dropdown with no
    /// explanation is the thing worth avoiding.
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

            // The rows live IN this registry, so the vocabulary list is one field away.
            var manifest = property.serializedObject.targetObject as LevelObjectRegistry;
            var choices = new List<string> { k_Unset };
            var sources = new List<string>();
            if (manifest != null)
            {
                for (int i = 0; i < manifest.tags.Count; i++)
                {
                    WorldTagRegistry registry = manifest.tags[i];
                    if (registry == null)
                        continue;
                    sources.Add(registry.name);
                    for (int j = 0; j < registry.entries.Count; j++)
                    {
                        WorldTagDef row = registry.entries[j];
                        if (row != null && !string.IsNullOrEmpty(row.name)
                            && !choices.Contains(row.name))
                            choices.Add(row.name);
                    }
                }
            }

            string current = string.IsNullOrEmpty(tagProperty.stringValue)
                ? k_Unset
                : tagProperty.stringValue;
            if (!choices.Contains(current))
                choices.Add(current); // a tag whose row is gone stays visible as the break

            var field = new DropdownField(choices, Mathf.Max(0, choices.IndexOf(current)))
            {
                tooltip = sources.Count > 0
                    ? "Vocabulary: " + string.Join(", ", sources)
                        + "  —  listed on " + manifest.name + ".tags"
                    : "This manifest lists no tag registries. Add one to "
                        + (manifest != null ? manifest.name : "the objects asset")
                        + ".tags to pick tags here."
            };
            field.RegisterValueChangedCallback(changed =>
            {
                tagProperty.stringValue = changed.newValue == k_Unset ? "" : changed.newValue;
                property.serializedObject.ApplyModifiedProperties();
            });
            return field;
        }
    }
}
