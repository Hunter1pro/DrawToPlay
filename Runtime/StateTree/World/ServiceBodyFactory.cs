using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE DEF BUILDS ITS OWN BODY (M30.3) — one routine, in place of a game's per-kind switch.
    ///
    /// Every case that switch held said the same four things in nine dialects: build this prefab,
    /// give it the placement's identity, point one of its parts at the row's entry, and hold its
    /// tree until the world knows it. Said once, as data (<see cref="ServiceBody"/>), the tenth
    /// kind of object costs an asset instead of a code edit — which is the entire claim of this
    /// milestone.
    ///
    /// THE ORDER IS NOT NEGOTIABLE and is the expensive half of what was learned building the yard
    /// by hand. A citizen describes itself to the world in OnEnable, so it must be born INACTIVE
    /// (a holder that is off before the prefab exists), be given its identity while nobody is
    /// looking, and be activated only once it is in its final parent — otherwise the world learns
    /// the prefab's name, or hears about the object twice, and the tree that asks who it belongs
    /// to is refused by a world that has never heard of it.
    /// </summary>
    public static class ServiceBodyFactory
    {
        /// <summary>
        /// Build one placement into one object, or null if this def has no body.
        /// </summary>
        /// <param name="def">The def that owns the body.</param>
        /// <param name="row">The placement: identity, entry, tree, tags.</param>
        /// <param name="parent">Where it lives — the level, so unloading takes it along.</param>
        /// <param name="position">Where it stands, height already decided by the caller.</param>
        /// <param name="rotation">Which way it faces.</param>
        /// <param name="held">Hosts whose trees the caller will start once the world is whole.
        /// Null is legal and simply means nothing is held back.</param>
        public static GameObject Build(ServiceDef def, LevelObjectDef row, Transform parent,
            Vector3 position, Quaternion rotation, List<StateTreeContextHost> held = null)
        {
            if (def == null || row == null || !def.body.IsThing)
                return null;

            ServiceBody body = def.body;

            var holder = new GameObject("spawning");
            holder.SetActive(false);
            if (parent != null)
                holder.transform.SetParent(parent, false);

            GameObject view = UnityEngine.Object.Instantiate(body.prefab, holder.transform);
            view.name = string.IsNullOrEmpty(row.name)
                ? def.name + " " + row.entryName
                : row.name;

            // EVERY COPY MINTS ITS OWN. A stable id is authored once and kept forever, which is
            // right for an authored object and wrong for a copy: the world takes the second
            // arrival for the first one moving house and forgets the one it replaced.
            WorldObjectBehaviour[] citizens =
                view.GetComponentsInChildren<WorldObjectBehaviour>(true);
            for (int i = 0; i < citizens.Length; i++)
                citizens[i].stableId = "";

            // WHICH PART IS THE PLACEMENT. A composed body carries several citizens — a
            // character, a talker, an ability host — and exactly ONE of them may wear the row's
            // id: the world indexes by id and the second holder of one id evicts the first. The
            // def names it, because the def is what knows how its body is assembled.
            // WHAT IT IS, kept on the object: the def spawned this body, so the body can be
            // asked what def it is — which is what lets a tree standing on a door read the
            // door's own API instead of being told about doors in advance (M30.4).
            view.AddComponent<ServiceBodyBinding>().def = def;

            WorldObjectBehaviour self = Identity(view, citizens, body.identityPart);
            if (self != null)
            {
                // THE PLACEMENT'S ID IS THE CITIZEN'S: one prefab, many placements, and a save
                // that says "this row is gone" has to mean something on the next load.
                self.stableId = row.id;
                if (body.wearsEntryName && !string.IsNullOrEmpty(row.entryName))
                    self.entryName = body.entryNamePrefix + row.entryName;
                Expose(self, view, body);
            }

            // WHAT THIS KIND IS, from the def (M31): the tags every body it builds wears. They
            // used to be EnsureTag calls inside the components — a supply of tags that no map
            // could see and no author could change without opening a script.
            for (int i = 0; i < body.tags.Count; i++)
            {
                string tag = body.tags[i];
                if (string.IsNullOrEmpty(tag))
                    continue;
                for (int c = 0; c < citizens.Length; c++)
                    citizens[c].EnsureTag(tag);
            }

            // AND WHAT THIS ONE IS: arrow targets, kill filters and zones are all found by tag,
            // and this is the last moment before OnEnable registers them.
            if (row.tags != null)
            {
                for (int t = 0; t < row.tags.Count; t++)
                {
                    string tag = row.tags[t] != null ? row.tags[t].tag : null;
                    if (string.IsNullOrEmpty(tag))
                        continue;
                    for (int c = 0; c < citizens.Length; c++)
                        citizens[c].EnsureTag(tag);
                }
            }

            Link(view, body, row);
            Number(view, def, row);
            Tint(view, def, row);

            var host = view.GetComponent<StateTreeContextHost>();
            if (host != null && body.mind != ServiceBodyMind.None)
            {
                Mind(host, row);
                if (body.mind == ServiceBodyMind.HeldForTheWorld)
                {
                    host.autoStart = false;
                    held?.Add(host);
                }
            }

            // Out of the holder and into the level, THEN live: reparenting an active object
            // would have fired OnEnable in the holder's place rather than the row's.
            view.transform.SetParent(parent, false);
            view.transform.SetPositionAndRotation(position, rotation);
            view.SetActive(true);
            Discard(holder);
            return view;
        }

        /// <summary>
        /// The row's mind AND its arguments — the tree it runs, then this placement's values for
        /// the parameters that tree declares.
        ///
        /// COPIED row by row, never aliased: registry rows are shared authored data, and a host
        /// holding the originals would let a live inspector edit write the asset.
        /// </summary>
        public static void Mind(StateTreeContextHost host, LevelObjectDef row)
        {
            if (host == null || row == null)
                return;
            if (row.tree != null)
                host.tree = row.tree;

            if (row.parameters == null || row.parameters.isEmpty)
                return;

            List<GraphTaskParameterOverride> arguments = row.parameters.values;
            host.parameterOverrides.Clear();
            for (int i = 0; i < arguments.Count; i++)
            {
                GraphTaskParameterOverride argument = arguments[i];
                if (argument == null)
                    continue;
                host.parameterOverrides.Add(new GraphTaskParameterOverride
                {
                    name = argument.name,
                    enabled = argument.enabled,
                    floatValue = argument.floatValue,
                    stringValue = argument.stringValue,
                    id = argument.id,
                    keyId = argument.keyId,
                    sourceParameterId = argument.sourceParameterId,
                    entryId = argument.entryId
                });
            }
        }

        /// <summary>The citizen the row is ABOUT — named by the def, or the first one on the
        /// root when the body is a single part.</summary>
        private static WorldObjectBehaviour Identity(GameObject view,
            WorldObjectBehaviour[] citizens, string named)
        {
            if (!string.IsNullOrEmpty(named))
            {
                for (int i = 0; i < citizens.Length; i++)
                {
                    Type type = citizens[i].GetType();
                    if (type.Name == named || type.FullName == named)
                        return citizens[i];
                }
                Warn(view, "has no part called '" + named + "' to carry the placement's id.");
            }
            WorldObjectBehaviour first = view.GetComponent<WorldObjectBehaviour>();
            return first != null ? first : (citizens.Length > 0 ? citizens[0] : null);
        }

        /// <summary>Point the body's own parts at the row's entry — the sentence the old switch
        /// wrote nine times.</summary>
        private static void Link(GameObject view, ServiceBody body, LevelObjectDef row)
        {
            if (body.links == null || body.links.Count == 0)
                return;
            string entryName = row.entryName;
            string entryId = row.entry != null ? row.entry.entryId : "";
            // A PLACEMENT THAT NAMES NOTHING LINKS NOTHING, and keeps whatever the prefab shipped
            // with: that is how a raider drops its own keycard until a row says otherwise.
            if (string.IsNullOrEmpty(entryName) && string.IsNullOrEmpty(entryId))
                return;

            for (int i = 0; i < body.links.Count; i++)
            {
                ServiceBodyLink link = body.links[i];
                if (link == null || string.IsNullOrEmpty(link.component)
                    || string.IsNullOrEmpty(link.field))
                    continue;

                Component part = PartOf(view, link.component);
                if (part == null)
                {
                    Warn(view, "has no part called '" + link.component + "' to link.");
                    continue;
                }

                FieldInfo field = part.GetType().GetField(link.field,
                    BindingFlags.Public | BindingFlags.Instance);
                if (field == null)
                {
                    Warn(view, "'" + link.component + "' has no field '" + link.field + "'.");
                    continue;
                }

                if (field.FieldType == typeof(string))
                {
                    field.SetValue(part, entryName);
                    continue;
                }

                object reference = field.GetValue(part);
                if (reference == null)
                {
                    try { reference = Activator.CreateInstance(field.FieldType); }
                    catch { reference = null; }
                    if (reference == null)
                    {
                        Warn(view, "cannot fill '" + link.field + "' — it is neither a name nor a "
                            + "reference this can build.");
                        continue;
                    }
                    field.SetValue(part, reference);
                }

                // BOTH HALVES OF A REFERENCE, always: the name is what the runtime reads, the id
                // is what survives the row being renamed.
                Set(reference, "entryId", entryId);
                Set(reference, "entryName", entryName);
            }
        }

        /// <summary>
        /// WHAT THIS ONE IS WORTH (M34): the placement's attribute values, applied to the body
        /// the def just built.
        ///
        /// The BASE is what moves, not the current value — a pool starts full at the number the
        /// row gives, and anything a modifier adds later still stacks on top. A name the def
        /// does not declare is refused rather than invented: the row is picked from what the
        /// kind says it has, and a value for something else is a typo that would otherwise sit
        /// there doing nothing.
        /// </summary>
        private static void Number(GameObject view, ServiceDef def, LevelObjectDef row)
        {
            if (row.attributes == null || row.attributes.Count == 0)
                return;

            var attributes = view.GetComponentInChildren<AttributeComponent>(true);
            if (attributes == null)
            {
                Warn(view, "carries attribute values but has no AttributeComponent to put them "
                    + "on.");
                return;
            }

            for (int i = 0; i < row.attributes.Count; i++)
            {
                PlacementAttribute set = row.attributes[i];
                if (set == null || string.IsNullOrEmpty(set.attribute))
                    continue;
                if (!Declares(def, set.attribute))
                {
                    Warn(view, "sets '" + set.attribute + "', which '" + def.serviceName
                        + "' does not declare it has — the value is ignored.");
                    continue;
                }
                attributes.Ensure(set.attribute, set.value);
                attributes.SetBase(set.attribute, set.value);
                attributes.SetCurrent(set.attribute, set.value);
            }
        }

        private static bool Declares(ServiceDef def, string attribute)
        {
            for (int i = 0; def != null && i < def.attributes.Count; i++)
            {
                if (def.attributes[i] != null && def.attributes[i].Name == attribute)
                    return true;
            }
            return false;
        }

        /// <summary>Wear the colour of the row this object is an instance of, when the def says
        /// so and that row has one.</summary>
        private static void Tint(GameObject view, ServiceDef def, LevelObjectDef row)
        {
            if (!def.body.tintFromDefinition || def.registry == null)
                return;
            StateTreeRegistryEntry definition = def.registry.FindByName(row.entryName);
            if (definition == null)
                return;

            FieldInfo colour = definition.GetType().GetField("tint",
                BindingFlags.Public | BindingFlags.Instance);
            if (colour == null || colour.FieldType != typeof(Color))
                return;
            var tint = (Color)colour.GetValue(definition);
            if (tint == Color.white)
                return;

            IWorldTintable part = view.GetComponent<IWorldTintable>();
            if (part == null && !string.IsNullOrEmpty(def.body.tintPart))
            {
                // ADDED IF MISSING rather than required on the prefab: a kind that has never been
                // painted should not need its prefab opened before it can be.
                Type type = TypeNamed(def.body.tintPart);
                if (type != null && typeof(Component).IsAssignableFrom(type))
                    part = view.AddComponent(type) as IWorldTintable;
            }
            part?.SetTint(tint);
        }

        /// <summary>Expose the parts a contract may be dereferenced through — the composite body
        /// answering for the promises its pieces keep (M30.2).</summary>
        private static void Expose(WorldObjectBehaviour self, GameObject view, ServiceBody body)
        {
            if (body.exposes == null)
                return;
            for (int i = 0; i < body.exposes.Count; i++)
            {
                Component part = PartOf(view, body.exposes[i]);
                if (part != null)
                    self.Expose(part);
                else if (!string.IsNullOrEmpty(body.exposes[i]))
                    Warn(view, "has no part called '" + body.exposes[i] + "' to expose.");
            }
        }

        private static Component PartOf(GameObject view, string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;
            Component[] parts = view.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null)
                    continue;
                Type type = parts[i].GetType();
                if (type.Name == typeName || type.FullName == typeName)
                    return parts[i];
            }
            return null;
        }

        private static void Set(object reference, string field, string value)
        {
            FieldInfo info = reference.GetType().GetField(field,
                BindingFlags.Public | BindingFlags.Instance);
            if (info != null && info.FieldType == typeof(string))
                info.SetValue(reference, value ?? "");
        }

        private static Type TypeNamed(string name)
        {
            Type direct = Type.GetType(name);
            if (direct != null)
                return direct;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch { continue; }
                for (int i = 0; i < types.Length; i++)
                {
                    if (types[i].Name == name || types[i].FullName == name)
                        return types[i];
                }
            }
            return null;
        }

        private static void Warn(GameObject view, string what)
        {
            Debug.LogWarning("[ServiceBody] '" + view.name + "' " + what, view);
        }

        private static void Discard(GameObject holder)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(holder);
            else
                UnityEngine.Object.DestroyImmediate(holder);
        }
    }
}
