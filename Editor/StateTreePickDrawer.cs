using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A <see cref="StateTreePickAttribute"/> field as the project's searchable picker.
    ///
    /// The rows are every <see cref="StateTreeAsset"/> in the project: named by the tree's own
    /// display name, foldered by where the asset lives (so a level's trees sit together), and
    /// described by their KIND — "npc", "enemy", "player" — which is the thing that tells two
    /// similarly-named trees apart. A "(none)" row is offered first, because clearing the field is
    /// a real choice and a picker with no way back to empty would trap the author.
    ///
    /// It is the same window as the task list and the registry rows
    /// (<see cref="StateTreeNodePicker"/>, via <see cref="StateTreePickerItem"/>), so every
    /// "choose one of these" in this toolset behaves the same way.
    /// </summary>
    [CustomPropertyDrawer(typeof(StateTreePickAttribute))]
    internal sealed class StateTreePickDrawer : PropertyDrawer
    {
        private const string k_Unset = "(none)";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference)
                return new PropertyField(property);

            var row = new VisualElement();
            row.AddToClassList("unity-base-field");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var label = new Label(ObjectNames.NicifyVariableName(property.name));
            label.AddToClassList("unity-base-field__label");
            row.Add(label);

            var button = new Button { text = TextOf(property.objectReferenceValue) };
            button.style.flexGrow = 1f;
            button.style.flexBasis = 0f;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.tooltip = "Pick a state tree. Search by name, or browse by folder.";

            string path = property.propertyPath;
            SerializedObject owner = property.serializedObject;
            button.clicked += () =>
            {
                StateTreeNodePicker.ShowItems(StateTreeNodePicker.ScreenRectOf(button),
                    Items(),
                    payload =>
                    {
                        owner.Update();
                        SerializedProperty live = owner.FindProperty(path);
                        if (live == null)
                            return;
                        live.objectReferenceValue = payload as StateTreeAsset;
                        owner.ApplyModifiedProperties();
                        button.text = TextOf(live.objectReferenceValue);
                    },
                    "Pick State Tree", "StateTreeAsset");
            };
            row.Add(button);
            return row;
        }

        private static string TextOf(UnityEngine.Object value)
        {
            return value == null ? k_Unset : value.name;
        }

        /// <summary>Every tree in the project, plus the row that clears the field.</summary>
        private static List<StateTreePickerItem> Items()
        {
            var items = new List<StateTreePickerItem>
            {
                new StateTreePickerItem
                {
                    displayName = k_Unset,
                    description = "Take whatever default the spawner uses.",
                    persistKey = "(none)",
                    payload = null
                }
            };

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(StateTreeAsset));
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var tree = AssetDatabase.LoadAssetAtPath<StateTreeAsset>(path);
                if (tree == null)
                    continue;

                items.Add(new StateTreePickerItem
                {
                    displayName = string.IsNullOrEmpty(tree.treeName) ? tree.name : tree.treeName,
                    // Foldered by where it lives: a level's trees sit together, which is how an
                    // author looking for "the yard's guard" actually narrows it down.
                    category = FolderOf(path),
                    description = string.IsNullOrEmpty(tree.treeKind) ? "" : tree.treeKind,
                    identity = path,
                    // The GUID, so a favourite survives the asset being moved or renamed.
                    persistKey = guids[i],
                    payload = tree
                });
            }
            return items;
        }

        /// <summary>The asset's containing folder, minus the leading "Assets/", as a category
        /// path. Empty for anything sitting directly in Assets.</summary>
        private static string FolderOf(string assetPath)
        {
            int cut = assetPath.LastIndexOf('/');
            if (cut < 0)
                return string.Empty;

            string folder = assetPath.Substring(0, cut);
            const string prefix = "Assets/";
            return folder.StartsWith(prefix, StringComparison.Ordinal)
                ? folder.Substring(prefix.Length)
                : folder;
        }
    }
}
