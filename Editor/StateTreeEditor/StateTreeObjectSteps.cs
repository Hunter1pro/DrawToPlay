using System.Collections.Generic;
using UnityEditor;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A step ABOUT a level object, from what its preset says fits it: where in a mission tree one may go (the
    /// groups whose level is the object's manifest), which of the preset's steps a place may hold, and the step
    /// itself — appended or inserted, the object and its primary fact filled in, one undo step. What the Level
    /// Manifest's "Use in mission…" and a row dropped on the State Tree window both do.
    /// </summary>
    public static class StateTreeObjectSteps
    {
        /// <summary>The groups of <paramref name="tree"/> a step about a row of <paramref name="manifest"/> may
        /// go in — a Level whose manifest it is, and the zones and branches under it — in the tree's order.</summary>
        public static List<StateTreeNodeAsset> Places(StateTreeAsset tree, StateTreeSchema schema,
            LevelObjectRegistry manifest)
        {
            var places = new List<StateTreeNodeAsset>();
            if (tree == null || schema == null || manifest == null)
                return places;
            foreach (StateTreeNodeAsset node in StateTreeEditorOps.CollectNodes(tree))
            {
                StateTreeSchemaKind kind = StateTreeSchemaRules.KindOf(schema, node);
                if (kind == null || kind.isTemplate || schema.AllowedUnder(kind.kind).Count == 0)
                    continue;
                if (StateTreeSchemaRules.EnclosingRegistry(tree, schema, node, typeof(LevelObjectDef)) == manifest)
                    places.Add(node);
            }
            return places;
        }

        /// <summary>A step a row's preset fits, and where it would go.</summary>
        public sealed class Use
        {
            public StateTreeAsset tree;
            public StateTreeSchema schema;
            public StateTreeNodeAsset place;
            public StateTreeSchemaKind kind;

            /// <summary>"Episode/Hall › corridor/Open Door" — the menu item; the tree's name only when there are
            /// several trees, the kind's label only when the place fits several.</summary>
            public string path;
        }

        /// <summary>
        /// Every step about <paramref name="row"/> its preset fits, in every place of <paramref name="trees"/> under
        /// the row's level — the "Use in mission…" menu. Empty when the row has no preset.
        /// </summary>
        public static List<Use> Uses(IReadOnlyList<StateTreeAsset> trees, LevelObjectRegistry manifest, LevelObjectDef row)
        {
            var uses = new List<Use>();
            LevelObjectPresetDef preset = manifest != null ? manifest.PresetOf(row) : null;
            if (preset == null || trees == null)
                return uses;
            var reaching = new List<StateTreeAsset>();
            foreach (StateTreeAsset tree in trees)
            {
                StateTreeSchema schema = StateTreeSchemas.For(tree);
                if (schema == null)
                    continue;
                int before = uses.Count;
                foreach (StateTreeNodeAsset place in Places(tree, schema, manifest))
                {
                    List<StateTreeSchemaKind> fits = Fitting(schema, place, preset);
                    string where = PlaceName(tree, schema, place);
                    foreach (StateTreeSchemaKind kind in fits)
                        uses.Add(new Use
                        {
                            tree = tree, schema = schema, place = place, kind = kind,
                            path = fits.Count > 1 ? where + "/" + kind.Label : where
                        });
                }
                if (uses.Count > before)
                    reaching.Add(tree);
            }
            if (reaching.Count > 1)
            {
                foreach (Use use in uses)
                    use.path = use.tree.name + "/" + use.path;
            }
            return uses;
        }

        private static List<StateTreeAsset> s_MissionTrees;

        /// <summary>The project's trees a schema claims — found once, again after the project changes.</summary>
        public static List<StateTreeAsset> MissionTrees()
        {
            if (s_MissionTrees != null)
                return s_MissionTrees;
            s_MissionTrees = new List<StateTreeAsset>();
            EditorApplication.projectChanged -= Forget;
            EditorApplication.projectChanged += Forget;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(StateTreeAsset)))
            {
                var tree = AssetDatabase.LoadAssetAtPath<StateTreeAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (tree != null && StateTreeSchemas.For(tree) != null)
                    s_MissionTrees.Add(tree);
            }
            return s_MissionTrees;
        }

        private static void Forget()
        {
            s_MissionTrees = null;
        }

        /// <summary>The preset's steps <paramref name="parent"/> may hold now, in the preset's order.</summary>
        public static List<StateTreeSchemaKind> Fitting(StateTreeSchema schema, StateTreeNodeAsset parent,
            LevelObjectPresetDef preset)
        {
            var fits = new List<StateTreeSchemaKind>();
            if (schema == null || parent == null || preset == null)
                return fits;
            foreach (string step in preset.steps)
            {
                StateTreeSchemaKind kind = schema.Kind(step);
                if (kind != null && kind.isTemplate && !fits.Contains(kind)
                    && StateTreeSchemaRules.CanHold(schema, parent, kind.kind))
                    fits.Add(kind);
            }
            return fits;
        }

        /// <summary>"Hall › corridor › Yes" — a place by its nodes' names, from the one that names the level ("/" would
        /// open a submenu).</summary>
        public static string PlaceName(StateTreeAsset tree, StateTreeSchema schema, StateTreeNodeAsset node)
        {
            var names = new List<string>();
            for (StateTreeNodeAsset at = node; at != null; at = StateTreeEditorOps.FindParent(tree, at))
            {
                if (StateTreeSchemaRules.KindOf(schema, at) == null
                    || StateTreeSchemaRules.EnclosingRegistry(tree, schema, at, typeof(LevelObjectDef)) == null)
                    break;
                names.Insert(0, string.IsNullOrEmpty(at.displayName) ? at.nodeId : at.displayName);
            }
            return string.Join(" › ", names);
        }

        /// <summary>
        /// A step of <paramref name="kindName"/> about <paramref name="row"/> under <paramref name="parent"/>, at
        /// <paramref name="index"/> among its children (-1: last): its first placement parameter is the row, and a
        /// fact of that placement is the row's preset's primary fact. One undo step; null when the rules refuse.
        /// </summary>
        public static StateTreeNodeAsset Add(StateTreeAsset tree, StateTreeSchema schema, StateTreeNodeAsset parent,
            int index, string kindName, LevelObjectRegistry manifest, LevelObjectDef row)
        {
            StateTreeSchemaKind kind = schema != null ? schema.Kind(kindName) : null;
            if (tree == null || parent == null || kind == null || !kind.isTemplate || row == null
                || !StateTreeSchemaRules.CanHold(schema, parent, kind.kind))
                return null;
            string undo = "Use " + (string.IsNullOrEmpty(row.name) ? row.id : row.name);
            int group = StateTreeEditorOps.BeginUndoGroup(undo);
            StateTreeNodeAsset node = StateTreeSchemaRules.AddNode(tree, schema, parent, kind.kind);
            if (node != null)
            {
                if (index >= 0 && index < parent.children.Count - 1)
                {
                    StateTreeEditorOps.MoveNode(tree, node, parent, index, undo);
                    StateTreeSchemaRules.ApplyDerived(tree, schema, parent);
                }
                Fill(tree, schema, node, kind, manifest, row, undo);
                StateTreeEditorOps.RefreshSubAssetNames(tree);
            }
            StateTreeEditorOps.EndUndoGroup(group);
            return node;
        }

        private static void Fill(StateTreeAsset tree, StateTreeSchema schema, StateTreeNodeAsset node,
            StateTreeSchemaKind kind, LevelObjectRegistry manifest, LevelObjectDef row, string undo)
        {
            RunSubTreeTask run = StateTreeSchemaRules.TemplateTask(kind, node);
            if (run == null)
                return;
            List<StateTreeStepParameter> parameters = StateTreeSchemaRules.Parameters(kind, node);
            StateTreeStepParameter about = parameters.Find(p => p.isPlacement && !p.isDerived);
            if (about == null)
                return;
            StateTreeSchemaRules.SetParameter(run, about.declaration, row.id, 0f, row.id, undo);
            LevelObjectPresetFact fact = manifest != null ? manifest.PresetOf(row)?.primaryFact : null;
            if (fact != null)
            {
                foreach (StateTreeStepParameter key in parameters)
                {
                    if (key.isKey && !key.isDerived
                        && StateTreeSchemaRules.FactTarget(kind.template, key.declaration.id) == about.declaration.id)
                        StateTreeSchemaRules.SetParameter(run, key.declaration, fact.key.text, 0f, "", undo, fact.key.keyId);
                }
            }
            StateTreeSchemaRules.ApplyDerived(tree, schema, node);
        }
    }
}
