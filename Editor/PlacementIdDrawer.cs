using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A PLACEMENT, PICKED — the drawer for <see cref="PlacementIdAttribute"/>, the shape of
    /// <see cref="WorldTagDrawer"/> over manifest rows. The field holds the row's ID, which is
    /// the stable reference already, and shows the row's name; an id no manifest holds is an error.
    /// Never typed.
    /// </summary>
    [CustomPropertyDrawer(typeof(PlacementIdAttribute))]
    internal sealed class PlacementIdDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.String)
                return new PropertyField(property);

            var container = new VisualElement();
            Build(container, property.serializedObject, property.propertyPath,
                property.displayName);
            return container;
        }

        private static void Build(VisualElement container, SerializedObject owner, string path,
            string label)
        {
            container.Clear();
            SerializedProperty property = owner.FindProperty(path);
            if (property == null)
                return;

            var offers = new List<LevelObjectDef>();
            bool declared = Offers(owner.targetObject, offers);
            LevelObjectDef row = Row(offers, property.stringValue);

            string current = property.stringValue;
            var field = new StateTreePickField(label, row != null ? row.name : current,
                row == null && !string.IsNullOrEmpty(current),
                row != null
                    ? "Picked: '" + row.name + "' (" + row.id + ")"
                    : "A placement of " + (declared ? "this asset's manifest." : "the project's manifests."),
                at => ShowMenu(at, container, owner, path, label));
            container.Add(field);
        }

        private static void ShowMenu(Rect anchor, VisualElement container,
            SerializedObject owner, string path, string label)
        {
            var offers = new List<LevelObjectDef>();
            bool declared = Offers(owner.targetObject, offers);
            SerializedProperty live = owner.FindProperty(path);
            string current = live != null ? live.stringValue : "";
            // In a schema's tree, the enclosing Level's manifest is this field's level.
            var level = StateTreePickItems.Enclosing(owner.targetObject, typeof(LevelObjectDef)) as LevelObjectRegistry;
            StateTreePickMenu menu = StateTreePickItems.Placements(offers,
                StateTreePickItems.Owning(owner.targetObject, out _), level);
            if (!declared)
                menu.title = "Placement (project-wide: this object declares nothing)";
            menu.Show(anchor, current, picked => Set(owner, path, container, label, picked.value));
        }

        private static void Set(SerializedObject owner, string path, VisualElement container,
            string label, string id)
        {
            owner.Update();
            SerializedProperty target = owner.FindProperty(path);
            if (target == null)
                return;
            target.stringValue = id ?? "";
            owner.ApplyModifiedProperties();
            Build(container, owner, path, label);
        }

        /// <summary>Whose manifests apply: the asset, then the file's main asset (a task is a
        /// sub-asset of the tree that declares), then every manifest in the project.</summary>
        private static bool Offers(Object target, List<LevelObjectDef> into)
        {
            StateTreeOffers.PlacementsFor(target, into);
            if (into.Count > 0)
                return true;

            string path = target != null ? AssetDatabase.GetAssetPath(target) : "";
            if (!string.IsNullOrEmpty(path))
            {
                Object main = AssetDatabase.LoadMainAssetAtPath(path);
                if (main != null && main != target)
                {
                    StateTreeOffers.PlacementsFor(main, into);
                    if (into.Count > 0)
                        return true;
                }
            }

            foreach (string guid in AssetDatabase.FindAssets("t:LevelObjectRegistry"))
            {
                var manifest = AssetDatabase.LoadAssetAtPath<LevelObjectRegistry>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (manifest == null)
                    continue;
                for (int i = 0; i < manifest.Count; i++)
                {
                    if (manifest.EntryAt(i) is LevelObjectDef row && !string.IsNullOrEmpty(row.id)
                        && !into.Contains(row))
                        into.Add(row);
                }
            }
            return false;
        }

        private static LevelObjectDef Row(List<LevelObjectDef> offers, string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i].id == id)
                    return offers[i];
            }
            return null;
        }
    }
}
