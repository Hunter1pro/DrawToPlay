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

            // AND A WAY BACK TO THE ASSET. The picker says which tree this row runs, and that
            // was the only thing an author could do with it: no way to look at it, no way to
            // find it in the project. A name is not a reference you can follow. This pings the
            // asset in the Project window (and OPENS it on alt-click) — what the object fields
            // Unity draws itself have always done, and what a custom picker quietly took away.
            var reveal = new Button { text = "◎" };
            reveal.tooltip = "Show this asset in the Project window. Alt-click to open it.";
            reveal.style.width = 24f;
            reveal.style.flexShrink = 0f;
            reveal.style.unityTextAlign = TextAnchor.MiddleCenter;

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
                        // Picking (none) has to take the reveal away again, or the row offers
                        // to show an asset that is no longer named.
                        reveal.SetEnabled(live.objectReferenceValue != null);
                    },
                    "Pick State Tree", "StateTreeAsset");
            };
            row.Add(button);

            reveal.RegisterCallback<ClickEvent>(evt =>
            {
                owner.Update();
                SerializedProperty live = owner.FindProperty(path);
                UnityEngine.Object asset = live != null ? live.objectReferenceValue : null;
                if (asset == null)
                    return;
                if (evt.altKey)
                {
                    AssetDatabase.OpenAsset(asset);
                    return;
                }
                // SELECT AND PING, in that order: selecting shows it in the inspector, pinging
                // makes the Project window scroll to it and flash — together they answer both
                // halves of "which asset is this, and where does it live".
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            });
            // Nothing to reveal is a DISABLED button rather than a missing one, so the row's
            // shape does not jump as an author fills the field in.
            reveal.SetEnabled(property.objectReferenceValue != null);
            row.Add(reveal);
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
