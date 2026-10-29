using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The <see cref="StateTreeKeyField"/> everywhere the REGULAR Inspector draws one — scene
    /// components above all: the same contract the State Tree window gives task fields. A key is
    /// picked — a declaration of the nearest reachable tree (wired by id: renames follow), a
    /// registry's key, a [KeyCatalog] constant — or declared from the menu; never typed. A name
    /// nothing declares is shown as an error.
    /// </summary>
    [CustomPropertyDrawer(typeof(StateTreeKeyField))]
    internal sealed class StateTreeKeyFieldDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var container = new VisualElement();
            Build(container, property);
            return container;
        }

        private void Build(VisualElement container, SerializedProperty property)
        {
            container.Clear();

            SerializedProperty textProperty = property.FindPropertyRelative("text");
            SerializedProperty idProperty = property.FindPropertyRelative("keyId");
            if (textProperty == null || idProperty == null)
            {
                container.Add(new PropertyField(property));
                return;
            }

            var wired = !string.IsNullOrEmpty(idProperty.stringValue);
            Object owner = property.serializedObject.targetObject;
            StateTreeAsset tree = TreeFor(owner);

            // The display half of the rename rule: a wired field shows (and keeps) the
            // declaration's CURRENT name — a tree's key, a registry's key, or a tree
            // PARAMETER (all seed by name and wire by id).
            if (wired)
            {
                StateTreeKeyDeclaration declaration =
                    FindDeclaration(owner, tree, idProperty.stringValue);
                var current = declaration != null
                    ? declaration.name
                    : tree != null ? FindParameter(tree, idProperty.stringValue)?.name : null;
                if (!string.IsNullOrEmpty(current) && !string.Equals(current,
                        textProperty.stringValue, System.StringComparison.Ordinal))
                {
                    textProperty.stringValue = current;
                    property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            var offers = StateTreeKeyOffers.For(owner, tree);
            bool known = wired || StateTreeKeyOffers.TryFind(offers, textProperty.stringValue, out _);
            var row = new StateTreePickField(property.displayName, textProperty.stringValue,
                !known && !string.IsNullOrEmpty(textProperty.stringValue),
                wired
                    ? "Wired to a declared key — the name follows the declaration."
                    : known
                        ? "A declared key, by name."
                        : "A blackboard key. Pick a declared key, or declare one (▾ → Declare).",
                at => ShowMenu(at, container, property, textProperty, idProperty));
            container.Add(row);

            // The lock has to follow the WIRE, not the moment this row was built: an undo or
            // a revert changes keyId under the row, and an editable bound field is the lie
            // this drawer exists to stop.
            row.TrackPropertyValue(idProperty, _ =>
            {
                var nowWired = !string.IsNullOrEmpty(idProperty.stringValue);
                if (nowWired != wired)
                    Build(container, property);
            });
        }

        private void ShowMenu(Rect anchor, VisualElement container,
            SerializedProperty property, SerializedProperty textProperty,
            SerializedProperty idProperty)
        {
            Object owner = property.serializedObject.targetObject;
            StateTreeAsset tree = TreeFor(owner);
            var declarations = new List<StateTreeKeyDeclaration>();
            if (tree != null)
                StateTreeKeyResolver.CollectVisible(tree, declarations);
            var fromRegistries = new List<StateTreeKeyDeclaration>();
            StateTreeOffers.KeysFor(owner, fromRegistries);
            declarations.AddRange(fromRegistries);

            var attribute = fieldInfo != null
                ? System.Attribute.GetCustomAttribute(fieldInfo, typeof(StateTreeKeyAttribute))
                    as StateTreeKeyAttribute
                : null;
            StateTreeKeyKind? preferred = attribute != null && !attribute.any
                ? attribute.kind
                : (StateTreeKeyKind?)null;

            var menu = new GenericMenu();
            if (declarations.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent(tree == null
                    ? "No key source reachable — no tree in the parents or at the scene's "
                        + "Root, and no registry keys behind this row."
                    : "The reachable sources declare no keys yet."));
            }

            var currentId = idProperty.stringValue;
            void AddEntries(bool matching)
            {
                foreach (StateTreeKeyDeclaration declaration in declarations)
                {
                    if (declaration == null || string.IsNullOrEmpty(declaration.name))
                        continue;
                    var isMatch = preferred == null || declaration.kind == preferred.Value;
                    if (isMatch != matching)
                        continue;
                    var keyName = declaration.name;
                    var keyId = declaration.id;
                    var label = (keyName + "  (" + declaration.kind + ")").Replace('/', '∕');
                    menu.AddItem(new GUIContent(label),
                        string.Equals(keyId, currentId, System.StringComparison.Ordinal),
                        () =>
                        {
                            textProperty.stringValue = keyName;
                            idProperty.stringValue = keyId;
                            property.serializedObject.ApplyModifiedProperties();
                            Build(container, property);
                        });
                }
            }

            AddEntries(true);

            // The tree's declared PARAMETERS wire the same way — the caller's arguments,
            // seeded onto the blackboard under their names.
            if (tree != null && tree.parameters != null && tree.parameters.Count > 0)
            {
                if (declarations.Count > 0)
                    menu.AddSeparator(string.Empty);
                foreach (GraphTaskParameter parameter in tree.parameters)
                {
                    if (parameter == null || string.IsNullOrEmpty(parameter.id)
                        || string.IsNullOrEmpty(parameter.name))
                        continue;
                    var wireId = parameter.id;
                    var wireName = parameter.name;
                    menu.AddItem(new GUIContent(
                        (wireName + "  · parameter (" + parameter.kind + ")")
                            .Replace('/', '∕')),
                        string.Equals(wireId, currentId, System.StringComparison.Ordinal),
                        () =>
                        {
                            textProperty.stringValue = wireName;
                            idProperty.stringValue = wireId;
                            property.serializedObject.ApplyModifiedProperties();
                            Build(container, property);
                        });
                }
            }

            // A TAG field's vocabulary is also the world's tag registries: the GLOBAL ones
            // the tree carries, plus every LEVEL's own (a level can mint unique tags —
            // LevelDef.tags — and the root still SEES them, grouped by level). A row pick
            // writes the TEXT (tags match by exact text at runtime); it is a known-list
            // choice, not an id wire — declarations keep the wire semantics.
            if (preferred == StateTreeKeyKind.Tag && tree != null)
            {
                var currentText = textProperty.stringValue;
                var anyRows = false;
                void AddTagRows(WorldTagRegistry tagRows, string prefix)
                {
                    if (tagRows == null)
                        return;
                    for (int j = 0; j < tagRows.entries.Count; j++)
                    {
                        WorldTagDef row = tagRows.entries[j];
                        if (row == null || string.IsNullOrEmpty(row.name))
                            continue;
                        if (!anyRows)
                        {
                            anyRows = true;
                            if (declarations.Count > 0)
                                menu.AddSeparator(string.Empty);
                        }
                        var tagName = row.name;
                        menu.AddItem(new GUIContent((prefix + tagName).Replace('\\', '∕')),
                            string.IsNullOrEmpty(currentId)
                                && string.Equals(tagName, currentText,
                                    System.StringComparison.Ordinal),
                            () =>
                            {
                                textProperty.stringValue = tagName;
                                idProperty.stringValue = string.Empty;
                                property.serializedObject.ApplyModifiedProperties();
                                Build(container, property);
                            });
                    }
                }

                for (int i = 0; i < tree.registries.Count; i++)
                {
                    if (tree.registries[i] is WorldTagRegistry global)
                        AddTagRows(global, "Tags/");
                    else if (tree.registries[i] is LevelRegistry levels)
                    {
                        for (int j = 0; j < levels.entries.Count; j++)
                        {
                            LevelDef level = levels.entries[j];
                            if (level == null)
                                continue;
                            // A level's vocabularies are the ones its manifest lists — the
                            // same single source the placement tag picker reads.
                            IReadOnlyList<WorldTagRegistry> levelTags = level.tags;
                            for (int k = 0; k < levelTags.Count; k++)
                                AddTagRows(levelTags[k], "Tags/" + level.name + "/");
                        }
                    }
                }
            }

            if (preferred != null)
            {
                var hasOthers = false;
                foreach (StateTreeKeyDeclaration declaration in declarations)
                    hasOthers |= declaration != null && declaration.kind != preferred.Value;
                if (hasOthers)
                    menu.AddSeparator(string.Empty);
                AddEntries(false);
            }

            // CODE-DECLARED keys: the constants of the game's [KeyCatalog] classes.
            var code = StateTreeKeyOffers.CodeKeys();
            if (code.Count > 0)
                menu.AddSeparator(string.Empty);
            foreach (StateTreeKeyOffer offer in code)
            {
                var keyName = offer.name;
                menu.AddItem(new GUIContent(offer.source.Split('.')[0] + "/" + keyName.Replace('/', '∕')),
                    string.IsNullOrEmpty(currentId) && keyName == textProperty.stringValue,
                    () =>
                    {
                        textProperty.stringValue = keyName;
                        idProperty.stringValue = string.Empty;
                        property.serializedObject.ApplyModifiedProperties();
                        Build(container, property);
                    });
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("(none)"), string.IsNullOrEmpty(textProperty.stringValue), () =>
            {
                textProperty.stringValue = string.Empty;
                idProperty.stringValue = string.Empty;
                property.serializedObject.ApplyModifiedProperties();
                Build(container, property);
            });
            if (tree != null)
            {
                System.Action<string> declare = name =>
                {
                    var declared = StateTreeKeyOffers.Declare(tree, name, preferred ?? StateTreeKeyKind.String, "Declare Key");
                    if (declared == null)
                        return;
                    textProperty.stringValue = declared.name;
                    idProperty.stringValue = declared.id;
                    property.serializedObject.ApplyModifiedProperties();
                    Build(container, property);
                };
                var text = textProperty.stringValue;
                if (!string.IsNullOrEmpty(text) && string.IsNullOrEmpty(currentId)
                    && !StateTreeKeyOffers.TryFind(StateTreeKeyOffers.For(owner, tree), text, out _))
                    menu.AddItem(new GUIContent("Declare '" + text.Replace('/', '∕') + "' on " + tree.name), false,
                        () => declare(text));
                menu.AddItem(new GUIContent("Declare a new key on " + tree.name + "…"), false,
                    () => StateTreeNamePrompt.Show(anchor, "A new key on " + tree.name, "", declare));
            }

            menu.DropDown(anchor);
        }

        /// <summary>The tree whose declarations this field can see: for a scene component,
        /// the nearest parent host carrying a tree, else the scene's Root host; for a tree
        /// sub-asset (a task inspected directly), the tree it lives in.</summary>
        private static StateTreeAsset TreeFor(Object target)
        {
            if (target is Component component)
            {
                Transform walk = component.transform;
                while (walk != null)
                {
                    var host = walk.GetComponent<StateTreeContextHost>();
                    if (host != null && host.tree != null)
                        return host.tree;
                    walk = walk.parent;
                }

                StateTreeContextHost[] hosts = Object.FindObjectsByType<StateTreeContextHost>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                StateTreeAsset fallback = null;
                foreach (StateTreeContextHost host in hosts)
                {
                    if (host == null || host.tree == null)
                        continue;
                    if (host.kind == StateTreeContextKind.Root)
                        return host.tree;
                    fallback = fallback != null ? fallback : host.tree;
                }
                return fallback;
            }

            var path = AssetDatabase.GetAssetPath(target);
            return string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadMainAssetAtPath(path) as StateTreeAsset;
        }

        private static GraphTaskParameter FindParameter(StateTreeAsset tree, string id)
        {
            var declared = tree != null ? tree.parameters : null;
            for (int i = 0; declared != null && i < declared.Count; ++i)
            {
                if (declared[i] != null
                    && string.Equals(declared[i].id, id, System.StringComparison.Ordinal))
                    return declared[i];
            }
            return null;
        }

        private static StateTreeKeyDeclaration FindDeclaration(Object owner,
            StateTreeAsset tree, string keyId)
        {
            var declarations = new List<StateTreeKeyDeclaration>();
            if (tree != null)
                StateTreeKeyResolver.CollectVisible(tree, declarations);
            var fromRegistries = new List<StateTreeKeyDeclaration>();
            StateTreeOffers.KeysFor(owner, fromRegistries);
            declarations.AddRange(fromRegistries);
            foreach (StateTreeKeyDeclaration declaration in declarations)
            {
                if (declaration != null
                    && string.Equals(declaration.id, keyId, System.StringComparison.Ordinal))
                    return declaration;
            }
            return null;
        }
    }
}
