using System;
using System.Collections.Generic;
using UnityEditor;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// What the Level Manifest window does with presets, apart from the window: the presets under a kind, an
    /// object's own value for a setting, a new preset from an object, and the fixed part of a new object's name.
    /// </summary>
    internal static class LevelPresetEdits
    {
        /// <summary>The presets filed under <paramref name="kindName"/>, in the catalogue's order.</summary>
        internal static List<LevelObjectPresetDef> Under(LevelObjectPresetRegistry presets, string kindName)
        {
            var under = new List<LevelObjectPresetDef>();
            if (presets == null)
                return under;
            foreach (LevelObjectPresetDef preset in presets.entries)
            {
                if (preset != null && !string.IsNullOrEmpty(preset.name)
                    && string.Equals(preset.kind.entryName, kindName, StringComparison.Ordinal))
                    under.Add(preset);
            }
            return under;
        }

        /// <summary>The fixed start of a new object's name: <c>level.kind.</c>.</summary>
        internal static string NamePrefix(string levelKey, string kindName)
        {
            return (string.IsNullOrEmpty(levelKey) ? "" : levelKey + ".") + kindName + ".";
        }

        /// <summary>The part of <paramref name="name"/> after the fixed prefix — what a designer types.</summary>
        internal static string Tail(string name, string prefix)
        {
            name ??= "";
            return name.StartsWith(prefix, StringComparison.Ordinal) ? name.Substring(prefix.Length) : name;
        }

        /// <summary>The name with <paramref name="typed"/> after the fixed prefix; nothing typed keeps the name it
        /// has — the fresh number it was placed with.</summary>
        internal static string Renamed(string current, string prefix, string typed)
        {
            typed = (typed ?? "").Trim();
            return typed.Length == 0 ? current : prefix + typed;
        }

        /// <summary>The value an object runs a setting with: its own, else the preset's, else the tree's
        /// default. <paramref name="own"/> is the object's row when it set one.</summary>
        internal static GraphTaskParameterOverride Setting(LevelObjectPresetDef preset, LevelObjectDef row,
            GraphTaskParameter declaration, out GraphTaskParameterOverride own)
        {
            own = row != null ? GraphTaskParameterOverride.EnabledFor(row.parameters.values, declaration) : null;
            if (own != null)
                return own;
            GraphTaskParameterOverride core = preset != null
                ? GraphTaskParameterOverride.EnabledFor(preset.parameters.values, declaration)
                : null;
            return core ?? new GraphTaskParameterOverride
            {
                id = declaration.id, name = declaration.name, floatValue = declaration.floatValue,
                stringValue = declaration.stringValue ?? ""
            };
        }

        /// <summary>Gives the object its own value for a setting. One undo step.</summary>
        internal static void SetSetting(LevelObjectRegistry manifest, LevelObjectDef row, GraphTaskParameter declaration,
            string text, float number, string entryId, string keyId)
        {
            if (manifest == null || row == null || declaration == null)
                return;
            Undo.RecordObject(manifest, "Set " + declaration.name);
            GraphTaskParameterOverride own = row.parameters.values.Find(v => v != null && v.Matches(declaration));
            if (own == null)
            {
                own = new GraphTaskParameterOverride { id = declaration.id };
                row.parameters.values.Add(own);
            }
            own.name = declaration.name;
            own.enabled = true;
            own.stringValue = text ?? "";
            own.floatValue = number;
            own.entryId = entryId ?? "";
            own.keyId = keyId ?? "";
            EditorUtility.SetDirty(manifest);
        }

        /// <summary>The object follows the preset's value again.</summary>
        internal static void ResetSetting(LevelObjectRegistry manifest, LevelObjectDef row, GraphTaskParameter declaration)
        {
            if (manifest == null || row == null || declaration == null)
                return;
            Undo.RecordObject(manifest, "Reset " + declaration.name);
            row.parameters.values.RemoveAll(v => v != null && v.Matches(declaration));
            EditorUtility.SetDirty(manifest);
        }

        /// <summary>
        /// A new preset from an object: its preset's core with the object's current settings as the defaults
        /// (or, for an object without one, its own kind, tree, arguments and tags), under <paramref name="name"/>
        /// in the manifest's catalogue — and the object switched to it, its own values cleared (they are the new
        /// preset's now). What runs does not change. One undo step; null without a catalogue.
        /// </summary>
        internal static LevelObjectPresetDef Fork(LevelObjectRegistry manifest, LevelObjectDef row, string name)
        {
            LevelObjectPresetRegistry presets = manifest != null ? manifest.presets : null;
            if (presets == null || row == null || string.IsNullOrWhiteSpace(name))
                return null;

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.RecordObject(presets, "New Preset");
            Undo.RecordObject(manifest, "New Preset");

            LevelObjectPresetDef source = manifest.PresetOf(row);
            string id = "preset." + Guid.NewGuid().ToString("N");
            LevelObjectPresetDef made;
            if (source != null)
            {
                made = source.Copy(id, Unique(presets, name.Trim()));
                made.parameters = source.Arguments(row);
            }
            else
            {
                made = new LevelObjectPresetDef
                {
                    id = id, name = Unique(presets, name.Trim()), group = row.kind.entryName, tree = row.tree,
                    kind = new StateTreeEntryRef<LevelObjectKindDef> { entryId = row.kind.entryId, entryName = row.kind.entryName }
                };
                foreach (GraphTaskParameterOverride value in row.parameters.values)
                {
                    if (value != null)
                        made.parameters.values.Add(LevelObjectPresetDef.Copy(value));
                }
                foreach (LevelObjectTagRef tag in row.tags)
                {
                    if (tag != null)
                        made.tags.Add(new LevelObjectTagRef { tag = tag.tag, tagId = tag.tagId });
                }
                row.tree = null;
                row.tags.Clear();
            }
            presets.entries.Add(made);
            row.preset = new StateTreeEntryRef<LevelObjectPresetDef> { entryId = made.id, entryName = made.name };
            row.parameters.values.Clear();

            EditorUtility.SetDirty(presets);
            EditorUtility.SetDirty(manifest);
            Undo.SetCurrentGroupName("New Preset " + made.name);
            Undo.CollapseUndoOperations(undo);
            return made;
        }

        private static string Unique(LevelObjectPresetRegistry presets, string name)
        {
            string unique = name;
            for (int n = 2; presets.FindByName(unique) != null; n++)
                unique = name + " " + n;
            return unique;
        }
    }
}
