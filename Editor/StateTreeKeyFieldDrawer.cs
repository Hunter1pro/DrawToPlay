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
            // An "any" field asks whether a key is there at all: it is offered flags first.
            bool presence = attribute != null && attribute.any;

            // Declared keys wire by id; code constants, parameters and tag rows by text.
            var offers = new List<StateTreeKeyOffer>();
            var names = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (StateTreeKeyDeclaration declaration in declarations)
            {
                if (declaration != null && !string.IsNullOrEmpty(declaration.name) && names.Add(declaration.name))
                    offers.Add(new StateTreeKeyOffer(declaration.name, declaration.id, "declared",
                        declaration.description, declaration.kind));
            }
            foreach (StateTreeKeyOffer code in StateTreeKeyOffers.CodeKeys())
            {
                if (names.Add(code.name))
                    offers.Add(code);
            }
            StateTreePickMenu menu = StateTreePickItems.Keys(offers, tree, preferred, presence);
            if (declarations.Count == 0 && tree == null)
                menu.empty = "No key source reachable — no tree in the parents or at the scene's Root, "
                    + "and no registry keys behind this row.";

            // The tree's declared PARAMETERS wire the same way — the caller's arguments,
            // seeded onto the blackboard under their names.
            if (tree != null && tree.parameters != null)
            {
                foreach (GraphTaskParameter parameter in tree.parameters)
                {
                    if (parameter == null || string.IsNullOrEmpty(parameter.id) || string.IsNullOrEmpty(parameter.name))
                        continue;
                    menu.items.Add(new StateTreePickItem(parameter.name, parameter.name,
                        "parameter (" + parameter.kind + ")", "Parameters")
                    {
                        payload = new StateTreeKeyOffer(parameter.name, parameter.id, "parameter", "")
                    });
                }
            }

            // A TAG field's vocabulary is also the world's tag registries: the GLOBAL ones the tree
            // carries, plus every LEVEL's own, grouped by level. A row pick writes the TEXT (tags match
            // by exact text at runtime) — a known-list choice, not an id wire.
            if (preferred == StateTreeKeyKind.Tag && tree != null)
            {
                void AddTagRows(WorldTagRegistry tagRows, string group)
                {
                    if (tagRows == null)
                        return;
                    foreach (WorldTagDef row in tagRows.entries)
                    {
                        if (row != null && !string.IsNullOrEmpty(row.name))
                            menu.items.Add(new StateTreePickItem(row.name, row.name, StateTreePickItems.Meaning(row), group)
                            {
                                payload = new StateTreeKeyOffer(row.name, "", "tag", "")
                            });
                    }
                }
                foreach (StateTreeRegistryAsset registry in tree.registries)
                {
                    if (registry is WorldTagRegistry global)
                        AddTagRows(global, "Tags");
                    else if (registry is LevelRegistry levels)
                    {
                        foreach (LevelDef level in levels.entries)
                        {
                            if (level == null)
                                continue;
                            IReadOnlyList<WorldTagRegistry> levelTags = level.tags;
                            for (int k = 0; k < levelTags.Count; k++)
                                AddTagRows(levelTags[k], "Tags/" + level.name);
                        }
                    }
                }
            }
            menu.InTree(StateTreePickMenu.UsedIn(tree));

            void Write(string name, string id)
            {
                textProperty.stringValue = name ?? string.Empty;
                idProperty.stringValue = id ?? string.Empty;
                property.serializedObject.ApplyModifiedProperties();
                Build(container, property);
            }

            if (tree != null)
            {
                System.Action<string> declare = name =>
                {
                    var declared = StateTreeKeyOffers.Declare(tree, name, preferred ?? StateTreeKeyKind.String, "Declare Key");
                    if (declared != null)
                        Write(declared.name, declared.id);
                };
                var text = textProperty.stringValue;
                if (!string.IsNullOrEmpty(text) && string.IsNullOrEmpty(idProperty.stringValue)
                    && !StateTreeKeyOffers.TryFind(StateTreeKeyOffers.For(owner, tree), text, out _))
                    menu.Command("Declare '" + text + "' on " + tree.name, () => declare(text));
                menu.Create(StateTreeKeyPick.NewKeyLabel(tree), "A new key on " + tree.name,
                    name => StateTreeKeyPick.NewKey(tree, name, preferred ?? StateTreeKeyKind.String));
            }

            menu.Show(anchor, textProperty.stringValue, picked =>
            {
                var offer = picked.payload is StateTreeKeyOffer chosen ? chosen : default;
                Write(picked.value, offer.id);
            });
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
