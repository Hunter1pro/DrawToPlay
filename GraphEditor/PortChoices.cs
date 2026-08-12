using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// TURNS A STRING PORT INTO A DROPDOWN — the one thing the public port API cannot do, and the
    /// difference between "type the name of a row and hope" and picking one.
    ///
    /// HOW IT IS POSSIBLE AT ALL. A port's inline editor is chosen by
    /// <c>CustomizableModelPropertyField.CreateDefaultFieldForType</c> from the port's TYPE and its
    /// ATTRIBUTES — and the string branch, before it settles on a TextField, looks through those
    /// attributes for <c>Unity.GraphToolkit.Editor.EnumAttribute</c> (a <c>string[] Values</c>) and
    /// builds a <see cref="UnityEngine.UIElements.DropdownField"/> from it instead. Ports really do
    /// carry attributes: <c>PortModel.Attributes</c>, filled from the builder's own
    /// <c>m_Attributes</c> list, which is what the public <c>AsTextArea</c> and <c>Delayed</c> add
    /// to. So the mechanism is there; only the door is shut, because <c>EnumAttribute</c> is
    /// internal and <c>[UnityRestricted]</c> and no builder method exposes the list.
    ///
    /// WHY REFLECTION IS ACCEPTABLE HERE. It is the same wall — and the same answer — as
    /// <see cref="ActiveStateHighlight"/>'s node tinting and
    /// <see cref="LibraryParameterPorts"/>'s enum-constant unwrapping: Graph Toolkit is 0.5.0-exp
    /// and the useful half of it is internal. What makes it safe is that EVERY failure degrades to
    /// exactly the behaviour we already had. A missing type, a renamed field, a changed
    /// constructor — each returns false and the port stays the plain text box it is today. The
    /// choices are also never the last word: the value is still a string, the bake still reads it
    /// as one, and <see cref="EntryRefValidator"/> still checks it. This makes authoring nicer; it
    /// is not load-bearing.
    ///
    /// THE LIST IS FIXED WHEN THE PORT IS DEFINED, so a node whose choices came from data must be
    /// redefined when that data changes — <see cref="Node.DefineNode"/>, the way
    /// <c>TaskGraph.RefreshReturnPins</c> already redefines Return nodes when the outputs move.
    /// </summary>
    public static class PortChoices
    {
        private static bool s_Resolved;
        private static ConstructorInfo s_EnumAttributeCtor;

        /// <summary>The change hints asked for when a node's ports move — see
        /// <see cref="RequestRebuild"/>.</summary>
        private static readonly string[] k_RebuildHints =
            { "GraphTopology", "Data", "NeedsRedraw", "Style", "Layout" };

        /// <summary>
        /// Offer <paramref name="choices"/> as a dropdown on the port this builder is about to
        /// build. Call it BEFORE <c>Build()</c>.
        /// </summary>
        /// <param name="builder">The value returned by <c>AddInputPort</c> and friends — passed as
        /// <see cref="object"/> because the concrete builder is internal and only its interfaces
        /// are nameable here.</param>
        /// <param name="choices">The values to offer, in display order. Fewer than one choice is a
        /// no-op: an empty dropdown is worse than a text box, because it cannot be corrected.</param>
        /// <returns>True when the port will be a dropdown; false when it stays a text field, which
        /// is the correct outcome on any older or newer Graph Toolkit.</returns>
        public static bool TryOffer(object builder, IReadOnlyList<string> choices)
        {
            if (builder == null || choices == null || choices.Count == 0)
                return false;

            try
            {
                if (!Resolve())
                    return false;

                object owner = AttributeOwner(builder);
                if (owner == null)
                    return false;

                FieldInfo attributes = owner.GetType().GetField("m_Attributes",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (attributes == null || !typeof(IList<Attribute>).IsAssignableFrom(
                        attributes.FieldType))
                    return false;

                if (!(attributes.GetValue(owner) is IList<Attribute> list))
                {
                    list = new List<Attribute>();
                    attributes.SetValue(owner, list);
                }

                var values = new string[choices.Count];
                for (int i = 0; i < choices.Count; i++)
                    values[i] = choices[i] ?? string.Empty;

                list.Add((Attribute)s_EnumAttributeCtor.Invoke(new object[] { values }));
                return true;
            }
            catch (Exception)
            {
                // Swallowed on purpose, and not logged: this runs while ports are being defined,
                // which happens for every node on every graph load. A console line per port per
                // reload would be a worse bug than the missing dropdown.
                return false;
            }
        }

        /// <summary>
        /// What a built port is currently offering, so a caller can tell a stale dropdown from a
        /// fresh one without redefining the node to find out.
        ///
        /// THIS IS WHY THE REFRESH EXISTS. Ports are defined while the graph model is still being
        /// assembled, and a node that is not yet attached to its graph cannot say which registries
        /// it reaches — so the first definition often has no choices at all. Comparing what a port
        /// offers against what it SHOULD offer is what lets
        /// <see cref="ChoicePortRefresh"/> redefine exactly the nodes that need it, once, instead
        /// of redefining everything on every change.
        /// </summary>
        /// <param name="port">A built input port.</param>
        /// <returns>The offered values, or null when the port is not a dropdown.</returns>
        public static IReadOnlyList<string> Offered(IPort port)
        {
            if (port == null)
                return null;

            try
            {
                if (!Resolve())
                    return null;

                object model = port;
                foreach (FieldInfo field in port.GetType().GetFields(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (field.Name.Contains("Implementation") || field.Name.Contains("Model"))
                        model = field.GetValue(port) ?? model;
                }

                return OfferedByModel(model);
            }
            catch (Exception)
            {
                // Same reasoning as TryOffer: a shape we cannot read means "no dropdown", which
                // the caller already handles.
            }
            return null;
        }

        /// <summary>
        /// The choices carried by a PORT MODEL — the same read as <see cref="Offered"/>, for a
        /// caller that reached the model another way (<see cref="ChoiceDropdownSync"/> arrives from
        /// the widget on screen, not from the public port).
        /// </summary>
        /// <param name="portModel">A <c>PortModel</c>.</param>
        /// <returns>The offered values, or null when the port is not a dropdown.</returns>
        public static IReadOnlyList<string> OfferedByModel(object portModel)
        {
            if (portModel == null)
                return null;

            try
            {
                if (!Resolve())
                    return null;

                PropertyInfo attributes = portModel.GetType().GetProperty("Attributes",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (!(attributes?.GetValue(portModel) is IEnumerable<Attribute> list))
                    return null;

                Type attributeType = s_EnumAttributeCtor.DeclaringType;
                foreach (Attribute attribute in list)
                {
                    if (attribute.GetType() != attributeType)
                        continue;
                    return attributeType.GetField("Values")?.GetValue(attribute) as string[];
                }
            }
            catch (Exception)
            {
                // Unreadable shape means "no dropdown", which every caller already handles.
            }
            return null;
        }

        /// <summary>Whether a port already offers exactly these values, in this order.</summary>
        /// <param name="port">A built input port.</param>
        /// <param name="choices">What it should offer; null or empty means "no dropdown".</param>
        /// <returns>True when nothing needs to change.</returns>
        public static bool Matches(IPort port, IReadOnlyList<string> choices)
        {
            IReadOnlyList<string> offered = Offered(port);
            int wanted = choices?.Count ?? 0;
            if (offered == null)
                return wanted == 0;
            if (offered.Count != wanted)
                return false;

            for (int i = 0; i < wanted; i++)
            {
                if (!string.Equals(offered[i], choices[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The builder object that actually HOLDS the attribute list.
        ///
        /// A typed port builder (<c>PortBuilder&lt;TData&gt;</c>, what <c>AddInputPort&lt;T&gt;</c>
        /// hands back) is a thin typed face over the untyped <c>PortBuilder</c>, which it points at
        /// with a public <c>parent</c> field; only the untyped one has <c>m_Attributes</c>. The
        /// walk is bounded rather than recursive-until-null so a future shape that loops cannot
        /// hang graph loading.
        /// </summary>
        /// <param name="builder">The builder as handed to <see cref="TryOffer"/>.</param>
        /// <returns>The object carrying <c>m_Attributes</c>, or null.</returns>
        private static object AttributeOwner(object builder)
        {
            const int maxHops = 3;
            object current = builder;
            for (int hop = 0; hop < maxHops && current != null; hop++)
            {
                if (current.GetType().GetField("m_Attributes",
                        BindingFlags.Instance | BindingFlags.NonPublic) != null)
                    return current;

                FieldInfo parent = current.GetType().GetField("parent",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                current = parent?.GetValue(current);
            }
            return null;
        }

        /// <summary>
        /// Tell the graph view a node's SHAPE changed, so it rebuilds the node instead of keeping
        /// the widgets it drew last time.
        ///
        /// WHY <see cref="Node.DefineNode"/> IS NOT ENOUGH. It updates the model — the port really
        /// does carry its new choices afterwards — but it registers no change the view listens to,
        /// so a redefined node keeps its old dropdown (old list, old value) until the graph is
        /// closed and reopened. That is indistinguishable from the refresh not working, and it is
        /// what makes switching a Registry Entry's registry look broken.
        ///
        /// The registration is the one <c>INode.DefaultColor</c>'s setter uses and this project
        /// already relies on — <c>GraphModel.CurrentGraphChangeDescription.AddChangedModel(model,
        /// hint)</c> — with <c>GraphTopology</c> rather than <c>Style</c>, because ports moving is
        /// a change of shape and not of colour.
        /// </summary>
        /// <param name="node">The node just redefined.</param>
        /// <returns>True when the view was asked to rebuild.</returns>
        public static bool RequestRebuild(Node node)
        {
            if (node == null)
                return false;

            try
            {
                const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic
                    | BindingFlags.Public;

                object model = typeof(Node)
                    .GetField("m_Implementation", any)?.GetValue(node);
                object graphModel = model?.GetType().GetProperty("GraphModel", any)?.GetValue(model);
                object description = graphModel?.GetType()
                    .GetProperty("CurrentGraphChangeDescription", any)?.GetValue(graphModel);
                if (description == null)
                    return false;

                Type hintType = typeof(Node).Assembly
                    .GetType("Unity.GraphToolkit.Editor.ChangeHint");
                if (hintType == null)
                    return false;

                MethodInfo add = null;
                foreach (MethodInfo method in description.GetType().GetMethods(any))
                {
                    if (method.Name != "AddChangedModel")
                        continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 2 && parameters[1].ParameterType == hintType
                        && parameters[0].ParameterType.IsInstanceOfType(model))
                    {
                        add = method;
                        break;
                    }
                }
                if (add == null)
                    return false;

                // EVERY hint that could mean "this node's shape moved", because which one the view
                // acts on is not documented and only one of them rebuilds a port's editor. They
                // are cheap and idempotent; a redraw asked for twice costs a frame, a redraw never
                // asked for costs the author their dropdown.
                var registered = false;
                foreach (string name in k_RebuildHints)
                {
                    object hint = hintType.GetField(name,
                        BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
                    if (hint == null)
                        continue;
                    add.Invoke(description, new[] { model, hint });
                    registered = true;
                }
                return registered;
            }
            catch (Exception)
            {
                // Same contract as the rest of this class: a shape we cannot drive means the node
                // keeps the widgets it has, which is what happened before any of this existed.
            }
            return false;
        }

        /// <summary>Cache the internal attribute's constructor, once per domain.</summary>
        private static bool Resolve()
        {
            if (s_Resolved)
                return s_EnumAttributeCtor != null;
            s_Resolved = true;

            Type type = typeof(Unity.GraphToolkit.Editor.Node).Assembly
                .GetType("Unity.GraphToolkit.Editor.EnumAttribute");
            s_EnumAttributeCtor = type?.GetConstructor(new[] { typeof(string[]) });
            return s_EnumAttributeCtor != null;
        }
    }
}
