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

        /// <summary>The row type the parameter names: a typed entry reference's, or a [RowName] string's.</summary>
        public Type entryType
        {
            get
            {
                if (field == null)
                    return null;
                if (field.FieldType == typeof(string)
                    && field.GetCustomAttribute<RowNameAttribute>(true) is RowNameAttribute rowName)
                    return rowName.entryType;
                if (!field.FieldType.IsGenericType
                    || field.FieldType.GetGenericTypeDefinition() != typeof(StateTreeEntryRef<>))
                    return null;
                return field.FieldType.GetGenericArguments()[0];
            }
        }

        /// <summary>A key: a key field, or a [KeyName] string.</summary>
        public bool isKey => field != null && (field.FieldType == typeof(StateTreeKeyField)
            || field.FieldType == typeof(string) && field.IsDefined(typeof(KeyNameAttribute), true));

        public bool isTag => field != null && field.FieldType == typeof(string)
            && field.IsDefined(typeof(WorldTagAttribute), true);

        /// <summary>An enum: picked from its values, kept as the value's name.</summary>
        public bool isEnum => field != null && field.FieldType.IsEnum;

        /// <summary>Empty is a choice: the field says what none means.</summary>
        public bool mayBeNone => field != null && field.IsDefined(typeof(NoneMeansAttribute), true);

        /// <summary>The step does not pick it: it follows from something the step holds.</summary>
        public bool isDerived => declaration != null && declaration.IsDerived;

        /// <summary>What the bound field is, by the "never a string" rule — a String parameter bound
        /// into a <see cref="StateTreeStringKind.Free"/> field (or into nothing) has no picker.</summary>
        public StateTreeStringKind stringKind
        {
            get
            {
                if (field == null)
                    return StateTreeStringKind.Free;
                if (isKey || entryType != null || isPlacement || isEnum)
                    return StateTreeStringKind.Picked;
                StateTreeStringKind kind = StateTreeStringRule.Classify(field);
                return kind == StateTreeStringKind.NotString ? StateTreeStringKind.Free : kind;
            }
        }
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
                    $"Holds a dev state '{node.nodeId}', which {schema.modeName} mode hides."));

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
                if (!parameter.isDerived && !parameter.mayBeNone && (parameter.isPlacement || parameter.entryType != null)
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
            float number, string entryId, string undoName, string keyId = "")
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
            row.keyId = keyId ?? "";
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

        /// <summary>
        /// The catalog of <paramref name="entryType"/> that the groups enclosing <paramref name="node"/>
        /// name — a Level's manifest field, or a row a Zone's context points at that carries one (a zone row's
        /// objective rows). What a picker narrows to; null when no enclosing group says.
        /// </summary>
        public static StateTreeRegistryAsset EnclosingRegistry(StateTreeAsset tree, StateTreeSchema schema,
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
                    object value = field.GetValue(context);
                    if (value is StateTreeRegistryAsset registry && registry != null
                        && entryType.IsAssignableFrom(registry.entryType))
                        return registry;
                    if (value is IStateTreeEntryRef reference && !string.IsNullOrEmpty(reference.EntryName)
                        && field.FieldType.IsGenericType)
                    {
                        Type rowType = field.FieldType.GetGenericArguments()[0];
                        StateTreeRegistryEntry row = StateTreeRowOffers.Find(
                            StateTreeRowOffers.For(context, rowType, out _), reference.EntryName);
                        if (row == null)
                            continue;
                        foreach (FieldInfo rowField in row.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (rowField.GetValue(row) is StateTreeRegistryAsset carried && carried != null
                                && entryType.IsAssignableFrom(carried.entryType))
                                return carried;
                        }
                    }
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
            if (sources.Count == 0)
                sources.AddRange(StateTreeRowOffers.For(template != null ? template : (UnityEngine.Object)tree, entryType, out _));
            return sources;
        }

        // --- derived parameters --------------------------------------------------------------

        /// <summary>What a derived parameter's value is for this step, and where it came from (a
        /// sentence for the inspector). Null value: it cannot be derived (its source is missing).</summary>
        public static string DerivedValue(StateTreeAsset tree, StateTreeSchema schema, StateTreeNodeAsset node,
            StateTreeSchemaKind kind, StateTreeStepParameter parameter, out string why, out StateTreeRegistryEntry row)
        {
            why = "";
            row = null;
            GraphTaskParameterDerivation derived = parameter.declaration.derived;
            if (derived == null)
                return null;
            switch (derived.source)
            {
                case GraphTaskParameterDerivation.Source.Template:
                    why = "the template's own — no step changes it";
                    return parameter.declaration.stringValue ?? "";
                case GraphTaskParameterDerivation.Source.Context:
                    for (StateTreeNodeAsset at = StateTreeEditorOps.FindParent(tree, node); at != null;
                         at = StateTreeEditorOps.FindParent(tree, at))
                    {
                        StateTreeSchemaKind atKind = KindOf(schema, at);
                        StateTreeTaskAsset context = ContextTask(atKind, at);
                        FieldInfo field = context != null
                            ? context.GetType().GetField(derived.member, BindingFlags.Public | BindingFlags.Instance)
                            : null;
                        if (field == null)
                            continue;
                        why = "from the " + atKind.Label + " '" + (string.IsNullOrEmpty(at.displayName) ? at.nodeId : at.displayName)
                            + "' (its " + ObjectNames.NicifyVariableName(derived.member).ToLowerInvariant() + ")";
                        return ValueOf(field.GetValue(context));
                    }
                    why = "from the enclosing group's " + derived.member + " — no enclosing group carries one";
                    return null;
                case GraphTaskParameterDerivation.Source.Row:
                    StateTreeStepParameter source = null;
                    foreach (StateTreeStepParameter candidate in Parameters(kind, node))
                    {
                        if (candidate.declaration.id == derived.parameterId)
                            source = candidate;
                    }
                    if (source == null || source.entryType == null)
                    {
                        why = "from a row parameter the template no longer has";
                        return null;
                    }
                    string label = Pretty(source.declaration.name);
                    if (string.IsNullOrEmpty(source.stringValue))
                    {
                        why = "from the " + label + " row — none is picked yet";
                        return "";
                    }
                    foreach (StateTreeRegistryAsset registry in RowSources(tree, schema, node, kind.template, source.entryType))
                    {
                        row = registry.FindByName(source.stringValue);
                        if (row != null)
                            break;
                    }
                    FieldInfo member = row != null
                        ? row.GetType().GetField(derived.member, BindingFlags.Public | BindingFlags.Instance)
                        : null;
                    if (member == null)
                    {
                        why = row == null
                            ? "from the " + label + " row '" + source.stringValue + "', which is no row"
                            : "from the " + label + " row's " + derived.member + " — the row has no such field";
                        return null;
                    }
                    why = "from the " + label + " '" + row.name + "' (its "
                        + ObjectNames.NicifyVariableName(derived.member).ToLowerInvariant() + ")";
                    return ValueOf(member.GetValue(row));
                default:
                    return null;
            }
        }

        private static string ValueOf(object value)
        {
            switch (value)
            {
                case null:
                    return "";
                case string text:
                    return text;
                case StateTreeKeyField key:
                    return key.text ?? "";
                case IStateTreeEntryRef entry:
                    return entry.EntryName ?? "";
                default:
                    return value.ToString();
            }
        }

        /// <summary>Write every derived parameter of every step of a tree into its override, so the
        /// runtime — which only reads overrides — runs what the step derives. Idempotent; returns how
        /// many overrides changed. A build calls this after it authors rows; the inspector after an edit.</summary>
        public static int ApplyDerived(StateTreeAsset tree, StateTreeSchema schema)
        {
            int changed = 0;
            if (tree == null || schema == null)
                return 0;
            foreach (StateTreeNodeAsset node in StateTreeEditorOps.CollectNodes(tree))
                changed += ApplyDerived(tree, schema, node);
            return changed;
        }

        public static int ApplyDerived(StateTreeAsset tree, StateTreeSchema schema, StateTreeNodeAsset node)
        {
            StateTreeSchemaKind kind = KindOf(schema, node);
            int changed = ApplyRest(kind, node);
            RunSubTreeTask run = kind != null && kind.isTemplate ? TemplateTask(kind, node) : null;
            if (run == null)
                return changed;
            changed += ApplyParameters(tree, schema, node, kind, run);
            return changed + ApplyRoutes(tree, schema, node, kind);
        }

        /// <summary>A kind that rests holds where its run ends instead of going on to its next sibling; one
        /// that ends its parent goes on after the parent.</summary>
        private static int ApplyRest(StateTreeSchemaKind kind, StateTreeNodeAsset node)
        {
            if (kind == null || node == null)
                return 0;
            StateTreeCompletionFlow flow = FlowOf(kind);
            if (node.completionFlow == flow)
                return 0;
            Undo.RecordObject(node, "Derive Flow");
            node.completionFlow = flow;
            EditorUtility.SetDirty(node);
            return 1;
        }

        /// <summary>Where a node of <paramref name="kind"/> goes when its run ends.</summary>
        public static StateTreeCompletionFlow FlowOf(StateTreeSchemaKind kind)
        {
            if (kind != null && kind.rests)
                return StateTreeCompletionFlow.Hold;
            return kind != null && kind.endsParent ? StateTreeCompletionFlow.EndsParent : StateTreeCompletionFlow.NextSibling;
        }

        /// <summary>The key a routed node's answer lands on — its <see cref="StateTreeSchemaKind.routeBy"/>
        /// parameter's value — or empty.</summary>
        public static string RouteKey(StateTreeSchemaKind kind, StateTreeNodeAsset node)
        {
            if (kind == null || string.IsNullOrEmpty(kind.routeBy))
                return "";
            foreach (StateTreeStepParameter parameter in Parameters(kind, node))
            {
                if (parameter.declaration.name == kind.routeBy)
                    return parameter.stringValue;
            }
            return "";
        }

        /// <summary>The board a routed node's answer is read on — its <see cref="StateTreeSchemaKind.routeScopeBy"/>
        /// parameter's value, a <see cref="StateTreeContextKind"/> by name — or null for the node's own board.</summary>
        public static StateTreeContextKind? RouteScope(StateTreeSchemaKind kind, StateTreeNodeAsset node)
        {
            if (kind == null || string.IsNullOrEmpty(kind.routeScopeBy))
                return null;
            foreach (StateTreeStepParameter parameter in Parameters(kind, node))
            {
                if (parameter.declaration.name == kind.routeScopeBy
                    && System.Enum.TryParse(parameter.stringValue, out StateTreeContextKind scope))
                    return scope;
            }
            return null;
        }

        /// <summary>
        /// A routed node's ways out, one per child: the i-th child when the answer key holds i. They
        /// follow from the children and the answer, so they are written, never authored — and a
        /// branch added or removed rewrites them. Returns 1 when they changed.
        /// </summary>
        private static int ApplyRoutes(StateTreeAsset tree, StateTreeSchema schema, StateTreeNodeAsset node,
            StateTreeSchemaKind kind)
        {
            if (kind == null || string.IsNullOrEmpty(kind.routeBy))
                return 0;
            string key = RouteKey(kind, node);
            StateTreeContextKind? scope = RouteScope(kind, node);
            List<StateTreeNodeAsset> branches = DomainChildren(schema, node);
            if (string.IsNullOrEmpty(key))
                branches.Clear();
            bool same = node.transitions.Count == branches.Count;
            for (int i = 0; same && i < branches.Count; i++)
            {
                StateTreeTransition transition = node.transitions[i];
                same = transition != null && !transition.checkWhileRunning
                    && transition.targetNodeId == branches[i].nodeId
                    && IsRoute(transition.condition, key, scope, i);
            }
            if (same)
                return 0;
            const string undo = "Route Answers";
            for (int i = node.transitions.Count - 1; i >= 0; i--)
                StateTreeEditorOps.RemoveTransition(tree, node, i, undo);
            for (int i = 0; i < branches.Count; i++)
            {
                StateTreeEditorOps.AddTransition(tree, node, branches[i].nodeId, undo);
                StateTreeEditorOps.SetTransitionCondition(tree, node, node.transitions[i], Route(key, scope, i), undo);
            }
            return 1;
        }

        /// <summary>The i-th answer: the key holds i, on the node's own board or on the scope it names.</summary>
        private static StateTreeConditionAsset Route(string key, StateTreeContextKind? scope, int i)
        {
            if (scope.HasValue)
            {
                var onScope = ScriptableObject.CreateInstance<ContextNumberCompareCondition>();
                onScope.scope = scope.Value;
                onScope.key = new StateTreeKeyField(key);
                onScope.op = BlackboardCompareCondition.Operator.Equal;
                onScope.value = i;
                return onScope;
            }
            var answer = ScriptableObject.CreateInstance<BlackboardCompareCondition>();
            answer.key = new StateTreeKeyField(key);
            answer.op = BlackboardCompareCondition.Operator.Equal;
            answer.value = i;
            return answer;
        }

        private static bool IsRoute(StateTreeConditionAsset condition, string key, StateTreeContextKind? scope, int i)
        {
            if (scope.HasValue)
                return condition is ContextNumberCompareCondition onScope && onScope.scope == scope.Value
                    && onScope.key != null && onScope.key.text == key
                    && onScope.op == BlackboardCompareCondition.Operator.Equal && Mathf.Approximately(onScope.value, i);
            return condition is BlackboardCompareCondition compare
                && compare.key != null && compare.key.text == key
                && compare.op == BlackboardCompareCondition.Operator.Equal
                && Mathf.Approximately(compare.value, i);
        }

        private static int ApplyParameters(StateTreeAsset tree, StateTreeSchema schema, StateTreeNodeAsset node,
            StateTreeSchemaKind kind, RunSubTreeTask run)
        {
            int changed = 0;
            foreach (StateTreeStepParameter parameter in Parameters(kind, node))
            {
                if (!parameter.isDerived)
                    continue;
                if (parameter.declaration.derived.source == GraphTaskParameterDerivation.Source.Template)
                {
                    if (parameter.overrideRow != null)
                    {
                        ResetParameter(run, parameter.declaration, "Derive Parameter");
                        changed++;
                    }
                    continue;
                }
                string value = DerivedValue(tree, schema, node, kind, parameter, out _, out _);
                if (value == null)
                    continue;
                if (parameter.overridden && parameter.overrideRow.stringValue == value
                    && string.IsNullOrEmpty(parameter.overrideRow.keyId) && string.IsNullOrEmpty(parameter.overrideRow.entryId)
                    && string.IsNullOrEmpty(parameter.overrideRow.sourceParameterId))
                    continue;
                if (!parameter.overridden && string.IsNullOrEmpty(value) && string.IsNullOrEmpty(parameter.declaration.stringValue)
                    && parameter.overrideRow == null)
                    continue;
                SetParameter(run, parameter.declaration, value, 0f, "", "Derive Parameter");
                if (parameter.overrideRow != null)
                    parameter.overrideRow.sourceParameterId = "";
                changed++;
            }
            return changed;
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
                if (parameter.declaration.kind != GraphTaskParameterKind.String || parameter.isKey || parameter.isDerived)
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
            node.completionFlow = FlowOf(kind);
            EditorUtility.SetDirty(node);
            // A new branch is a new way out of the node above it.
            ApplyDerived(tree, schema, parent);
            return node;
        }
    }
}
