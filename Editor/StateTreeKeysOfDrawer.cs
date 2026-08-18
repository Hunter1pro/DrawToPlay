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

            var container = new VisualElement();
            Build(container, property.serializedObject, property.propertyPath,
                property.displayName);
            return container;
        }

        /// <summary>
        /// WIRED OR TYPED, and the row looks like whichever it is.
        ///
        /// A picked part is a LINK: it shows the declaration's current name, follows a rename
        /// on the tree, and is locked — because a linked field you can also type into is a
        /// field that silently stops matching the thing it claims to be linked to. Unbind from
        /// the menu and it is free text again, which is the same bargain every wired key field
        /// in this toolset offers.
        /// </summary>
        private void Build(VisualElement container, SerializedObject owner, string path,
            string label)
        {
            container.Clear();
            SerializedProperty property = owner.FindProperty(path);
            if (property == null)
                return;

            var attribute = fieldInfo != null
                ? System.Attribute.GetCustomAttribute(fieldInfo, typeof(StateTreeKeysOfAttribute))
                    as StateTreeKeysOfAttribute
                : null;
            SerializedProperty idProperty = IdOf(property, attribute);
            bool wired = idProperty != null && !string.IsNullOrEmpty(idProperty.stringValue);

            // THE RENAME FOLLOWS THE WIRE: the declaration is the one place the name lives, so
            // a wired row re-reads it rather than keeping the copy it was given.
            if (wired)
            {
                StateTreeAsset tree = TreeFrom(property, attribute?.treeField);
                string current = NameOf(tree, idProperty.stringValue);
                if (!string.IsNullOrEmpty(current) && current != property.stringValue)
                {
                    property.stringValue = current;
                    owner.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            container.Add(row);

            var text = new TextField(label);
            text.AddToClassList(TextField.alignedFieldUssClassName);
            text.style.flexGrow = 1f;
            text.BindProperty(property);
            text.SetEnabled(!wired);
            text.tooltip = wired
                ? "Linked to a key the tree declares — the name follows the declaration. "
                    + "Unbind from the ⚿ menu to type freely."
                : "Typed freely. ⚿ links it to a key the picked tree declares.";
            row.Add(text);

            var pick = new Button { text = wired ? "⚿" : "⚿" };
            pick.style.width = 26f;
            pick.style.flexShrink = 0f;
            pick.tooltip = wired
                ? "Link to a different declared key, or unbind to type freely."
                : "Link to a key the picked tree declares.";
            pick.clicked += () => ShowMenu(pick, container, owner, path, label);
            row.Add(pick);
        }

        private void ShowMenu(VisualElement anchor, VisualElement container,
            SerializedObject owner, string path, string label)
        {
            var attribute = fieldInfo != null
                ? System.Attribute.GetCustomAttribute(fieldInfo, typeof(StateTreeKeysOfAttribute))
                    as StateTreeKeysOfAttribute
                : null;

            owner.Update();
            SerializedProperty live = owner.FindProperty(path);
            SerializedProperty liveId = IdOf(live, attribute);
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
                string entry = string.IsNullOrEmpty(declaration.description)
                    ? declaration.name
                    : declaration.name + "  —  " + declaration.description;
                string chosen = declaration.name;
                string chosenId = declaration.id;
                menu.AddItem(new GUIContent(entry), false, () =>
                {
                    owner.Update();
                    SerializedProperty target = owner.FindProperty(path);
                    if (target == null)
                        return;
                    target.stringValue = chosen;
                    // BOTH HALVES, or the link is a coincidence: the name is what the runtime
                    // reads, the id is what survives the declaration being renamed.
                    SerializedProperty targetId = IdOf(target, attribute);
                    if (targetId != null)
                        targetId.stringValue = chosenId;
                    owner.ApplyModifiedProperties();
                    Build(container, owner, path, label);
                });
            }

            if (offered == 0)
                menu.AddDisabledItem(new GUIContent("'" + tree.name
                    + "' declares no keys of this kind."));

            // THE WAY BACK OUT. A link you cannot leave is a cage, and a script may need to
            // speak of a part before any tree declares it.
            if (liveId != null && !string.IsNullOrEmpty(liveId.stringValue))
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Unbind — type this name freely"), false, () =>
                {
                    owner.Update();
                    SerializedProperty target = owner.FindProperty(path);
                    SerializedProperty targetId = IdOf(target, attribute);
                    if (targetId == null)
                        return;
                    targetId.stringValue = "";
                    owner.ApplyModifiedProperties();
                    Build(container, owner, path, label);
                });
            }
            menu.DropDown(anchor.worldBound);
        }

        /// <summary>The sibling property holding the picked declaration's id, or null when the
        /// field is a plain offer with no link to keep.</summary>
        private static SerializedProperty IdOf(SerializedProperty property,
            StateTreeKeysOfAttribute attribute)
        {
            if (property == null || attribute == null || string.IsNullOrEmpty(attribute.idField))
                return null;
            int cut = property.propertyPath.LastIndexOf('.');
            if (cut < 0)
                return property.serializedObject.FindProperty(attribute.idField);
            SerializedProperty parent = property.serializedObject
                .FindProperty(property.propertyPath.Substring(0, cut));
            return parent?.FindPropertyRelative(attribute.idField);
        }

        /// <summary>What the declaration is called NOW — the whole reason an id is stored.</summary>
        private static string NameOf(StateTreeAsset tree, string keyId)
        {
            if (tree == null || string.IsNullOrEmpty(keyId))
                return null;
            var declarations = new List<StateTreeKeyDeclaration>();
            StateTreeKeyResolver.CollectVisible(tree, declarations);
            for (int i = 0; i < declarations.Count; i++)
            {
                if (declarations[i] != null && declarations[i].id == keyId)
                    return declarations[i].name;
            }
            return null;
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
