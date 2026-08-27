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
    /// the stable reference already; picked, it shows the row's name and locks.
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

            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            container.Add(line);

            var text = new TextField(label);
            text.AddToClassList(TextField.alignedFieldUssClassName);
            text.style.flexGrow = 1f;
            text.BindProperty(property);
            text.SetEnabled(row == null);
            text.tooltip = row == null
                ? "No manifest here holds this id, so it is still free text. ⛭ offers what "
                    + (declared ? "this asset declares." : "the project holds.")
                : "Picked: '" + row.name + "' — the id is the row's. ⛭ to change it.";
            line.Add(text);

            var pick = new Button { text = "⛭" };
            pick.style.width = 26f;
            pick.style.flexShrink = 0f;
            pick.tooltip = offers.Count == 0
                ? "This asset declares no manifest yet."
                : "Pick a placement from what this asset declares.";
            pick.clicked += () => ShowMenu(pick, container, owner, path, label);
            line.Add(pick);
        }

        private static void ShowMenu(VisualElement anchor, VisualElement container,
            SerializedObject owner, string path, string label)
        {
            var offers = new List<LevelObjectDef>();
            bool declared = Offers(owner.targetObject, offers);

            var menu = new GenericMenu();
            if (offers.Count == 0)
                menu.AddDisabledItem(new GUIContent("nothing here declares a manifest"));
            else if (!declared)
                menu.AddDisabledItem(new GUIContent("— project-wide (this object declares nothing) —"));

            SerializedProperty live = owner.FindProperty(path);
            string current = live != null ? live.stringValue : "";
            for (int i = 0; i < offers.Count; i++)
            {
                LevelObjectDef row = offers[i];
                string entry = (string.IsNullOrEmpty(row.group) ? "" : row.group + "/")
                    + row.name + "  (" + row.id + ")";
                menu.AddItem(new GUIContent(entry), row.id == current,
                    () => Set(owner, path, container, label, row.id));
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Unpick — type it freely"), false,
                () => Set(owner, path, container, label, current));
            menu.DropDown(anchor.worldBound);
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
