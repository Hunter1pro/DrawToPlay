using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// The single source of truth for the correspondence between a runtime library type
    /// (<see cref="StateTreeTaskAsset"/> / <see cref="StateTreeConditionAsset"/> subclass) and the
    /// parameter ports of its graph wrapper. Both halves of M7 go through here: the node classes
    /// call <see cref="DefineParameterPorts"/> to declare their ports, and the bake calls
    /// <see cref="GetParameterFields"/> + <see cref="TryReadValue"/> to read them back into a fresh
    /// instance. Because both sides ask the same method for the same list, a port and its field
    /// cannot drift apart, and adding a task to the M6 library only costs a wrapper class that
    /// names its runtime type.
    ///
    /// THE RULE, stated once: a parameter port's name IS the field's name, byte for byte
    /// (<c>moveSpeed</c>, <c>useBlackboardRange</c>, <c>cooldownKey</c>). Only the display name is
    /// prettified. Reflection selects PUBLIC INSTANCE fields, which is precisely Unity's own
    /// serialization surface for these assets minus the private state the tasks keep for
    /// themselves (<c>m_NextAttackTime</c>, <c>m_Elapsed</c>, …), and skips anything marked
    /// <see cref="NonSerializedAttribute"/> or <see cref="HideInInspector"/>.
    ///
    /// Field ORDER is reflection order, which for these sealed, single-level classes is declaration
    /// order; nothing depends on it beyond the visual order of the ports.
    /// </summary>
    public static class LibraryParameterPorts
    {
        private static readonly Dictionary<Type, FieldInfo[]> s_Fields = new Dictionary<Type, FieldInfo[]>();
        private static readonly Dictionary<Type, ScriptableObject> s_Templates = new Dictionary<Type, ScriptableObject>();
        private static readonly FieldInfo[] k_NoFields = new FieldInfo[0];

        /// <summary>
        /// The parameter fields of a runtime library type, in declaration order — the list every
        /// parameter port on its wrapper mirrors.
        /// </summary>
        /// <param name="libraryType">A <see cref="StateTreeTaskAsset"/> or
        /// <see cref="StateTreeConditionAsset"/> subclass.</param>
        /// <returns>The public instance fields whose type a port can carry.</returns>
        public static IReadOnlyList<FieldInfo> GetParameterFields(Type libraryType)
        {
            if (libraryType == null)
                return k_NoFields;

            if (s_Fields.TryGetValue(libraryType, out var cached))
                return cached;

            var collected = new List<FieldInfo>();
            foreach (var field in libraryType.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.IsInitOnly || field.IsNotSerialized)
                    continue;
                if (field.GetCustomAttribute<HideInInspector>() != null)
                    continue;
                // A [TaskOutput] field is a RETURN, not a parameter: it gets an OUTPUT pin
                // (TaskOutputPorts), never an input port inviting a value the task overwrites.
                if (field.GetCustomAttribute<TaskOutputAttribute>() != null)
                    continue;
                if (!IsSupportedParameterType(field.FieldType))
                    continue;
                collected.Add(field);
            }

            cached = collected.ToArray();
            s_Fields[libraryType] = cached;
            return cached;
        }

        /// <summary>
        /// The runtime library type a wrapper node stands for, whichever canvas it is on — null
        /// for anything that wraps nothing (states, transitions, entries, flow). Stated once here
        /// because it is the node⟷library seam, which is what this class is for; every consumer
        /// that reasons about a node's PARAMETERS starts from it.
        /// </summary>
        /// <param name="node">Any node of either canvas.</param>
        /// <returns>The <see cref="StateTreeTaskAsset"/> / <see cref="StateTreeConditionAsset"/>
        /// subclass the node configures, or null.</returns>
        public static Type LibraryTypeOf(INode node)
        {
            switch (node)
            {
                case StateTaskBlockNode block:
                    return block.taskType;
                case TaskCallNode call:
                    return call.taskType;
                case StateTreeConditionNode condition:
                    return condition.conditionType;
                case ConditionValueNode conditionValue:
                    return conditionValue.conditionType;
                default:
                    return null;
            }
        }

        /// <summary>
        /// The concrete entry class behind a typed-reference field — the generic argument of
        /// <see cref="StateTreeEntryRef{TEntry}"/>, which is what says WHICH registry answers for
        /// the field. Null for anything that is not a typed reference.
        /// </summary>
        /// <param name="fieldType">A library field's declared type.</param>
        /// <returns>The <see cref="StateTreeRegistryEntry"/> subclass, or null.</returns>
        public static Type EntryTypeOf(Type fieldType)
        {
            for (Type walk = fieldType; walk != null; walk = walk.BaseType)
            {
                if (walk.IsGenericType
                    && walk.GetGenericTypeDefinition() == typeof(StateTreeEntryRef<>))
                    return walk.GetGenericArguments()[0];
            }
            return null;
        }

        /// <summary>
        /// Whether a field of this type can be carried by a port. The list is the set of types the
        /// Graph Toolkit inline editor can actually draw (CustomizableModelPropertyField
        /// .CreateDefaultFieldForType): anything else would produce a connector with no editor.
        /// Every field in the M6 library is covered (float, bool, int, string, Vector2, enum,
        /// WeaponDefAsset).
        /// </summary>
        /// <param name="type">Field type to test.</param>
        /// <returns>True when a port of that type is editable in the graph.</returns>
        public static bool IsSupportedParameterType(Type type)
        {
            if (type == null)
                return false;
            if (type.IsEnum)
                return true;
            if (type == typeof(StateTreeKeyField))
                return true;
            if (typeof(IStateTreeEntryRef).IsAssignableFrom(type))
                return true;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                return true;
            return type == typeof(bool) || type == typeof(int) || type == typeof(long)
                || type == typeof(float) || type == typeof(double) || type == typeof(string)
                || type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4)
                || type == typeof(Color) || type == typeof(LayerMask);
        }

        /// <summary>
        /// Declares one input port per parameter field of <paramref name="libraryType"/>, named
        /// after the field and pre-filled with the value the runtime asset itself would have.
        /// </summary>
        /// <param name="context">The port definition context of the node being defined.</param>
        /// <param name="libraryType">The runtime type the node wraps.</param>
        public static void DefineParameterPorts(Node.IPortDefinitionContext context, Type libraryType)
        {
            DefineParameterPorts(context, libraryType, null);
        }

        /// <summary>
        /// The same, with the OWNING NODE — which is what lets a typed-reference parameter be a
        /// DROPDOWN of real rows instead of a text box.
        ///
        /// A <see cref="StateTreeEntryRef{TEntry}"/> field ports as the entry NAME, and until now
        /// that meant typing it: Graph Toolkit chooses a port's editor from its type, and there is
        /// no public way to attach a picker. There IS a way to attach CHOICES
        /// (<see cref="PortChoices"/>), and the legal ones are exactly the rows the graph can
        /// reach — so Give Item's Item pin offers medkit/keycard/ration/relic rather than inviting
        /// a typo that only shows up when someone plays the conversation.
        ///
        /// The node is optional because the choices are an affordance, not the contract: pass null
        /// (or author a graph no registry points at) and every pin is the text field it has always
        /// been, still baked the same way and still checked by
        /// <see cref="EntryRefValidator"/>.
        /// </summary>
        /// <param name="context">The port definition context of the node being defined.</param>
        /// <param name="libraryType">The runtime type the node wraps.</param>
        /// <param name="owner">The node being defined, for the registry scope its graph reaches.</param>
        public static void DefineParameterPorts(Node.IPortDefinitionContext context, Type libraryType,
            Node owner)
        {
            if (context == null || libraryType == null)
                return;

            var fields = GetParameterFields(libraryType);
            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var builder = context.AddInputPort(field.Name)
                    .WithDataType(PortDataType(field.FieldType))
                    .WithDisplayName(ObjectNames.NicifyVariableName(field.Name))
                    .WithTooltip($"{libraryType.Name}.{field.Name}");

                object authoredDefault = PortValue(GetAuthoredDefault(libraryType, field));
                if (authoredDefault != null)
                    builder = builder.WithDefaultValue(authoredDefault);

                PortChoices.TryOffer(builder,
                    EntryChoicesFor(owner, field) ?? RequestChoicesFor(field));
                builder.Build();
            }
        }

        /// <summary>
        /// The DECLARED REQUESTS a [ServiceRequestKey] field may name — every public row
        /// of every ServiceDef in the project (§4g). The same reasoning as the row pin: a
        /// subsystem's API is a known list, so asking an author to retype one of its keys
        /// on a canvas invites the typo that shows up as a button doing nothing. Null for
        /// every other field, which leaves its editor alone.
        /// </summary>
        public static List<string> RequestChoicesFor(FieldInfo field)
        {
            if (field == null || field.FieldType != typeof(string)
                || !field.IsDefined(typeof(ServiceRequestKeyAttribute), true))
                return null;

            var choices = new List<string> { string.Empty };
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(ServiceDef)))
            {
                var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                for (int i = 0; def != null && i < def.requests.Count; i++)
                {
                    ServiceRequest row = def.requests[i];
                    if (row != null && !string.IsNullOrEmpty(row.key)
                        && !choices.Contains(row.key))
                        choices.Add(row.key);
                }
            }
            return choices.Count > 1 ? choices : null;
        }

        /// <summary>The row names a typed-reference field may take, from the graph's registry
        /// scope; null for every other field, which leaves its editor alone.</summary>
        /// <param name="owner">The node being defined, or null.</param>
        /// <param name="field">The library field the port mirrors.</param>
        /// <returns>The choices, or null.</returns>
        public static List<string> EntryChoicesFor(Node owner, FieldInfo field)
        {
            Type entryType = EntryTypeOf(field.FieldType);
            if (owner == null || entryType == null)
                return null;

            Graph graph;
            try
            {
                graph = owner.Graph;
            }
            catch (Exception)
            {
                // Ports are defined during graph load, when the model may still be half-built.
                return null;
            }
            if (graph == null)
                return null;

            var rows = new List<StateTreeRegistryEntry>();
            GraphRegistryScope.For(graph).CollectEntries(entryType, rows);
            if (rows.Count == 0)
                return null;

            // Empty first: a reference nobody has chosen yet is a real state, and a dropdown with
            // no way back to it would force a value on every node the moment it is dropped.
            var choices = new List<string> { string.Empty };
            for (int i = 0; i < rows.Count; i++)
            {
                if (!choices.Contains(rows[i].name))
                    choices.Add(rows[i].name);
            }
            return choices;
        }

        /// <summary>
        /// The data type a field's PORT carries. A <see cref="StateTreeKeyField"/> (M14) ports
        /// as a plain STRING — same port name, same data type as before the wrapper existed —
        /// so every graph authored against the string era keeps its ports, wires and constants
        /// (the MovedFrom lesson, applied to ports). Everything else ports as itself.
        /// </summary>
        public static Type PortDataType(Type fieldType)
        {
            if (fieldType == typeof(StateTreeKeyField))
                return typeof(string);
            // An entry reference ports as the entry NAME: the canvas authors the free-typed
            // flavor (name-only, resolved by name at StartTree) — id-wiring stays an
            // asset-editor affordance.
            if (typeof(IStateTreeEntryRef).IsAssignableFrom(fieldType))
                return typeof(string);
            return fieldType;
        }

        /// <summary>A field value as its PORT sees it: a key wrapper flattens to its authored
        /// text, everything else passes through.</summary>
        public static object PortValue(object fieldValue)
        {
            if (fieldValue is StateTreeKeyField key)
                return key.text;
            if (fieldValue is IStateTreeEntryRef entryRef)
                return entryRef.EntryName;
            return fieldValue;
        }

        /// <summary>The write half of the port⟷field seam: a string landing on a key-wrapper
        /// field goes into the wrapper's TEXT (the wrapper instance survives, a graph has no
        /// wires so its keyId stays empty); everything else is a plain SetValue.</summary>
        public static void WriteFieldValue(FieldInfo field, object target, object value)
        {
            if (field.FieldType == typeof(StateTreeKeyField))
            {
                if (!(field.GetValue(target) is StateTreeKeyField key))
                {
                    key = new StateTreeKeyField();
                    field.SetValue(target, key);
                }
                key.text = value as string ?? string.Empty;
                return;
            }

            if (typeof(IStateTreeEntryRef).IsAssignableFrom(field.FieldType))
            {
                object reference = field.GetValue(target);
                if (reference == null)
                {
                    reference = System.Activator.CreateInstance(field.FieldType);
                    field.SetValue(target, reference);
                }
                var nameField = field.FieldType.GetField("entryName");
                nameField?.SetValue(reference, value as string ?? string.Empty);
                return;
            }

            field.SetValue(target, value);
        }

        /// <summary>
        /// The value the field has on a freshly created instance of the runtime asset — the C#
        /// field initializer, which is where the M6 library keeps its authored defaults
        /// (<c>moveSpeed = 2f</c>, <c>detectRange = 6f</c>). Getting it means instantiating the
        /// ScriptableObject once per type; the instances are cached for the domain and hidden.
        /// </summary>
        /// <param name="libraryType">The runtime type being wrapped.</param>
        /// <param name="field">The field whose default is wanted.</param>
        /// <returns>The default value, or null when it cannot be read or is null itself.</returns>
        public static object GetAuthoredDefault(Type libraryType, FieldInfo field)
        {
            if (libraryType == null || field == null)
                return null;

            var template = GetTemplate(libraryType);
            if (template == null)
                return null;

            return field.GetValue(template);
        }

        /// <summary>
        /// Reads the effective value of an input port: the value wired into it when it is
        /// connected to a constant or a graph variable, otherwise the value typed into the port's
        /// own field. Mirrors the resolution order of the canonical sample's importer
        /// (VisualNovelDirectorImporter.GetInputPortValue) but stays type-erased so the bake can
        /// drive it from a <see cref="FieldInfo"/>.
        /// </summary>
        /// <param name="port">The input port to read.</param>
        /// <param name="valueType">The type to read it as — normally the mirrored field's type.</param>
        /// <param name="value">Receives the value on success.</param>
        /// <returns>True when a value of <paramref name="valueType"/> could be read.</returns>
        public static bool TryReadValue(IPort port, Type valueType, out object value)
        {
            value = null;
            if (port == null || valueType == null)
                return false;

            if (port.IsConnected)
            {
                var source = port.FirstConnectedPort;
                switch (source?.GetNode())
                {
                    case IConstantNode constantNode:
                        return TryInvokeTryGet(typeof(IConstantNode), "TryGetValue", constantNode, valueType, out value);
                    case IVariableNode variableNode:
                        var variable = variableNode.Variable;
                        if (variable == null)
                            return false;
                        return TryInvokeTryGet(typeof(IVariable), "TryGetDefaultValue", variable, valueType, out value);
                    default:
                        // Wired to something that carries no value (a condition node on a condition
                        // port, a flow wire): the caller resolves those by walking the connection.
                        return false;
                }
            }

            if (TryInvokeTryGet(typeof(IPort), "TryGetValue", port, valueType, out value))
                return true;

            // Enum constants ride in Unity.GraphToolkit.EnumValueReference (an INTERNAL
            // type, hence the reflection) — unwrap when the enum-typed get refuses.
            if (valueType.IsEnum && ResolveEnumReference()
                && TryInvokeTryGet(typeof(IPort), "TryGetValue", port, s_EnumRefType,
                    out object wrapped)
                && wrapped != null && s_EnumRefValue != null)
            {
                value = Enum.ToObject(valueType, (int)s_EnumRefValue.GetValue(wrapped));
                return true;
            }

            return false;
        }

        private static Type s_EnumRefType;
        private static ConstructorInfo s_EnumRefCtor;
        private static PropertyInfo s_EnumRefValue;
        private static bool s_EnumRefResolved;

        /// <summary>Unity.GraphToolkit.EnumValueReference is internal; the whole enum seam
        /// goes through these cached reflection handles.</summary>
        private static bool ResolveEnumReference()
        {
            if (s_EnumRefResolved)
                return s_EnumRefType != null;
            s_EnumRefResolved = true;
            s_EnumRefType = typeof(Node).Assembly.GetType("Unity.GraphToolkit.EnumValueReference");
            if (s_EnumRefType != null)
            {
                s_EnumRefCtor = s_EnumRefType.GetConstructor(new[] { typeof(Enum) });
                s_EnumRefValue = s_EnumRefType.GetProperty("Value");
            }
            return s_EnumRefType != null;
        }

        /// <summary>
        /// Writes a value into an unconnected input port — the only way a graph can be built
        /// programmatically, and therefore the mechanism the M7 demo uses to assemble the zombie
        /// tree in code before baking it.
        /// </summary>
        /// <param name="port">The input port to write.</param>
        /// <param name="valueType">The port's data type.</param>
        /// <param name="value">The value to store.</param>
        /// <returns>True when the port accepted the value.</returns>
        public static bool TryWriteValue(IPort port, Type valueType, object value)
        {
            if (port == null || valueType == null)
                return false;

            var method = typeof(IPort).GetMethod("TrySetValue", BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
                return false;

            var result = method.MakeGenericMethod(valueType).Invoke(port, new[] { value });
            if (result is bool ok && ok)
                return true;

            // Enum constants ride in Unity.GraphToolkit.EnumValueReference — the enum-typed
            // set refuses, the wrapped one lands.
            if (valueType.IsEnum && value is Enum enumValue && ResolveEnumReference()
                && s_EnumRefCtor != null)
            {
                object wrapped = s_EnumRefCtor.Invoke(new object[] { enumValue });
                result = method.MakeGenericMethod(s_EnumRefType).Invoke(port, new[] { wrapped });
                return result is bool okWrapped && okWrapped;
            }

            return false;
        }

        private static bool TryInvokeTryGet(Type declaringType, string methodName, object target, Type valueType, out object value)
        {
            value = null;
            var method = declaringType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (method == null || target == null)
                return false;

            var args = new object[] { null };
            var result = method.MakeGenericMethod(valueType).Invoke(target, args);
            if (result is bool ok && ok)
            {
                value = args[0];
                return true;
            }
            return false;
        }

        private static ScriptableObject GetTemplate(Type libraryType)
        {
            if (s_Templates.TryGetValue(libraryType, out var template))
                return template;

            template = null;
            if (typeof(ScriptableObject).IsAssignableFrom(libraryType) && !libraryType.IsAbstract)
            {
                try
                {
                    template = ScriptableObject.CreateInstance(libraryType);
                    if (template != null)
                        template.hideFlags = HideFlags.HideAndDontSave;
                }
                catch (Exception)
                {
                    // Ports are defined during graph load; if Unity refuses to build an instance at
                    // that moment the node still gets its ports, they just start at the type's zero
                    // value. Swallowed on purpose: a console error per node per reload would be
                    // worse than a missing default, and the graph is still correct.
                    template = null;
                }
            }

            s_Templates[libraryType] = template;
            return template;
        }
    }
}
