using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>A scope, picked — the drawer for <see cref="ScopeIdAttribute"/>: the nearest scope of
    /// its kind (empty), a level row, or a context host's id in the open scenes.</summary>
    [CustomPropertyDrawer(typeof(ScopeIdAttribute))]
    internal sealed class ScopeIdDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.String)
                return new Label(property.displayName + ": [ScopeId] needs a string");
            var container = new VisualElement();
            Build(container, property.serializedObject, property.propertyPath, property.displayName);
            return container;
        }

        internal static List<string> Offers(Object owner)
        {
            var ids = new List<string>();
            foreach (StateTreeRegistryAsset catalog in StateTreeRowOffers.For(owner, typeof(LevelDef), out _))
            {
                for (int i = 0; catalog != null && i < catalog.Count; i++)
                {
                    StateTreeRegistryEntry row = catalog.EntryAt(i);
                    if (row != null && !string.IsNullOrEmpty(row.name) && !ids.Contains(row.name))
                        ids.Add(row.name);
                }
            }
            foreach (StateTreeContextHost host in Object.FindObjectsByType<StateTreeContextHost>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (host != null && !string.IsNullOrEmpty(host.contextId) && !ids.Contains(host.contextId))
                    ids.Add(host.contextId);
            }
            return ids;
        }

        private static void Build(VisualElement container, SerializedObject owner, string path, string label)
        {
            container.Clear();
            SerializedProperty property = owner.FindProperty(path);
            if (property == null)
                return;
            string current = property.stringValue;
            bool missing = !string.IsNullOrEmpty(current) && !Offers(owner.targetObject).Contains(current);
            container.Add(new StateTreePickField(label, string.IsNullOrEmpty(current) ? "(nearest of its kind)" : current,
                missing, "Which scope: the nearest of its kind, or one by its id.", at =>
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("(nearest of its kind)"), string.IsNullOrEmpty(current),
                        () => Set(container, owner, path, label, ""));
                    menu.AddSeparator("");
                    foreach (string id in Offers(owner.targetObject))
                    {
                        string chosen = id;
                        menu.AddItem(new GUIContent(id.Replace('/', '∕')), id == current,
                            () => Set(container, owner, path, label, chosen));
                    }
                    menu.DropDown(at);
                }));
        }

        private static void Set(VisualElement container, SerializedObject owner, string path, string label, string value)
        {
            owner.Update();
            SerializedProperty property = owner.FindProperty(path);
            if (property == null)
                return;
            property.stringValue = value ?? "";
            owner.ApplyModifiedProperties();
            Build(container, owner, path, label);
        }
    }
}
