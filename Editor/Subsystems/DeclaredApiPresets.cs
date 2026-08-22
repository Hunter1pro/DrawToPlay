using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE DECLARED API AS PICKER ENTRIES (M38.1b) — "Ask · inventory · bag.add", "When ·
    /// clock.dawn", "Show · hud", "Say To · hud · pulse" — each one a factory for the ordinary
    /// library task or condition with its key, row or verb already set.
    ///
    /// The State Tree window's picker lists these beside the library types, so a state can say
    /// "ask the bag for a medkit" by picking, exactly as a graph node can. Same defs, same
    /// reads (<see cref="DeclaredApi"/>), a different surface: the circle's third place.
    /// </summary>
    public static class DeclaredApiPresets
    {
        public sealed class Preset
        {
            public string displayName;
            public string category;
            public string description;
            public Func<ScriptableObject> make;

            /// <summary>Stable across sessions, for favourites and recents.</summary>
            public string key;
        }

        /// <summary>Every preset that yields a <paramref name="baseType"/> — tasks or conditions.</summary>
        public static List<Preset> For(Type baseType)
        {
            var presets = new List<Preset>();
            if (baseType == null)
                return presets;
            if (baseType.IsAssignableFrom(typeof(RequestTask)))
                Asks(presets);
            if (baseType.IsAssignableFrom(typeof(ShowUiTask)))
                Shows(presets);
            if (baseType.IsAssignableFrom(typeof(UiCallTask)))
                Says(presets);
            if (baseType.IsAssignableFrom(typeof(AnnouncementCondition)))
                Whens(presets);
            return presets;
        }

        // ---- the four shapes -------------------------------------------------------------

        private static void Asks(List<Preset> into)
        {
            foreach (string defName in DeclaredApi.Subsystems())
            {
                ServiceDef def = DeclaredApi.Subsystem(defName);
                if (def == null)
                    continue;
                foreach (string key in DeclaredApi.RequestKeys(defName))
                {
                    if (string.IsNullOrEmpty(key))
                        continue;
                    ServiceRequest row = DeclaredApi.Request(defName, key);
                    string what = row != null && row.namesRowOf != null
                        ? " — value: a row of " + row.namesRowOf.name
                        : row != null && !string.IsNullOrEmpty(row.description) ? " — " + row.description : "";
                    into.Add(new Preset
                    {
                        displayName = "Ask · " + def.serviceName + " · " + key,
                        category = "Subsystems/Ask/" + def.serviceName,
                        description = "Write '" + key + "' for " + def.name + " to serve" + what + ".",
                        key = "preset:ask:" + def.name + ":" + key,
                        make = () => Ask(key)
                    });
                }
            }
        }

        private static void Shows(List<Preset> into)
        {
            foreach (string rowName in DeclaredApi.UiRows())
            {
                if (string.IsNullOrEmpty(rowName))
                    continue;
                string row = rowName;
                into.Add(new Preset
                {
                    displayName = "Show · " + row,
                    category = "Subsystems/Show",
                    description = "Put the '" + row + "' UI row on screen while this state runs.",
                    key = "preset:show:" + row,
                    make = () => Show(row)
                });
            }
        }

        private static void Says(List<Preset> into)
        {
            foreach (string rowName in DeclaredApi.UiRows())
            {
                if (string.IsNullOrEmpty(rowName))
                    continue;
                foreach (string verb in DeclaredApi.Verbs(rowName))
                {
                    if (string.IsNullOrEmpty(verb))
                        continue;
                    string row = rowName, said = verb;
                    into.Add(new Preset
                    {
                        displayName = "Say To · " + row + " · " + said,
                        category = "Subsystems/Say To/" + row,
                        description = "Call '" + said + "' on the '" + row + "' row's skins — a verb they declare.",
                        key = "preset:say:" + row + ":" + said,
                        make = () => SayTo(row, said)
                    });
                }
            }
        }

        private static void Whens(List<Preset> into)
        {
            foreach (string defName in DeclaredApi.Subsystems())
            {
                ServiceDef def = DeclaredApi.Subsystem(defName);
                if (def == null)
                    continue;
                foreach (string key in DeclaredApi.AnnouncementKeys(defName))
                {
                    if (string.IsNullOrEmpty(key))
                        continue;
                    StateTreeContextKind scope = def.scope;
                    into.Add(new Preset
                    {
                        displayName = "When · " + def.serviceName + " · " + key,
                        category = "Subsystems/When Announced/" + def.serviceName,
                        description = "Fires once each time " + def.name + " announces '" + key + "'.",
                        key = "preset:when:" + def.name + ":" + key,
                        make = () => When(key, scope)
                    });
                }
            }
        }

        // ---- the factories: what the builder calls too, so it says what the picker says ----

        public static RequestTask Ask(string key, string value = "1")
        {
            var task = ScriptableObject.CreateInstance<RequestTask>();
            task.key = key;
            task.value = value;
            return task;
        }

        public static ShowUiTask Show(string rowName)
        {
            var task = ScriptableObject.CreateInstance<ShowUiTask>();
            Point(task.ui, rowName);
            return task;
        }

        public static UiCallTask SayTo(string rowName, string verb, string argument = "")
        {
            var task = ScriptableObject.CreateInstance<UiCallTask>();
            Point(task.ui, rowName);
            task.verb = verb;
            task.argument = argument ?? "";
            return task;
        }

        public static AnnouncementCondition When(string key, StateTreeContextKind scope)
        {
            var condition = ScriptableObject.CreateInstance<AnnouncementCondition>();
            condition.key = key;
            condition.scope = scope;
            return condition;
        }

        /// <summary>A UI row reference, both halves: the name the runtime reads and the id a
        /// rename follows.</summary>
        private static void Point(StateTreeEntryRef<UiDef> reference, string rowName)
        {
            UiDef row = DeclaredApi.UiRow(rowName);
            reference.entryName = rowName ?? "";
            reference.entryId = row != null ? row.id : "";
        }
    }
}
