using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A placement tag as a DROPDOWN of the vocabulary its level actually speaks: the GLOBAL
    /// registries the owning tree declares, plus the level's OWN — followed through the
    /// wiring that exists (see <see cref="LevelVocabulary"/>), not guessed by scanning the
    /// project. The field's tooltip names where the list came from, so an author can see at
    /// a glance whether the chain is wired or being fallen back on.
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
            LevelVocabulary.CollectTags(property.serializedObject.targetObject, choices,
                out string source);

            string current = string.IsNullOrEmpty(tagProperty.stringValue)
                ? k_Unset
                : tagProperty.stringValue;
            if (!choices.Contains(current))
                choices.Add(current); // a tag whose row is gone stays visible as the break

            var field = new DropdownField(choices, Mathf.Max(0, choices.IndexOf(current)))
            {
                tooltip = source
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
