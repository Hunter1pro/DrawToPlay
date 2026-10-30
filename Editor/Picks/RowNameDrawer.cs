using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>A row, picked — the drawer for <see cref="RowNameAttribute"/>. The field keeps the
    /// row's name; a name no catalog holds is shown as an error.</summary>
    [CustomPropertyDrawer(typeof(RowNameAttribute))]
    internal sealed class RowNameDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.String)
                return new Label(property.displayName + ": [RowName] needs a string");
            var container = new VisualElement();
            Build(container, property.serializedObject, property.propertyPath, property.displayName,
                ((RowNameAttribute)attribute).entryType);
            return container;
        }

        internal static void Build(VisualElement container, SerializedObject owner, string path, string label,
            System.Type entryType)
        {
            container.Clear();
            SerializedProperty property = owner.FindProperty(path);
            if (property == null)
                return;
            List<StateTreeRegistryAsset> catalogs = StateTreeRowOffers.For(owner.targetObject, entryType, out bool declared);
            string current = property.stringValue;
            StateTreeRegistryEntry row = StateTreeRowOffers.Find(catalogs, current);
            string kind = entryType != null ? entryType.Name.Replace("Def", "") : "row";
            var field = new StateTreePickField(label, current, row == null && !string.IsNullOrEmpty(current),
                row != null ? row.Describe() : "A " + kind + " row" + (declared ? "" : " (project-wide: this asset declares no catalog of them)"),
                at =>
                {
                    StateTreeRegistryAsset level = StateTreePickItems.Enclosing(owner.targetObject, entryType);
                    StateTreePickItems.Rows(catalogs, StateTreePickItems.Owning(owner.targetObject, out _), entryType, level)
                        .OfferNew(StateTreeRowCreate.Targets(level, catalogs, declared))
                        .Show(at, current, picked => Set(container, owner, path, label, entryType, picked.value));
                });
            field.WithRowButtons(row, row != null ? StateTreeRowOffers.CatalogOf(catalogs, row) : catalogs.Count > 0 ? catalogs[0] : null);
            container.Add(field);
        }

        private static void Set(VisualElement container, SerializedObject owner, string path, string label,
            System.Type entryType, string value)
        {
            owner.Update();
            SerializedProperty property = owner.FindProperty(path);
            if (property == null)
                return;
            property.stringValue = value ?? "";
            owner.ApplyModifiedProperties();
            Build(container, owner, path, label, entryType);
        }
    }
}
