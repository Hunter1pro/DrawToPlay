using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A DOMAIN for state trees of one kind — the HT typed editors' node palette and hierarchy
    /// rules, carried by an asset the game supplies instead of by an editor per domain. A tree whose
    /// <see cref="StateTreeAsset.treeKind"/> a schema claims opens the State Tree window in that
    /// schema's mode (mission mode, for a game's missions): the same asset, the same window, its
    /// domain nodes shown with their raw parts hidden, Add offering only what the rules allow.
    ///
    /// A domain node is an ordinary state whose <see cref="StateTreeNodeAsset.roleKind"/> names one
    /// of <see cref="kinds"/>. Nothing here runs: the runtime sees states, sub-tree tasks and
    /// transitions, as it always did.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/State Tree Schema", fileName = "StateTreeSchema")]
    public sealed class StateTreeSchema : ScriptableObject
    {
        /// <summary>In a kind's <see cref="StateTreeSchemaKind.children"/>: every kind marked
        /// <see cref="StateTreeSchemaKind.step"/>.</summary>
        public const string StepToken = "step";

        [Tooltip("Trees of this kind open in this schema's mode.")]
        public string treeKind = "";

        [Tooltip("What the mode is called in the window's toolbar.")]
        public string modeName = "Mission";

        [Tooltip("Kinds that may sit directly under a state that is not a domain node (the tree's "
            + "root, usually).")]
        public List<string> topKinds = new List<string>();

        public List<StateTreeSchemaKind> kinds = new List<StateTreeSchemaKind>();

        public StateTreeSchemaKind Kind(string kind)
        {
            if (string.IsNullOrEmpty(kind))
                return null;
            for (int i = 0; i < kinds.Count; i++)
            {
                if (kinds[i] != null && string.Equals(kinds[i].kind, kind, StringComparison.Ordinal))
                    return kinds[i];
            }
            return null;
        }

        /// <summary>Whether <paramref name="childKind"/> may sit under a node of
        /// <paramref name="parentKind"/>; an empty parent kind is a non-domain parent.</summary>
        public bool Allows(string parentKind, string childKind)
        {
            StateTreeSchemaKind child = Kind(childKind);
            if (child == null)
                return false;
            List<string> allowed;
            if (string.IsNullOrEmpty(parentKind))
                allowed = topKinds;
            else
            {
                StateTreeSchemaKind parent = Kind(parentKind);
                if (parent == null)
                    return false;
                allowed = parent.children;
            }
            for (int i = 0; allowed != null && i < allowed.Count; i++)
            {
                if (string.Equals(allowed[i], child.kind, StringComparison.Ordinal)
                    || (child.step && string.Equals(allowed[i], StepToken, StringComparison.Ordinal)))
                    return true;
            }
            return false;
        }

        /// <summary>The palette under a node of <paramref name="parentKind"/>, in schema order.</summary>
        public List<StateTreeSchemaKind> AllowedUnder(string parentKind)
        {
            var offered = new List<StateTreeSchemaKind>();
            for (int i = 0; i < kinds.Count; i++)
            {
                if (kinds[i] != null && Allows(parentKind, kinds[i].kind))
                    offered.Add(kinds[i]);
            }
            return offered;
        }
    }

    /// <summary>One node kind of a <see cref="StateTreeSchema"/>.</summary>
    [Serializable]
    public sealed class StateTreeSchemaKind
    {
        [Tooltip("The roleKind a node of this kind carries.")]
        public string kind = "";

        public string label = "";

        [Tooltip("A glyph shown before the node's name.")]
        public string icon = "";

        public Color color = new Color(0.55f, 0.75f, 1f, 1f);

        [Tooltip("Kinds a node of this kind may hold; 'step' = every step kind. Empty = a leaf.")]
        public List<string> children = new List<string>();

        [Tooltip("A step: offered wherever a kind lists 'step'.")]
        public bool step;

        [Tooltip("A reusable tree: a node of this kind is a state holding ONE sub-tree task of it, "
            + "its parameters set by the node. Empty = a group.")]
        public StateTreeAsset template;

        [Tooltip("A group's data: the full name of a task type the node holds as its context. "
            + "The task is never run — a group is never entered.")]
        public string contextTask = "";

        [Tooltip("How many children it may hold at most; 0 = any.")]
        public int childLimit;

        [Tooltip("The new state's id starts with this.")]
        public string idPrefix = "";

        [Tooltip("Read-only, in the inspector: where a quit during it comes back to.")]
        [TextArea] public string resume = "";

        [Tooltip("Read-only, in the inspector: whether it saves the game.")]
        public bool saves;

        [TextArea] public string help = "";

        public string Label => string.IsNullOrEmpty(label) ? kind : label;

        public bool isTemplate => template != null;

        public Type ContextType()
        {
            if (string.IsNullOrEmpty(contextTask))
                return null;
            Type type = StateTreeSchemas.FindType(contextTask);
            return type != null && typeof(StateTreeTaskAsset).IsAssignableFrom(type) ? type : null;
        }
    }

    /// <summary>
    /// Which schema claims a tree. Schemas are found by type once and cached until the project
    /// changes — the window asks on every load, and a project walk there would be per click.
    /// Tests register one directly.
    /// </summary>
    [InitializeOnLoad]
    public static class StateTreeSchemas
    {
        private static Dictionary<string, StateTreeSchema> s_ByKind;
        private static readonly List<StateTreeSchema> s_Registered = new List<StateTreeSchema>();
        private static readonly Dictionary<string, Type> s_Types = new Dictionary<string, Type>();

        static StateTreeSchemas()
        {
            EditorApplication.projectChanged += Invalidate;
        }

        public static StateTreeSchema For(StateTreeAsset tree)
        {
            return tree != null ? For(tree.treeKind) : null;
        }

        public static StateTreeSchema For(string treeKind)
        {
            if (string.IsNullOrEmpty(treeKind))
                return null;
            for (int i = s_Registered.Count - 1; i >= 0; i--)
            {
                if (s_Registered[i] == null)
                    s_Registered.RemoveAt(i);
                else if (string.Equals(s_Registered[i].treeKind, treeKind, StringComparison.Ordinal))
                    return s_Registered[i];
            }
            if (s_ByKind == null)
                Scan();
            return s_ByKind.TryGetValue(treeKind, out StateTreeSchema found) && found != null
                ? found
                : null;
        }

        /// <summary>A schema that wins over any asset of the same kind — tests, or a game that
        /// builds its schema in code.</summary>
        public static void Register(StateTreeSchema schema)
        {
            if (schema != null && !s_Registered.Contains(schema))
                s_Registered.Add(schema);
        }

        public static void Unregister(StateTreeSchema schema)
        {
            s_Registered.Remove(schema);
        }

        public static void Invalidate()
        {
            s_ByKind = null;
        }

        internal static Type FindType(string fullName)
        {
            if (s_Types.TryGetValue(fullName, out Type known))
                return known;
            Type found = null;
            foreach (Type candidate in TypeCache.GetTypesDerivedFrom<StateTreeTaskAsset>())
            {
                if (candidate.FullName == fullName || candidate.Name == fullName)
                {
                    found = candidate;
                    break;
                }
            }
            s_Types[fullName] = found;
            return found;
        }

        private static void Scan()
        {
            s_ByKind = new Dictionary<string, StateTreeSchema>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(StateTreeSchema)))
            {
                var schema = AssetDatabase.LoadAssetAtPath<StateTreeSchema>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (schema != null && !string.IsNullOrEmpty(schema.treeKind)
                    && !s_ByKind.ContainsKey(schema.treeKind))
                    s_ByKind.Add(schema.treeKind, schema);
            }
        }
    }
}
