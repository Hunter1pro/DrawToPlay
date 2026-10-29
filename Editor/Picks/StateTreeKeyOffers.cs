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
        public StateTreeKeyOffer(string name, string id, string source, string description,
            StateTreeKeyKind? kind = null)
        {
            this.name = name;
            this.id = id ?? "";
            this.source = source ?? "";
            this.description = description ?? "";
            this.kind = kind;
        }

        /// <summary>The declared kind, or null for a constant nothing declares.</summary>
        public StateTreeKeyKind? kind { get; }

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
                        s_Code.Add(new StateTreeKeyOffer(name, "", type.Name + "." + field.Name,
                            Summary(type, field.Name)));
                }
            }
            s_Code.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return s_Code;
        }

        private static readonly Dictionary<Type, Dictionary<string, string>> s_Summaries =
            new Dictionary<Type, Dictionary<string, string>>();

        /// <summary>A constant's meaning: the first line of its &lt;summary&gt; in the catalog's source —
        /// read once per domain, with the catalog's keys.</summary>
        internal static string Summary(Type catalog, string member)
        {
            if (!s_Summaries.TryGetValue(catalog, out Dictionary<string, string> summaries))
            {
                summaries = new Dictionary<string, string>(StringComparer.Ordinal);
                s_Summaries[catalog] = summaries;
                foreach (string guid in AssetDatabase.FindAssets(catalog.Name + " t:MonoScript"))
                {
                    var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                    if (script == null || script.GetClass() != catalog)
                        continue;
                    foreach (KeyValuePair<string, string> each in ParseSummaries(script.text))
                        summaries[each.Key] = each.Value;
                }
            }
            return summaries.TryGetValue(member, out string found) ? found : "";
        }

        /// <summary>Each string constant of a source text, with the first sentence of the &lt;summary&gt;
        /// above it.</summary>
        internal static Dictionary<string, string> ParseSummaries(string source)
        {
            var summaries = new Dictionary<string, string>(StringComparer.Ordinal);
            var comment = new System.Text.StringBuilder();
            foreach (string raw in (source ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("///", StringComparison.Ordinal))
                {
                    comment.Append(' ').Append(line.Substring(3).Trim());
                    continue;
                }
                int at = line.IndexOf("const string ", StringComparison.Ordinal);
                if (at >= 0)
                {
                    string rest = line.Substring(at + 13);
                    int end = rest.IndexOfAny(new[] { ' ', '=' });
                    string constant = end > 0 ? rest.Substring(0, end) : rest;
                    string text = System.Text.RegularExpressions.Regex.Replace(comment.ToString(), "<[^>]+>", "").Trim();
                    int stop = text.IndexOf(". ", StringComparison.Ordinal);
                    summaries[constant] = StateTreePickItems.OneLine(stop > 0 ? text.Substring(0, stop + 1) : text);
                }
                if (!line.StartsWith("[", StringComparison.Ordinal))
                    comment.Clear();
            }
            return summaries;
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
                        declaration.description, declaration.kind));
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
    }
}
