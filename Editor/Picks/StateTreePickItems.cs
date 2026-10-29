using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The pickers' offers as <see cref="StateTreePickMenu"/> items — keys grouped by prefix, rows by
    /// catalog and group, placements by manifest and group, each with its one-line meaning — and the
    /// narrowing each kind of field asks for. Called when a picker opens.
    /// </summary>
    public static class StateTreePickItems
    {
        // --- keys ------------------------------------------------------------------------------

        /// <summary>Whether a key of <paramref name="kind"/> fits a field: a presence check
        /// (<paramref name="presence"/>, "is it set?") takes flags — Bool and Event keys; a typed field
        /// takes its own kind. A key whose kind is not declared fits nothing narrowed.</summary>
        public static bool Fits(StateTreeKeyKind? kind, StateTreeKeyKind? field, bool presence)
        {
            if (presence)
                return kind == StateTreeKeyKind.Bool || kind == StateTreeKeyKind.Event;
            if (field == null)
                return true;
            return kind == field;
        }

        public static StateTreePickMenu Keys(List<StateTreeKeyOffer> offers, StateTreeAsset tree,
            StateTreeKeyKind? field, bool presence)
        {
            var menu = new StateTreePickMenu
            {
                title = presence ? "Flag key" : field != null ? field + " key" : "Key",
                other = presence ? "Other keys (not flags)" : field != null ? "Other kinds of key" : "Other",
                empty = "No key declared here yet"
            };
            bool narrowed = presence || field != null;
            foreach (StateTreeKeyOffer offer in offers)
            {
                if (string.IsNullOrEmpty(offer.name))
                    continue;
                string meaning = offer.description;
                if (string.IsNullOrEmpty(meaning))
                    meaning = offer.source == "declared" ? "declared" : offer.source;
                if (offer.kind != null)
                    meaning = offer.kind + " · " + meaning;
                menu.items.Add(new StateTreePickItem(offer.name, offer.name, meaning, StateTreePickMenu.PrefixGroup(offer.name))
                {
                    fits = !narrowed || Fits(offer.kind, field, presence),
                    payload = offer
                });
            }
            return menu.InTree(StateTreePickMenu.UsedIn(tree));
        }

        // --- rows ------------------------------------------------------------------------------

        /// <summary>Rows of <paramref name="catalogs"/>; when <paramref name="narrowTo"/> is set (the
        /// level's own catalog), the others' rows are set aside under "Other".</summary>
        public static StateTreePickMenu Rows(List<StateTreeRegistryAsset> catalogs, StateTreeAsset tree,
            Type entryType, StateTreeRegistryAsset narrowTo = null)
        {
            string kind = entryType != null ? ObjectNames.NicifyVariableName(entryType.Name.Replace("Def", "")) : "Row";
            var menu = new StateTreePickMenu
            {
                title = kind,
                other = "Other catalogs",
                empty = "No rows to pick — add one to the catalog"
            };
            foreach (StateTreeRegistryAsset catalog in catalogs)
            {
                for (int i = 0; catalog != null && i < catalog.Count; i++)
                {
                    StateTreeRegistryEntry row = catalog.EntryAt(i);
                    if (row == null || string.IsNullOrEmpty(row.name))
                        continue;
                    menu.items.Add(new StateTreePickItem(row.name, row.name, Meaning(row),
                        (catalogs.Count > 1 ? catalog.name + "/" : "") + (row.group ?? ""))
                    {
                        fits = narrowTo == null || catalog == narrowTo,
                        payload = row
                    });
                }
            }
            return menu.InTree(StateTreePickMenu.UsedIn(tree));
        }

        /// <summary>A row's one line: its own words (the first text field its class adds), else what it
        /// says about itself.</summary>
        public static string Meaning(StateTreeRegistryEntry row)
        {
            if (row == null)
                return "";
            foreach (FieldInfo field in row.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(string) || field.Name == "id" || field.Name == "name" || field.Name == "group")
                    continue;
                if (field.GetValue(row) is string text && !string.IsNullOrWhiteSpace(text))
                    return OneLine(text);
            }
            return OneLine(row.Describe() ?? "");
        }

        internal static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            int end = text.IndexOfAny(new[] { '\n', '\r' });
            text = (end >= 0 ? text.Substring(0, end) : text).Trim();
            return text.Length > 80 ? text.Substring(0, 79) + "…" : text;
        }

        // --- placements --------------------------------------------------------------------------

        /// <summary>Placements of <paramref name="rows"/>, written by id and shown by name; the level's own
        /// manifest (<paramref name="narrowTo"/>) first, the rest under "Other levels".</summary>
        public static StateTreePickMenu Placements(List<LevelObjectDef> rows, StateTreeAsset tree,
            LevelObjectRegistry narrowTo)
        {
            var menu = new StateTreePickMenu
            {
                title = "Placement",
                other = "Other levels",
                empty = "Nothing here declares a manifest"
            };
            var owners = new Dictionary<LevelObjectDef, LevelObjectRegistry>();
            foreach (string guid in narrowTo != null ? ManifestGuids() : Array.Empty<string>())
            {
                var manifest = AssetDatabase.LoadAssetAtPath<LevelObjectRegistry>(AssetDatabase.GUIDToAssetPath(guid));
                if (manifest == null)
                    continue;
                for (int i = 0; i < manifest.Count; i++)
                {
                    if (manifest.EntryAt(i) is LevelObjectDef row)
                        owners[row] = manifest;
                }
            }
            foreach (LevelObjectDef row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.id))
                    continue;
                owners.TryGetValue(row, out LevelObjectRegistry manifest);
                bool fits = narrowTo == null || narrowTo.entries.Contains(row);
                string where = narrowTo != null && !fits && manifest != null ? manifest.name + "/" : "";
                menu.items.Add(new StateTreePickItem(row.id, row.name,
                    (row.kind != null && !string.IsNullOrEmpty(row.kind.entryName) ? row.kind.entryName : "placement") + " · " + row.id,
                    where + (row.group ?? ""))
                {
                    fits = fits,
                    payload = row
                });
            }
            return menu.InTree(StateTreePickMenu.UsedIn(tree));
        }

        private static string[] s_Manifests;

        /// <summary>The project's manifests, found once and dropped on a project change.</summary>
        private static string[] ManifestGuids()
        {
            return s_Manifests ??= AssetDatabase.FindAssets("t:" + nameof(LevelObjectRegistry));
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.projectChanged -= Invalidate;
            EditorApplication.projectChanged += Invalidate;
        }

        private static void Invalidate() => s_Manifests = null;

        // --- tags --------------------------------------------------------------------------------

        public static StateTreePickMenu Tags(List<WorldTagDef> tags, StateTreeAsset tree)
        {
            var menu = new StateTreePickMenu { title = "Tag", empty = "Nothing here declares a tag vocabulary" };
            foreach (WorldTagDef tag in tags)
            {
                if (tag == null || string.IsNullOrEmpty(tag.name))
                    continue;
                menu.items.Add(new StateTreePickItem(tag.name, tag.name, Meaning(tag), tag.group ?? "") { payload = tag });
            }
            return menu.InTree(StateTreePickMenu.UsedIn(tree));
        }

        // --- where a field sits -------------------------------------------------------------------

        /// <summary>The tree a task or condition lives in, and the state that holds it (or null).</summary>
        public static StateTreeAsset Owning(UnityEngine.Object target, out StateTreeNodeAsset node)
        {
            node = null;
            string path = target != null ? AssetDatabase.GetAssetPath(target) : "";
            var tree = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path) as StateTreeAsset;
            if (tree == null)
                return null;
            foreach (StateTreeNodeAsset each in StateTreeEditorOps.CollectNodes(tree))
            {
                if (each == null)
                    continue;
                if (each == target || each.tasks.Contains(target as StateTreeTaskAsset))
                {
                    node = each;
                    return tree;
                }
                foreach (StateTreeTransition transition in each.transitions)
                {
                    if (Holds(transition?.condition, target, 0))
                    {
                        node = each;
                        return tree;
                    }
                }
            }
            return tree;
        }

        private static bool Holds(StateTreeConditionAsset condition, UnityEngine.Object target, int depth)
        {
            if (condition == null || depth > 8)
                return false;
            if (condition == target)
                return true;
            foreach (FieldInfo field in condition.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.GetValue(condition) is StateTreeConditionAsset inner && Holds(inner, target, depth + 1))
                    return true;
                if (field.GetValue(condition) is List<StateTreeConditionAsset> list)
                {
                    foreach (StateTreeConditionAsset each in list)
                    {
                        if (Holds(each, target, depth + 1))
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>The catalog of <paramref name="entryType"/> the enclosing groups of the field's state
        /// name (a Level's manifest, a Zone's rows) — what the picker narrows to. Null outside a schema's
        /// tree, or when no group says.</summary>
        public static StateTreeRegistryAsset Enclosing(UnityEngine.Object target, Type entryType)
        {
            StateTreeAsset tree = Owning(target, out StateTreeNodeAsset node);
            StateTreeSchema schema = StateTreeSchemas.For(tree);
            if (tree == null || node == null || schema == null)
                return null;
            return StateTreeSchemaRules.EnclosingRegistry(tree, schema, node, entryType);
        }
    }
}
