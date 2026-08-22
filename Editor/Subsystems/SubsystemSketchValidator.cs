using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>One thing the sketch still has wrong — where, what, and whether it blocks.</summary>
    internal sealed class SketchFinding
    {
        public string section;
        public string message;
        public bool blocks;

        public override string ToString()
        {
            return (blocks ? "✕ " : "⚠ ") + section + ": " + message;
        }
    }

    /// <summary>
    /// WHAT A SKETCH STILL HAS WRONG (M37.2) — the validators behind every stage, pure so a test
    /// can ask them. A finding that BLOCKS stops generation; one that does not is advice the
    /// generator acts on (a catalog a picked row came from is added to declares, not demanded).
    ///
    /// The questions are the ones the runtime will ask later, asked now: is the key free, is
    /// the action a name, is the class name taken, does the catalog the value names a row of
    /// exist. Every one of them used to be answered by a recompile or a play.
    /// </summary>
    internal static class SubsystemSketchValidator
    {
        private static readonly Regex s_Word = new Regex("^[a-z][a-z0-9]*$");
        private static readonly Regex s_Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$");

        internal static List<SketchFinding> Validate(SubsystemSketch sketch)
        {
            var findings = new List<SketchFinding>();
            if (sketch == null)
                return findings;

            Name(sketch, findings);
            Requests(sketch, findings);
            Announcements(sketch, findings);
            Spawns(sketch, findings);
            Settings(sketch, findings);
            Attributes(sketch, findings);
            return findings;
        }

        internal static bool Blocks(List<SketchFinding> findings)
        {
            for (int i = 0; i < findings.Count; i++)
            {
                if (findings[i].blocks)
                    return true;
            }
            return false;
        }

        // ---- Name -------------------------------------------------------------------------

        private static void Name(SubsystemSketch sketch, List<SketchFinding> into)
        {
            string name = sketch.serviceName ?? "";
            if (name.Length == 0)
            {
                into.Add(Block("Name", "name it — one lowercase word, like 'clock'"));
                return;
            }
            if (!s_Word.IsMatch(name))
            {
                into.Add(Block("Name", "'" + name + "' is not one lowercase word — the def, the "
                    + "class and the keys are all spelt from it"));
                return;
            }

            // A def with this name already exists and is not the one this sketch wrote.
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(ServiceDef)))
            {
                var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && def.serviceName == name && def != sketch.generatedDef)
                {
                    into.Add(Block("Name", "a subsystem named '" + name + "' already exists ("
                        + def.name + ")"));
                    break;
                }
            }

            // THE CLASS IS WRITTEN ONCE: a type with the class's name that this sketch did not
            // write is somebody's file, and the flow never overwrites somebody's file.
            if (string.IsNullOrEmpty(sketch.generatedClassPath)
                && ServiceDef.ResolveServiceType(sketch.className) != null)
            {
                into.Add(Block("Name", "a class named " + sketch.className + " already exists — "
                    + "the flow never overwrites a class; pick another name or point the sketch at "
                    + "its file"));
            }
            if (!string.IsNullOrEmpty(sketch.capability) && !s_Identifier.IsMatch(sketch.capabilityName))
                into.Add(Block("Name", "capability '" + sketch.capability + "' is not an identifier"));
            if (!string.IsNullOrEmpty(sketch.codeNamespace)
                && !Regex.IsMatch(sketch.codeNamespace, "^[A-Za-z_][A-Za-z0-9_.]*$"))
                into.Add(Block("Name", "namespace '" + sketch.codeNamespace + "' is not one"));
        }

        // ---- Asks -------------------------------------------------------------------------

        private static void Requests(SubsystemSketch sketch, List<SketchFinding> into)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var actions = new HashSet<string>(StringComparer.Ordinal);
            AssetWireScan.Index index = AssetWireScan.Get();
            for (int i = 0; i < sketch.requests.Count; i++)
            {
                SketchRequest row = sketch.requests[i];
                string at = "Asks #" + (i + 1);
                if (row == null || string.IsNullOrEmpty(row.key))
                {
                    into.Add(Block(at, "a request needs a key — 'clock.set'"));
                    continue;
                }
                if (!keys.Add(row.key))
                    into.Add(Block(at, "key '" + row.key + "' is sketched twice"));
                if (index != null && index.requestOwners.TryGetValue(row.key, out UnityEngine.Object owner)
                    && owner != null && owner != sketch.generatedDef)
                {
                    into.Add(Block(at, "key '" + row.key + "' is already served by " + owner.name));
                }
                IReadOnlyList<string> constants = ServiceKeyCode.Owners(row.key);
                if (constants.Count > 0)
                {
                    into.Add(Warn(at, "key '" + row.key + "' is also a C# constant ("
                        + constants[0] + ") — the generated class will declare its own"));
                }
                if (string.IsNullOrEmpty(row.action) || !s_Identifier.IsMatch(row.action.Replace("-", "_")))
                    into.Add(Block(at, "action '" + row.action + "' must be a word — it becomes a const"));
                else if (!actions.Add(row.action))
                    into.Add(Block(at, "action '" + row.action + "' is sketched twice"));
                if (row.namesRowOf != null && row.namesRowOf != sketch.catalog
                    && !sketch.declares.Contains(row.namesRowOf))
                {
                    into.Add(Warn(at, "'" + row.key + "' names rows of " + row.namesRowOf.name
                        + ", which the sketch does not declare — Generate will add it"));
                }
            }
        }

        private static void Announcements(SubsystemSketch sketch, List<SketchFinding> into)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < sketch.announcements.Count; i++)
            {
                SketchAnnouncement row = sketch.announcements[i];
                string at = "Says #" + (i + 1);
                if (row == null || string.IsNullOrEmpty(row.key))
                    into.Add(Block(at, "an announcement needs a key — 'clock.dawn'"));
                else if (!keys.Add(row.key))
                    into.Add(Block(at, "'" + row.key + "' is announced twice"));
            }
        }

        private static void Spawns(SubsystemSketch sketch, List<SketchFinding> into)
        {
            for (int i = 0; i < sketch.spawns.Count; i++)
            {
                if (sketch.spawns[i] == null || string.IsNullOrEmpty(sketch.spawns[i].entryName))
                    into.Add(Block("Shows #" + (i + 1), "pick a UI row, or remove the slot"));
            }
        }

        // ---- Has & is tuned ------------------------------------------------------------

        private static void Settings(SubsystemSketch sketch, List<SketchFinding> into)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var hasTagCatalog = false;
            for (int i = 0; i < sketch.declares.Count; i++)
                if (sketch.declares[i] is WorldTagRegistry) hasTagCatalog = true;
            for (int i = 0; i < sketch.settings.Count; i++)
            {
                SketchSetting row = sketch.settings[i];
                string at = "Tuned #" + (i + 1);
                if (row == null || string.IsNullOrEmpty(row.name) || !s_Identifier.IsMatch(row.name))
                {
                    into.Add(Block(at, "a setting needs a C# name — 'secondsPerDay'"));
                    continue;
                }
                if (!names.Add(row.name))
                    into.Add(Block(at, "setting '" + row.name + "' is sketched twice"));
                if (row.kind == SketchSettingKind.Tag && !hasTagCatalog)
                    into.Add(Warn(at, "'" + row.name + "' is a tag, and the sketch declares no tag "
                        + "vocabulary — the def will have nothing to pick from"));
            }
        }

        private static void Attributes(SubsystemSketch sketch, List<SketchFinding> into)
        {
            for (int i = 0; i < sketch.attributes.Count; i++)
            {
                StateTreeEntryRef<AttributeDef> row = sketch.attributes[i];
                string at = "Has #" + (i + 1);
                if (row == null || string.IsNullOrEmpty(row.entryName))
                {
                    into.Add(Block(at, "pick an attribute, or remove the slot"));
                    continue;
                }
                StateTreeRegistryAsset home = HomeOf(row.entryId, typeof(AttributeRegistry));
                if (home == null)
                    into.Add(Block(at, "'" + row.entryName + "' is in no attribute catalog"));
                else if (!sketch.declares.Contains(home))
                    into.Add(Warn(at, "'" + row.entryName + "' comes from " + home.name
                        + ", which the sketch does not declare — Generate will add it"));
            }
        }

        /// <summary>The registry asset holding a row id, among assets of one registry type.</summary>
        internal static StateTreeRegistryAsset HomeOf(string entryId, Type registryType)
        {
            if (string.IsNullOrEmpty(entryId))
                return null;
            foreach (string guid in AssetDatabase.FindAssets("t:" + registryType.Name))
            {
                var registry = AssetDatabase.LoadAssetAtPath<StateTreeRegistryAsset>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (registry != null && registry.FindById(entryId) != null)
                    return registry;
            }
            return null;
        }

        private static SketchFinding Block(string section, string message)
        {
            return new SketchFinding { section = section, message = message, blocks = true };
        }

        private static SketchFinding Warn(string section, string message)
        {
            return new SketchFinding { section = section, message = message, blocks = false };
        }
    }
}
