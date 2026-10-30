using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A placement's definition reference as a PICKER of the right table: the sibling
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

            StateTreeRegistryEntry current = definitions.FindById(idProperty.stringValue)
                ?? definitions.FindByName(nameProperty.stringValue);
            bool missing = current == null && !string.IsNullOrEmpty(nameProperty.stringValue);
            SerializedObject owner = property.serializedObject;
            string path = property.propertyPath;
            StateTreePickField field = null;
            field = new StateTreePickField("Entry (" + definitions.name + ")",
                current != null ? current.name : nameProperty.stringValue, missing,
                current != null ? StateTreePickItems.Meaning(current) : "A row of " + definitions.name + ".",
                at => StateTreePickItems.Rows(new List<StateTreeRegistryAsset> { definitions }, null,
                        definitions.entryType)
                    .OfferNew(new List<StateTreeRegistryAsset> { definitions })
                    .Show(at, current != null ? current.name : nameProperty.stringValue, picked =>
                    {
                        owner.Update();
                        SerializedProperty live = owner.FindProperty(path);
                        if (live == null)
                            return;
                        var row = picked.payload as StateTreeRegistryEntry;
                        live.FindPropertyRelative("entryName").stringValue = row != null ? row.name : "";
                        live.FindPropertyRelative("entryId").stringValue = row != null ? row.id : "";
                        owner.ApplyModifiedProperties();
                        field.button.text = (row != null ? row.name : "(none)") + "  ▾";
                        field.button.style.color = StyleKeyword.Null;
                    }));
            field.WithPing(definitions, "the table " + definitions.name);
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

            LevelObjectKindDef row = LevelObjectKinds.Find(kindId, kindName);
            if (row != null && row.definitions != null)
                return row.definitions;
            return null;
        }
    }
}
