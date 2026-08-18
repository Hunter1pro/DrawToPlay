using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A <see cref="StateTreeKeysOfAttribute"/> string: typed freely, or picked from the keys
    /// the row's OWN tree declares.
    ///
    /// The registry-row answer to the question the key field already answers for tasks. A task
    /// can look up its tree; a row cannot — but it names one, so this walks the property path
    /// upwards for that field (a cast entry's "beats" lives two levels above it), loads the
    /// tree, and offers what the tree says it speaks about. Choosing writes the declaration's
    /// NAME, which is what the runtime looks the role up by.
    ///
    /// It never disables the text field. A script may legitimately mention a part before the
    /// tree declares it, and a picker that forbade that would make the row wait on the tree.
    /// </summary>
    [CustomPropertyDrawer(typeof(StateTreeKeysOfAttribute))]
    internal sealed class StateTreeKeysOfDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.String)
                return new PropertyField(property);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var text = new TextField(property.displayName);
            text.AddToClassList(TextField.alignedFieldUssClassName);
            text.style.flexGrow = 1f;
            text.BindProperty(property);
            row.Add(text);

            var pick = new Button { text = "⚿" };
            pick.style.width = 26f;
            pick.style.flexShrink = 0f;
            pick.tooltip = "Pick one of the keys the picked tree declares.";
            string path = property.propertyPath;
            SerializedObject owner = property.serializedObject;
            pick.clicked += () => ShowMenu(pick, owner, path);
            row.Add(pick);
            return row;
        }

        private void ShowMenu(VisualElement anchor, SerializedObject owner, string path)
        {
            var attribute = fieldInfo != null
                ? System.Attribute.GetCustomAttribute(fieldInfo, typeof(StateTreeKeysOfAttribute))
                    as StateTreeKeysOfAttribute
                : null;

            owner.Update();
            SerializedProperty live = owner.FindProperty(path);
            StateTreeAsset tree = TreeFrom(live, attribute?.treeField);

            var menu = new GenericMenu();
            if (tree == null)
            {
                // WHICH TREE is the first thing missing, and saying so beats an empty list:
                // an author who has not picked the beats yet has nothing to choose from, and
                // that is a different problem from a tree that declares nothing.
                menu.AddDisabledItem(new GUIContent("Pick the tree first — its keys are what "
                    + "this offers."));
                menu.DropDown(anchor.worldBound);
                return;
            }

            var declarations = new List<StateTreeKeyDeclaration>();
            StateTreeKeyResolver.CollectVisible(tree, declarations);
            int offered = 0;
            for (int i = 0; i < declarations.Count; i++)
            {
                StateTreeKeyDeclaration declaration = declarations[i];
                if (declaration == null || string.IsNullOrEmpty(declaration.name))
                    continue;
                if (attribute != null && !attribute.any && declaration.kind != attribute.kind)
                    continue;

                offered++;
                string label = string.IsNullOrEmpty(declaration.description)
                    ? declaration.name
                    : declaration.name + "  —  " + declaration.description;
                string chosen = declaration.name;
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    owner.Update();
                    SerializedProperty target = owner.FindProperty(path);
                    if (target == null)
                        return;
                    target.stringValue = chosen;
                    owner.ApplyModifiedProperties();
                });
            }

            if (offered == 0)
                menu.AddDisabledItem(new GUIContent("'" + tree.name
                    + "' declares no keys of this kind."));
            menu.DropDown(anchor.worldBound);
        }

        /// <summary>
        /// The tree the row names, found by walking UP the property path. A cast entry's role
        /// sits at <c>entries.Array.data[0].cast.Array.data[1].role</c> and the tree it belongs
        /// to is <c>entries.Array.data[0].beats</c> — two levels up — so the search climbs one
        /// segment at a time and takes the first ancestor that has the named field.
        /// </summary>
        private static StateTreeAsset TreeFrom(SerializedProperty property, string treeField)
        {
            if (property == null || string.IsNullOrEmpty(treeField))
                return null;

            string path = property.propertyPath;
            SerializedObject owner = property.serializedObject;
            for (int guard = 0; guard < 8; guard++)
            {
                int cut = path.LastIndexOf('.');
                if (cut < 0)
                    break;
                path = path.Substring(0, cut);
                // Array plumbing is not a level an author ever sees; step over it so "up one"
                // means what it looks like in the inspector.
                if (path.EndsWith(".Array") || path.EndsWith("Array"))
                    continue;

                SerializedProperty ancestor = owner.FindProperty(path);
                if (ancestor == null)
                    continue;
                SerializedProperty named = ancestor.FindPropertyRelative(treeField);
                if (named != null && named.propertyType == SerializedPropertyType.ObjectReference)
                    return named.objectReferenceValue as StateTreeAsset;
            }
            return null;
        }
    }
}
