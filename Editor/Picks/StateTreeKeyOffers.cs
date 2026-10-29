using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>One key a picker may offer: its runtime name, the declaration's id when it has one
    /// (a code constant has none), where it is declared and what it means.</summary>
    public readonly struct StateTreeKeyOffer
    {
        public StateTreeKeyOffer(string name, string id, string source, string description)
        {
            this.name = name;
            this.id = id ?? "";
            this.source = source ?? "";
            this.description = description ?? "";
        }

        public string name { get; }

        public string id { get; }

        public string source { get; }

        public string description { get; }
    }

    /// <summary>
    /// Every key a key picker offers, from one place: the tree's declarations and its imports', the
    /// keys of the registries it reaches, its parameters, and the constants of every
    /// <see cref="KeyCatalogAttribute"/> class. A key is picked from here or declared here — never typed.
    /// </summary>
    public static class StateTreeKeyOffers
    {
        private static List<StateTreeKeyOffer> s_Code;

        /// <summary>The constants of every [KeyCatalog] class — types only change on a domain reload,
        /// which clears this.</summary>
        public static IReadOnlyList<StateTreeKeyOffer> CodeKeys()
        {
            if (s_Code != null)
                return s_Code;
            s_Code = new List<StateTreeKeyOffer>();
            foreach (Type type in TypeCache.GetTypesWithAttribute<KeyCatalogAttribute>())
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    // A constant that carries an attribute is declared for something else (a request).
                    if (!field.IsLiteral || field.FieldType != typeof(string)
                        || field.GetCustomAttributes(false).Length > 0)
                        continue;
                    var name = field.GetRawConstantValue() as string;
                    if (!string.IsNullOrEmpty(name))
                        s_Code.Add(new StateTreeKeyOffer(name, "", type.Name + "." + field.Name, ""));
                }
            }
            s_Code.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return s_Code;
        }

        /// <summary>The keys an asset can see. <paramref name="tree"/> may be null (a registry row).</summary>
        public static List<StateTreeKeyOffer> For(UnityEngine.Object owner, StateTreeAsset tree)
        {
            var offers = new List<StateTreeKeyOffer>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var declared = new List<StateTreeKeyDeclaration>();
            if (tree != null)
                StateTreeKeyResolver.CollectVisible(tree, declared);
            var fromRegistries = new List<StateTreeKeyDeclaration>();
            StateTreeOffers.KeysFor(owner, fromRegistries);
            declared.AddRange(fromRegistries);
            foreach (StateTreeKeyDeclaration declaration in declared)
            {
                if (declaration != null && !string.IsNullOrEmpty(declaration.name) && names.Add(declaration.name))
                    offers.Add(new StateTreeKeyOffer(declaration.name, declaration.id, "declared",
                        declaration.description));
            }
            foreach (StateTreeKeyOffer code in CodeKeys())
            {
                if (names.Add(code.name))
                    offers.Add(code);
            }
            return offers;
        }

        public static bool TryFind(List<StateTreeKeyOffer> offers, string name, out StateTreeKeyOffer found)
        {
            for (int i = 0; offers != null && i < offers.Count; i++)
            {
                if (string.Equals(offers[i].name, name, StringComparison.Ordinal))
                {
                    found = offers[i];
                    return true;
                }
            }
            found = default;
            return false;
        }

        /// <summary>A new declaration on <paramref name="tree"/> — the one place a key's name is typed.</summary>
        public static StateTreeKeyDeclaration Declare(StateTreeAsset tree, string name, StateTreeKeyKind kind,
            string undoName)
        {
            if (tree == null || string.IsNullOrWhiteSpace(name))
                return null;
            Undo.RecordObject(tree, undoName);
            tree.keys ??= new List<StateTreeKeyDeclaration>();
            foreach (StateTreeKeyDeclaration existing in tree.keys)
            {
                if (existing != null && existing.name == name)
                    return existing;
            }
            var declaration = new StateTreeKeyDeclaration
            {
                id = Guid.NewGuid().ToString("N"),
                name = name.Trim(),
                kind = kind
            };
            tree.keys.Add(declaration);
            EditorUtility.SetDirty(tree);
            return declaration;
        }

        /// <summary>A menu of the offers: declared first, then each code catalog's under its class.</summary>
        public static void AddTo(GenericMenu menu, List<StateTreeKeyOffer> offers, string current,
            Action<StateTreeKeyOffer> picked)
        {
            if (offers.Count == 0)
                menu.AddDisabledItem(new GUIContent("No key declared here yet"));
            foreach (StateTreeKeyOffer offer in offers)
            {
                StateTreeKeyOffer chosen = offer;
                string group = offer.source == "declared" ? "" : offer.source.Split('.')[0] + "/";
                menu.AddItem(new GUIContent(group + offer.name.Replace('/', '∕')),
                    string.Equals(offer.name, current, StringComparison.Ordinal), () => picked(chosen));
            }
        }
    }
}
