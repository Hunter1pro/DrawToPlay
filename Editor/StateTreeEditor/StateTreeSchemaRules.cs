using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>One thing wrong with a domain node, said the way a designer reads it.</summary>
    public readonly struct StateTreeSchemaProblem
    {
        public StateTreeSchemaProblem(StateTreeNodeAsset node, string message)
        {
            this.node = node;
            this.message = message;
        }

        public StateTreeNodeAsset node { get; }

        public string message { get; }
    }

    /// <summary>A template parameter as a step shows it: its declaration, the field it is bound to
    /// inside the template (which decides its picker), and what the step currently gives it.</summary>
    public sealed class StateTreeStepParameter
    {
        public GraphTaskParameter declaration;

        /// <summary>The template field the parameter is written into, or null when nothing binds it.</summary>
        public FieldInfo field;

        public GraphTaskParameterOverride overrideRow;

        public bool overridden => overrideRow != null && overrideRow.enabled;

        public string stringValue => overridden ? overrideRow.stringValue ?? "" : declaration.stringValue ?? "";

        public float floatValue => overridden ? overrideRow.floatValue : declaration.floatValue;

        public bool isPlacement => field != null && field.FieldType == typeof(string)
            && field.IsDefined(typeof(PlacementIdAttribute), true);

        public Type entryType
        {
            get
            {
                if (field == null || !field.FieldType.IsGenericType
                    || field.FieldType.GetGenericTypeDefinition() != typeof(StateTreeEntryRef<>))
                    return null;
                return field.FieldType.GetGenericArguments()[0];
            }
        }

        public bool isKey => field != null && field.FieldType == typeof(StateTreeKeyField);
    }

    /// <summary>
    /// The hierarchy rules, the node factory and the context lookups of a schema's mode — what the
    /// window, the inspector, the gizmos and a game's build script all ask, kept headless so the
    /// rules are tested without a window.
    /// </summary>
    public static class StateTreeSchemaRules
    {
        /// <summary>The schema kind of a node, or null for a plain state.</summary>
        public static StateTreeSchemaKind KindOf(StateTreeSchema schema, StateTreeNodeAsset node)
        {
            return schema != null && node != null ? schema.Kind(node.roleKind) : null;
        }

        public static bool IsDomain(StateTreeSchema schema, StateTreeNodeAsset node)
        {
            return KindOf(schema, node) != null;
        }

        /// <summary>The domain nodes whose parent is not one — the tops of the mode's tree.</summary>
        public static List<StateTreeNodeAsset> DomainRoots(StateTreeAsset tree, StateTreeSchema schema)
        {
            var roots = new List<StateTreeNodeAsset>();
            if (tree == null || tree.root == null || schema == null)
                return roots;
            Collect(tree.root, false, schema, roots, 0);
            return roots;
        }

        private static void Collect(StateTreeNodeAsset node, bool parentIsDomain, StateTreeSchema schema,
            List<StateTreeNodeAsset> into, int depth)
        {
            if (node == null || depth > StateTreeEditorOps.DepthGuard)
                return;
            bool domain = IsDomain(schema, node);
            if (domain && !parentIsDomain)
                into.Add(node);
            for (int i = 0; i < node.children.Count; i++)
                Collect(node.children[i], domain, schema, into, depth + 1);
        }

        /// <summary>The domain children of a domain node, in order.</summary>
        public static List<StateTreeNodeAsset> DomainChildren(StateTreeSchema schema, StateTreeNodeAsset node)
        {
            var children = new List<StateTreeNodeAsset>();
            if (node == null)
                return children;
            for (int i = 0; i < node.children.Count; i++)
            {
                if (IsDomain(schema, node.children[i]))
                    children.Add(node.children[i]);
            }
            return children;
        }

        /// <summary>The kind a node's children are checked against: its own, or "" when it is a
        /// plain state (the top of the mode).</summary>
        public static string ParentKind(StateTreeSchema schema, StateTreeNodeAsset parent)
        {
            StateTreeSchemaKind kind = KindOf(schema, parent);
            return kind != null ? kind.kind : "";
        }

        public static bool CanHold(StateTreeSchema schema, StateTreeNodeAsset parent, string childKind)
        {
            if (schema == null)
                return false;
            StateTreeSchemaKind parentKind = KindOf(schema, parent);
            if (!schema.Allows(parentKind != null ? parentKind.kind : "", childKind))
                return false;
            return parentKind == null || parentKind.childLimit <= 0
                || DomainChildren(schema, parent).Count < parentKind.childLimit;
        }

        /// <summary>The domain node a state belongs to: itself, or its nearest domain ancestor —
        /// what Play highlights while the state runs.</summary>
        public static StateTreeNodeAsset OwnerOf(StateTreeAsset tree, StateTreeSchema schema, string nodeId)
        {
            if (tree == null || string.IsNullOrEmpty(nodeId))
                return null;
            List<StateTreeNodeAsset> nodes = StateTreeEditorOps.CollectNodes(tree);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].nodeId != nodeId)
                    continue;
                for (StateTreeNodeAsset at = nodes[i]; at != null; at = StateTreeEditorOps.FindParent(tree, at))
                {
                    if (IsDomain(schema, at))
                        return at;
                }
                return null;
            }
            return null;
        }

        /// <summary>The single sub-tree task a template node holds, or null.</summary>
        public static RunSubTreeTask TemplateTask(StateTreeSchemaKind kind, StateTreeNodeAsset node)
        {
            if (kind == null || kind.template == null || node == null)
                return null;
            for (int i = 0; i < node.tasks.Count; i++)
            {
                if (node.tasks[i] is RunSubTreeTask run && run.subTree == kind.template)
                    return run;
            }
            return null;
        }

        /// <summary>A group's context task, or null.</summary>
        public static StateTreeTaskAsset ContextTask(StateTreeSchemaKind kind, StateTreeNodeAsset node)
        {
            Type type = kind != null ? kind.ContextType() : null;
            if (type == null || node == null)
                return null;
            for (int i = 0; i < node.tasks.Count; i++)
            {
                if (node.tasks[i] != null && type.IsInstanceOfType(node.tasks[i]))
                    return node.tasks[i];
            }
            return null;
        }

        // --- validation --------------------------------------------------------------------

        public static List<StateTreeSchemaProblem> Validate(StateTreeAsset tree, StateTreeSchema schema)
        {
            var problems = new List<StateTreeSchemaProblem>();
            if (tree == null || tree.root == null || schema == null)
                return problems;
            Visit(tree.root, null, schema, problems, 0);
            return problems;
        }

        private static void Visit(StateTreeNodeAsset node, StateTreeNodeAsset parent, StateTreeSchema schema,
            List<StateTreeSchemaProblem> problems, int depth)
        {
            if (node == null || depth > StateTreeEditorOps.DepthGuard)
                return;

            StateTreeSchemaKind kind = KindOf(schema, node);
            StateTreeSchemaKind parentKind = KindOf(schema, parent);
            if (kind == null && !string.IsNullOrEmpty(node.roleKind) && parentKind != null)
                problems.Add(new StateTreeSchemaProblem(node,
                    $"'{node.roleKind}' is not a kind of the {schema.modeName} schema."));
            if (kind == null && parentKind != null)
                problems.Add(new StateTreeSchemaProblem(parent,
                    $"Holds a programmer state '{node.nodeId}', which {schema.modeName} mode hides."));

            if (kind != null)
            {
                string under = parentKind != null ? parentKind.kind : "";
                if (parent != null && !schema.Allows(under, kind.kind))
                {
                    problems.Add(new StateTreeSchemaProblem(node, parentKind != null
                        ? $"A {kind.Label} cannot sit under a {parentKind.Label}."
                        : $"A {kind.Label} cannot sit at the top — it belongs under "
                        + Holders(schema, kind) + "."));
                }

                int count = DomainChildren(schema, node).Count;
                if (kind.childLimit > 0 && count > kind.childLimit)
                    problems.Add(new StateTreeSchemaProblem(node,
                        $"Holds {count} — a {kind.Label} holds at most {kind.childLimit}."));

                if (kind.isTemplate)
                    ValidateTemplate(node, kind, problems);
                else
                {
                    for (int i = 0; i < node.tasks.Count; i++)
                    {
                        Type context = kind.ContextType();
                        if (node.tasks[i] != null && (context == null || !context.IsInstanceOfType(node.tasks[i])))
                        {
                            problems.Add(new StateTreeSchemaProblem(node,
                                $"A {kind.Label} is a group: it holds no tasks of its own."));
                            break;
                        }
                    }
                    if (kind.ContextType() != null && ContextTask(kind, node) == null)
                        problems.Add(new StateTreeSchemaProblem(node,
                            $"Has no {kind.ContextType().Name} — the {kind.Label}'s context."));
                }
            }

            for (int i = 0; i < node.children.Count; i++)
                Visit(node.children[i], node, schema, problems, depth + 1);
        }

        private static void ValidateTemplate(StateTreeNodeAsset node, StateTreeSchemaKind kind,
            List<StateTreeSchemaProblem> problems)
        {
            RunSubTreeTask run = TemplateTask(kind, node);
            if (run == null)
            {
                problems.Add(new StateTreeSchemaProblem(node,
                    $"Does not run its template '{kind.template.name}'."));
                return;
            }
            if (node.tasks.Count != 1)
                problems.Add(new StateTreeSchemaProblem(node,
                    $"Holds {node.tasks.Count} tasks — a {kind.Label} is its template alone."));

            foreach (StateTreeStepParameter parameter in Parameters(kind, node))
            {
                // A placement or a row is what a step is ABOUT; a text or a key may be empty on
                // purpose (an unsaved film names no seen fact).
                if ((parameter.isPlacement || parameter.entryType != null)
                    && string.IsNullOrEmpty(parameter.stringValue))
                    problems.Add(new StateTreeSchemaProblem(node,
                        $"'{Pretty(parameter.declaration.name)}' is not set."));
            }
        }

        private static string Holders(StateTreeSchema schema, StateTreeSchemaKind child)
        {
            var names = new List<string>();
            foreach (StateTreeSchemaKind candidate in schema.kinds)
            {
                if (candidate != null && schema.Allows(candidate.kind, child.kind))
                    names.Add("a " + candidate.Label);
            }
            return names.Count > 0 ? string.Join(" or ", names) : "nothing";
        }

        // --- parameters --------------------------------------------------------------------

        /// <summary>A template node's parameters, each with the template field it is bound to.</summary>
        public static List<StateTreeStepParameter> Parameters(StateTreeSchemaKind kind, StateTreeNodeAsset node)
        {
            var parameters = new List<StateTreeStepParameter>();
            RunSubTreeTask run = TemplateTask(kind, node);
            if (run == null || run.subTree == null)
                return parameters;
            foreach (GraphTaskParameter declaration in run.subTree.parameters)
            {
                if (declaration == null || string.IsNullOrEmpty(declaration.name))
                    continue;
                GraphTaskParameterOverride row = null;
                for (int i = 0; run.overrides != null && i < run.overrides.Count; i++)
                {
                    if (run.overrides[i] != null && run.overrides[i].Matches(declaration))
                    {
                        row = run.overrides[i];
                        break;
                    }
                }
                parameters.Add(new StateTreeStepParameter
                {
                    declaration = declaration,
                    field = BoundField(run.subTree, declaration.id),
                    overrideRow = row
                });
            }
            return parameters;
        }

        /// <summary>The first template field a parameter is bound into — the field's type and
        /// attributes are what the step's picker follows.</summary>
        public static FieldInfo BoundField(StateTreeAsset template, string parameterId)
        {
            if (template == null || string.IsNullOrEmpty(parameterId))
                return null;
            foreach (StateTreeNodeAsset node in StateTreeEditorOps.CollectNodes(template))
            {
                foreach (StateTreeFieldBinding binding in node.bindings)
                {
                    if (binding == null || binding.sourceKind != StateTreeFieldBinding.SourceKind.Parameter
                        || binding.parameterId != parameterId)
                        continue;
                    UnityEngine.Object target = StateTreeEditorOps.ResolveBindingTarget(node, binding.targetKind, binding.targetIndex);
                    FieldInfo field = target != null
                        ? target.GetType().GetField(binding.fieldName, BindingFlags.Public | BindingFlags.Instance)
                        : null;
                    if (field != null)
                        return field;
                }
            }
            return null;
        }

        /// <summary>Give a step's parameter a value — the override row the sub-tree task carries.</summary>
        public static void SetParameter(RunSubTreeTask run, GraphTaskParameter declaration, string text,
            float number, string entryId, string undoName)
        {
            if (run == null || declaration == null)
                return;
            Undo.RecordObject(run, undoName);
            run.overrides ??= new List<GraphTaskParameterOverride>();
            GraphTaskParameterOverride row = null;
            for (int i = 0; i < run.overrides.Count; i++)
            {
                if (run.overrides[i] != null && run.overrides[i].Matches(declaration))
                    row = run.overrides[i];
            }
            if (row == null)
            {
                row = new GraphTaskParameterOverride { id = declaration.id, name = declaration.name };
                run.overrides.Add(row);
            }
            row.name = declaration.name;
            row.enabled = true;
            row.stringValue = text ?? "";
            row.floatValue = number;
            row.entryId = entryId ?? "";
            EditorUtility.SetDirty(run);
        }

        public static void ResetParameter(RunSubTreeTask run, GraphTaskParameter declaration, string undoName)
        {
            if (run == null || run.overrides == null || declaration == null)
                return;
            Undo.RecordObject(run, undoName);
            run.overrides.RemoveAll(row => row != null && row.Matches(declaration));
            EditorUtility.SetDirty(run);
        }

        /// <summary>"walk.near" → "Near": the namespace is the template's, not the designer's.</summary>
        public static string Pretty(string parameterName)
        {
            if (string.IsNullOrEmpty(parameterName))
                return "";
            int dot = parameterName.LastIndexOf('.');
            return ObjectNames.NicifyVariableName(dot >= 0 ? parameterName.Substring(dot + 1) : parameterName);
        }

        // --- context -----------------------------------------------------------------------

        /// <summary>
        /// The registry of entry type <paramref name="entryType"/> a node's pickers read: the nearest
        /// ancestor group's context field of that registry type (a Level carries its manifest), else
        /// the tree's own Data. This is how a template stays level-agnostic and its step still picks
        /// from the level it sits in.
        /// </summary>
        public static StateTreeRegistryAsset ContextRegistry(StateTreeAsset tree, StateTreeSchema schema,
            StateTreeNodeAsset node, Type entryType)
        {
            if (entryType == null)
                return null;
            for (StateTreeNodeAsset at = node; at != null; at = StateTreeEditorOps.FindParent(tree, at))
            {
                StateTreeTaskAsset context = ContextTask(KindOf(schema, at), at);
                if (context == null)
                    continue;
                foreach (FieldInfo field in context.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (typeof(StateTreeRegistryAsset).IsAssignableFrom(field.FieldType)
                        && field.GetValue(context) is StateTreeRegistryAsset registry
                        && registry != null && entryType.IsAssignableFrom(registry.entryType))
                        return registry;
                }
            }
            if (tree != null)
            {
                foreach (StateTreeRegistryAsset registry in tree.registries)
                {
                    if (registry != null && entryType.IsAssignableFrom(registry.entryType))
                        return registry;
                }
            }
            return null;
        }

        /// <summary>The registries a step's row parameter offers: the context's first, then the
        /// template's own Data (which is what the template resolves against at runtime).</summary>
        public static List<StateTreeRegistryAsset> RowSources(StateTreeAsset tree, StateTreeSchema schema,
            StateTreeNodeAsset node, StateTreeAsset template, Type entryType)
        {
            var sources = new List<StateTreeRegistryAsset>();
            if (entryType == null)
                return sources;
            if (template != null)
            {
                foreach (StateTreeRegistryAsset registry in template.registries)
                {
                    if (registry != null && entryType.IsAssignableFrom(registry.entryType) && !sources.Contains(registry))
                        sources.Add(registry);
                }
            }
            StateTreeRegistryAsset context = ContextRegistry(tree, schema, node, entryType);
            if (context != null && !sources.Contains(context))
                sources.Insert(0, context);
            return sources;
        }

        /// <summary>A placement row of the node's level, by the id a [PlacementId] field stores.</summary>
        public static LevelObjectDef Placement(StateTreeAsset tree, StateTreeSchema schema,
            StateTreeNodeAsset node, string id, out LevelObjectRegistry manifest)
        {
            manifest = ContextRegistry(tree, schema, node, typeof(LevelObjectDef)) as LevelObjectRegistry;
            if (manifest == null || string.IsNullOrEmpty(id))
                return null;
            return manifest.FindById(id) as LevelObjectDef ?? manifest.FindByName(id) as LevelObjectDef;
        }

        /// <summary>What a step's row in the tree says after its name: its parameters' values,
        /// placement ids shown by their row's name.</summary>
        public static string Summary(StateTreeAsset tree, StateTreeSchema schema, StateTreeNodeAsset node)
        {
            StateTreeSchemaKind kind = KindOf(schema, node);
            if (kind == null || !kind.isTemplate)
                return "";
            var parts = new List<string>();
            foreach (StateTreeStepParameter parameter in Parameters(kind, node))
            {
                if (parameter.declaration.kind != GraphTaskParameterKind.String || parameter.isKey)
                    continue;
                string value = parameter.stringValue;
                if (string.IsNullOrEmpty(value))
                    continue;
                if (parameter.isPlacement)
                {
                    LevelObjectDef row = Placement(tree, schema, node, value, out _);
                    value = row != null ? row.name : value;
                }
                parts.Add(value);
            }
            return string.Join(" · ", parts);
        }

        // --- factory -----------------------------------------------------------------------

        /// <summary>
        /// A new domain node under <paramref name="parent"/>: the state, its roleKind, and what the
        /// kind is made of — one sub-tree task of the template, or the group's context task. Refused
        /// (null) when the rules do not allow it there. Undoable; the caller groups.
        /// </summary>
        public static StateTreeNodeAsset AddNode(StateTreeAsset tree, StateTreeSchema schema,
            StateTreeNodeAsset parent, string kindName)
        {
            StateTreeSchemaKind kind = schema != null ? schema.Kind(kindName) : null;
            if (tree == null || kind == null || parent == null || !CanHold(schema, parent, kind.kind))
                return null;

            const string undo = "Add " + "Node";
            string id = (string.IsNullOrEmpty(kind.idPrefix) ? kind.kind + "." : kind.idPrefix)
                + kind.Label.ToLowerInvariant().Replace(' ', '-');
            StateTreeNodeAsset node = StateTreeEditorOps.CreateNode(tree, parent, id, kind.Label, undo);
            if (node == null)
                return null;
            Undo.RecordObject(node, undo);
            node.roleKind = kind.kind;
            if (kind.isTemplate)
                StateTreeEditorOps.CreateSubTreeTask(tree, node, kind.template, undo);
            else if (kind.ContextType() != null)
            {
                StateTreeTaskAsset context = StateTreeEditorOps.CreateTask(tree, node, kind.ContextType(), undo);
                if (context != null)
                    context.blocking = false;
            }
            EditorUtility.SetDirty(node);
            return node;
        }
    }
}
